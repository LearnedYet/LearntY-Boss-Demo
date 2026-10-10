using System;
using AdaptiveBossArena.Combat.Vitals;
using AdaptiveBossArena.Core.Combat;
using NUnit.Framework;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>测试共享生命值池。</summary>
    [TestFixture]
    public sealed class HealthPoolTests
    {
        private const float Tolerance = 0.0001f;

        private static DamageInfo Hit(float amount) =>
            DamageInfo.Create(amount, DamageType.BossMelee, CombatantTeam.Boss, sourceInstanceId: 7);

        [Test]
        public void Constructor_WithNonPositiveMaximum_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new HealthPool(0f, CombatantTeam.Player));
        }

        [Test]
        public void NewPool_StartsFull()
        {
            var health = new HealthPool(100f, CombatantTeam.Player);

            Assert.AreEqual(100f, health.Current, Tolerance);
            Assert.AreEqual(1f, health.Normalized, Tolerance);
            Assert.IsTrue(health.IsAlive);
        }

        [Test]
        public void Reduce_RemovesHealthAndReportsTheAmountTaken()
        {
            var health = new HealthPool(100f, CombatantTeam.Player);

            float applied = health.Reduce(30f, Hit(30f));

            Assert.AreEqual(30f, applied, Tolerance);
            Assert.AreEqual(70f, health.Current, Tolerance);
        }

        [Test]
        public void Reduce_ClampsAtZeroAndReportsOnlyWhatWasTaken()
        {
            var health = new HealthPool(100f, CombatantTeam.Player);

            float applied = health.Reduce(250f, Hit(250f));

            // 过量伤害不能报告为实际造成伤害，否则伤害统计及未来依据它的评分都会被致命一击虚增。
            Assert.AreEqual(100f, applied, Tolerance);
            Assert.AreEqual(0f, health.Current, Tolerance);
        }

        [Test]
        public void Reduce_RaisesChangedBeforeDied()
        {
            var health = new HealthPool(50f, CombatantTeam.Player);
            var order = new System.Collections.Generic.List<string>();

            health.Changed += _ => order.Add("changed");
            health.Died += _ => order.Add("died");

            health.Reduce(50f, Hit(50f));

            // 死亡界面响应 Died 时，生命条必须已经显示为零。
            CollectionAssert.AreEqual(new[] { "changed", "died" }, order);
        }

        [Test]
        public void Died_CarriesTheKillerAndTheFinalBlow()
        {
            var health = new HealthPool(10f, CombatantTeam.Player);
            DeathArgs captured = default;
            health.Died += args => captured = args;

            health.Reduce(10f, Hit(10f));

            Assert.AreEqual(CombatantTeam.Player, captured.Team);
            Assert.AreEqual(7, captured.KillerInstanceId);
            Assert.AreEqual(DamageType.BossMelee, captured.FinalBlow.Type);
        }

        [Test]
        public void Died_IsRaisedOnlyOnce()
        {
            var health = new HealthPool(10f, CombatantTeam.Player);
            int deathCount = 0;
            health.Died += _ => deathCount++;

            health.Reduce(10f, Hit(10f));
            health.Reduce(10f, Hit(10f));

            Assert.AreEqual(1, deathCount);
        }

        [Test]
        public void Heal_ClampsAtMaximum()
        {
            var health = new HealthPool(100f, CombatantTeam.Player);
            health.Reduce(20f, Hit(20f));

            float restored = health.Heal(50f);

            Assert.AreEqual(20f, restored, Tolerance);
            Assert.AreEqual(100f, health.Current, Tolerance);
        }

        [Test]
        public void Heal_OnTheDead_DoesNothing()
        {
            var health = new HealthPool(10f, CombatantTeam.Player);
            health.Reduce(10f, Hit(10f));

            // 复活会使已处理死亡的监听者处于不一致状态。
            Assert.AreEqual(0f, health.Heal(50f), Tolerance);
            Assert.IsFalse(health.IsAlive);
        }

        [Test]
        public void ResetToFull_RestoresTheStartingCondition()
        {
            var health = new HealthPool(100f, CombatantTeam.Player);
            health.Reduce(100f, Hit(100f));

            health.ResetToFull();

            Assert.IsTrue(health.IsAlive);
            Assert.AreEqual(1f, health.Normalized, Tolerance);
        }
    }

    /// <summary>
    /// 测试体力池。
    /// </summary>
    /// <remarks>
    /// 恢复延迟让体力从限速器变为动作投入，因此测试重点是暂停恢复，而非算术本身。
    /// </remarks>
    [TestFixture]
    public sealed class StaminaPoolTests
    {
        private const float Tolerance = 0.001f;
        private const float Frame = 1f / 60f;

        [Test]
        public void TrySpend_WithEnoughStamina_Succeeds()
        {
            var stamina = new StaminaPool(100f, regenPerSecond: 20f, regenDelaySeconds: 0.5f);

            Assert.IsTrue(stamina.TrySpend(25f));
            Assert.AreEqual(75f, stamina.Current, Tolerance);
        }

        [Test]
        public void TrySpend_WithoutEnoughStamina_FailsAndChangesNothing()
        {
            var stamina = new StaminaPool(20f, regenPerSecond: 20f, regenDelaySeconds: 0.5f);

            Assert.IsFalse(stamina.TrySpend(25f));
            Assert.AreEqual(20f, stamina.Current, Tolerance);
        }

        [Test]
        public void AfterSpending_RegenerationIsBlockedForTheDelay()
        {
            var stamina = new StaminaPool(100f, regenPerSecond: 50f, regenDelaySeconds: 0.5f);
            stamina.TrySpend(50f);

            // 明显处于延迟时间内，因此尚不应恢复任何体力。
            for (int i = 0; i < 12; i++)
            {
                stamina.Tick(Frame);
            }

            Assert.IsTrue(stamina.IsRegenerationBlocked);
            Assert.AreEqual(50f, stamina.Current, Tolerance);
        }

        [Test]
        public void AfterTheDelay_RegenerationResumes()
        {
            var stamina = new StaminaPool(100f, regenPerSecond: 50f, regenDelaySeconds: 0.2f);
            stamina.TrySpend(50f);

            for (int i = 0; i < 60; i++)
            {
                stamina.Tick(Frame);
            }

            Assert.IsFalse(stamina.IsRegenerationBlocked);
            Assert.Greater(stamina.Current, 50f);
        }

        [Test]
        public void SpendingAgainMidRegeneration_RestartsTheDelay()
        {
            var stamina = new StaminaPool(100f, regenPerSecond: 50f, regenDelaySeconds: 0.3f);

            stamina.TrySpend(20f);
            for (int i = 0; i < 30; i++)
            {
                stamina.Tick(Frame);
            }

            float beforeSecondSpend = stamina.Current;
            stamina.TrySpend(20f);

            for (int i = 0; i < 6; i++)
            {
                stamina.Tick(Frame);
            }

            // 连续闪避不能逐渐回满；每次消耗都重新开始暂停恢复。
            Assert.IsTrue(stamina.IsRegenerationBlocked);
            Assert.AreEqual(beforeSecondSpend - 20f, stamina.Current, Tolerance);
        }

        [Test]
        public void Regeneration_ClampsAtMaximum()
        {
            var stamina = new StaminaPool(100f, regenPerSecond: 500f, regenDelaySeconds: 0f);
            stamina.TrySpend(10f);

            for (int i = 0; i < 60; i++)
            {
                stamina.Tick(Frame);
            }

            Assert.AreEqual(100f, stamina.Current, Tolerance);
            Assert.AreEqual(1f, stamina.Normalized, Tolerance);
        }

        [Test]
        public void CanSpend_MatchesWhatTrySpendWillDo()
        {
            var stamina = new StaminaPool(30f, regenPerSecond: 10f, regenDelaySeconds: 0.1f);

            Assert.IsTrue(stamina.CanSpend(30f));
            Assert.IsFalse(stamina.CanSpend(31f));
            Assert.IsFalse(stamina.TrySpend(31f));
        }

        [Test]
        public void ResetToFull_ClearsThePendingDelay()
        {
            var stamina = new StaminaPool(100f, regenPerSecond: 20f, regenDelaySeconds: 5f);
            stamina.TrySpend(50f);

            stamina.ResetToFull();

            Assert.IsFalse(stamina.IsRegenerationBlocked);
            Assert.AreEqual(100f, stamina.Current, Tolerance);
        }
    }
}
