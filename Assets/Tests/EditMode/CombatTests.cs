using System.Collections.Generic;
using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Combat.Vitals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试替代动画事件的攻击时间轴。
    /// </summary>
    /// <remarks>
    /// 帧数据是动作手感核心，因此直接断言而非目测。跨阶段行为最关键：长帧不能让命中框卡在开启，
    /// 也不能跳过有效窗口而从未开启。
    /// </remarks>
    [TestFixture]
    public sealed class AttackTimelineTests
    {
        private const float Startup = 0.2f;
        private const float Active = 0.1f;
        private const float Recovery = 0.3f;

        private AttackDefinition _attack;

        [SetUp]
        public void CreateAttack()
        {
            _attack = ScriptableObject.CreateInstance<AttackDefinition>();

            var serialized = new SerializedObject(_attack);
            serialized.FindProperty("_startupSeconds").floatValue = Startup;
            serialized.FindProperty("_activeSeconds").floatValue = Active;
            serialized.FindProperty("_recoverySeconds").floatValue = Recovery;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void DestroyAttack() => Object.DestroyImmediate(_attack);

        [Test]
        public void BeforeBeginning_IsInactive()
        {
            var timeline = new AttackTimeline();

            Assert.AreEqual(AttackPhase.Inactive, timeline.Phase);
            Assert.IsFalse(timeline.IsRunning);
        }

        [Test]
        public void Begin_EntersStartup()
        {
            var timeline = new AttackTimeline();
            timeline.Begin(_attack);

            Assert.AreEqual(AttackPhase.Startup, timeline.Phase);
            Assert.IsTrue(timeline.IsRunning);
        }

        [Test]
        public void Timeline_PassesThroughEveryPhaseInOrder()
        {
            var timeline = new AttackTimeline();
            var observed = new List<AttackPhase>();
            timeline.PhaseChanged += observed.Add;

            timeline.Begin(_attack);
            for (int i = 0; i < 60; i++)
            {
                timeline.Tick(0.02f);
            }

            CollectionAssert.AreEqual(
                new[]
                {
                    AttackPhase.Startup,
                    AttackPhase.Active,
                    AttackPhase.Recovery,
                    AttackPhase.Inactive
                },
                observed);
        }

        [Test]
        public void HitboxOpensOnlyAfterStartup()
        {
            var timeline = new AttackTimeline();
            timeline.Begin(_attack);

            timeline.Tick(Startup * 0.5f);
            Assert.AreEqual(AttackPhase.Startup, timeline.Phase);

            timeline.Tick(Startup * 0.6f);
            Assert.AreEqual(AttackPhase.Active, timeline.Phase);
        }

        [Test]
        public void ASingleLongFrame_StillReportsTheActiveWindow()
        {
            var timeline = new AttackTimeline();
            var observed = new List<AttackPhase>();
            timeline.PhaseChanged += observed.Add;

            timeline.Begin(_attack);

            // 一帧足以跨过整个攻击。卡顿不得让有效窗口从未通知就被跳过。
            timeline.Tick(5f);

            CollectionAssert.Contains(observed, AttackPhase.Active);
            Assert.AreEqual(AttackPhase.Inactive, timeline.Phase);
        }

        [Test]
        public void Completed_IsRaisedWhenTheAttackFinishes()
        {
            var timeline = new AttackTimeline();
            int completedCount = 0;
            timeline.Completed += () => completedCount++;

            timeline.Begin(_attack);
            for (int i = 0; i < 60; i++)
            {
                timeline.Tick(0.02f);
            }

            Assert.AreEqual(1, completedCount);
            Assert.IsFalse(timeline.IsRunning);
        }

        [Test]
        public void Cancel_StopsTheAttackWithoutCompletingIt()
        {
            var timeline = new AttackTimeline();
            int completedCount = 0;
            timeline.Completed += () => completedCount++;

            timeline.Begin(_attack);
            timeline.Tick(Startup + Active * 0.5f);
            timeline.Cancel();

            Assert.IsFalse(timeline.IsRunning);

            // 取消不等于完成。等待完成以归还控制的对象必须能区分。
            Assert.AreEqual(0, completedCount);
        }

        [Test]
        public void ComboWindow_OpensOnlyDuringRecovery()
        {
            var timeline = new AttackTimeline();
            timeline.Begin(_attack);

            timeline.Tick(Startup * 0.5f);
            Assert.IsFalse(timeline.IsInComboWindow, "A combo must not chain before the hit resolves.");

            timeline.Tick(Startup * 0.6f + Active + Recovery * 0.4f);
            Assert.IsTrue(timeline.IsInComboWindow);
        }

        [Test]
        public void NormalizedTime_RunsFromZeroToOne()
        {
            var timeline = new AttackTimeline();
            timeline.Begin(_attack);

            Assert.AreEqual(0f, timeline.NormalizedTime, 0.001f);

            timeline.Tick(_attack.TotalSeconds * 0.5f);
            Assert.AreEqual(0.5f, timeline.NormalizedTime, 0.05f);
        }
    }

    /// <summary>
    /// 测试决定命中能否打断的架势资源池。
    /// </summary>
    /// <remarks>
    /// 需要保护的规则是破防不能连续触发。否则对硬直目标连击可让其完全失去战斗能力，
    /// 这区分高要求游戏与不公平游戏。
    /// </remarks>
    [TestFixture]
    public sealed class PoisePoolTests
    {
        private const float Frame = 1f / 60f;
        private const float Tolerance = 0.001f;

        [Test]
        public void PartialPoiseDamage_DoesNotBreak()
        {
            var poise = new PoisePool(100f, regenPerSecond: 20f, breakRecoverySeconds: 0.5f);

            Assert.IsFalse(poise.ApplyPoiseDamage(40f));
            Assert.IsFalse(poise.IsBroken);
            Assert.AreEqual(60f, poise.Current, Tolerance);
        }

        [Test]
        public void AccumulatedPoiseDamage_Breaks()
        {
            var poise = new PoisePool(100f, regenPerSecond: 20f, breakRecoverySeconds: 0.5f);
            int breakCount = 0;
            poise.Broken += () => breakCount++;

            poise.ApplyPoiseDamage(60f);

            Assert.IsTrue(poise.ApplyPoiseDamage(40f));
            Assert.IsTrue(poise.IsBroken);
            Assert.AreEqual(1, breakCount);
        }

        [Test]
        public void PoiseDamageDuringABreak_IsDiscarded()
        {
            var poise = new PoisePool(50f, regenPerSecond: 20f, breakRecoverySeconds: 1f);
            poise.ApplyPoiseDamage(50f);

            // 连击命中硬直目标不得接出第二次破防。
            Assert.IsFalse(poise.ApplyPoiseDamage(500f));
            Assert.IsTrue(poise.IsBroken);
        }

        [Test]
        public void AfterTheBreakRecovers_PoiseReturnsFull()
        {
            var poise = new PoisePool(50f, regenPerSecond: 5f, breakRecoverySeconds: 0.2f);
            poise.ApplyPoiseDamage(50f);

            for (int i = 0; i < 30; i++)
            {
                poise.Tick(Frame);
            }

            // 从零恢复会让角色刚恢复就被一次轻击再次破防，看起来硬直从未结束。
            Assert.IsFalse(poise.IsBroken);
            Assert.AreEqual(1f, poise.Normalized, Tolerance);
        }

        [Test]
        public void PoiseRegenerates_SoChipDamageNeverStaggers()
        {
            var poise = new PoisePool(100f, regenPerSecond: 50f, breakRecoverySeconds: 0.5f);

            // 每半秒一次轻刺，面对每秒五十点恢复，不应累积至破防。
            for (int poke = 0; poke < 10; poke++)
            {
                Assert.IsFalse(poise.ApplyPoiseDamage(10f));

                for (int i = 0; i < 30; i++)
                {
                    poise.Tick(Frame);
                }
            }

            Assert.IsFalse(poise.IsBroken);
            Assert.AreEqual(1f, poise.Normalized, 0.01f);
        }

        [Test]
        public void ResetToFull_ClearsABreak()
        {
            var poise = new PoisePool(50f, regenPerSecond: 5f, breakRecoverySeconds: 10f);
            poise.ApplyPoiseDamage(50f);

            poise.ResetToFull();

            Assert.IsFalse(poise.IsBroken);
            Assert.AreEqual(1f, poise.Normalized, Tolerance);
        }
    }
}
