using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Constants;
using AdaptiveBossArena.Core.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AdaptiveBossArena.Tests.PlayMode
{
    /// <summary>
    /// 断言攻击只命中打击部位实际经过的位置。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 玩家报告明显从身旁掠过的挥击仍造成伤害。伤害来自攻击者前方最大四米、120 度的扇形，
    /// 无论身体实际在其中做什么。测试手动挥动刀刃，模拟动画带动它的方式，
    /// 通过真实执行器攻击真实触发碰撞体，使可见表现与伤害之间的连接成为测试对象。
    /// </para>
    /// <para>
    /// 刻意保留一个旧扇形案例，证明场景正是玩家报告的问题：如今挥空的同次挥击原先会造成伤害。
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class StrikeHonestyTests
    {
        /// <summary>记录受到的攻击，充当战斗者。</summary>
        private sealed class RecordingTarget : MonoBehaviour, IDamageable
        {
            public readonly List<DamageInfo> Hits = new List<DamageInfo>();

            public CombatantTeam Team => CombatantTeam.Boss;

            public bool IsAlive => true;

            public DamageResult TakeDamage(in DamageInfo damage)
            {
                Hits.Add(damage);
                return DamageResult.Applied(damage.Amount, false);
            }
        }

        /// <summary>由测试手动驱动的时钟。</summary>
        private sealed class ScriptedTime : ITimeService
        {
            public float DeltaTime { get; set; }

            public float UnscaledDeltaTime => DeltaTime;

            public float FixedDeltaTime => 0.02f;

            public float CombatTime { get; set; }

            public float TimeScale => 1f;

            public bool IsPaused => false;

            public void RequestHitStop(float seconds) { }

            public void RequestSlowMotion(float scale, float seconds) { }

            public void SetPaused(bool paused) { }

            public void ClearTimeEffects() { }

            public void ResetCombatClock() => CombatTime = 0f;

            public void Tick() { }
        }

        private const float Step = 0.01f;

        /// <summary>刀刃的旋转支点：攻击者肩部。</summary>
        private static readonly Vector3 Shoulder = Vector3.up;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private AttackDefinition _attack;

        [TearDown]
        public void CleanUp()
        {
            foreach (GameObject spawned in _spawned)
            {
                Object.DestroyImmediate(spawned);
            }

            _spawned.Clear();

            if (_attack != null)
            {
                Object.DestroyImmediate(_attack);
                _attack = null;
            }
        }

        [UnityTest]
        public IEnumerator ASwingThatPassesBesideTheTargetDealsNothing()
        {
            // 距离 3.2 米，明显在旧四米扇形内，且远超两米刀刃范围。
            RecordingTarget target = SpawnTarget(new Vector3(0f, 1f, 3.2f));
            StrikeVolume blade = SpawnBlade();
            yield return null;

            Swing(StrikerParts.Weapon, blade, degreesPerTick: 10f);

            Assert.IsEmpty(target.Hits, "A blade that never reached the target still hurt it.");
        }

        [UnityTest]
        public IEnumerator TheOldWedgeWouldHaveHurtFromThere()
        {
            RecordingTarget target = SpawnTarget(new Vector3(0f, 1f, 3.2f));
            StrikeVolume blade = SpawnBlade();
            yield return null;

            Swing(StrikerParts.None, blade, degreesPerTick: 10f);

            Assert.AreEqual(1, target.Hits.Count,
                "The old wedge did not reach the target, so the scenario above proves nothing.");
        }

        [UnityTest]
        public IEnumerator ASwingThroughTheTargetLandsExactlyOnce()
        {
            RecordingTarget target = SpawnTarget(new Vector3(0f, 1f, 1.6f));
            StrikeVolume blade = SpawnBlade();
            yield return null;

            Swing(StrikerParts.Weapon, blade, degreesPerTick: 10f);

            Assert.AreEqual(1, target.Hits.Count, "A blade through the target did not land exactly once.");
        }

        [UnityTest]
        public IEnumerator AFastSwingCannotPassThroughUnseen()
        {
            // 每帧 60 度：只在 -30 与 +30 度采样刀刃，两处均避开目标。只有两帧间的扫掠能发现目标。
            RecordingTarget target = SpawnTarget(new Vector3(0f, 1f, 1.6f));
            StrikeVolume blade = SpawnBlade();
            yield return null;

            Swing(StrikerParts.Weapon, blade, degreesPerTick: 60f);

            Assert.AreEqual(1, target.Hits.Count, "A fast blade passed through the target between two frames.");
        }

        /// <summary>
        /// 执行一次攻击，同时将刀刃从攻击者左侧移至右侧。
        /// </summary>
        private void Swing(StrikerParts strikers, StrikeVolume blade, float degreesPerTick)
        {
            var time = new ScriptedTime { DeltaTime = Step };
            GameObject attacker = Spawn("Attacker");

            var executor = new AttackExecutor(
                attacker.transform, CombatantTeam.Player, Layers.PlayerAttackMask, time, new CombatEventBus());
            executor.SetStrikers(new[] { blade });

            executor.Begin(MakeAttack(strikers));

            float yaw = -90f;

            for (int i = 0; i < 60 && executor.IsRunning; i++)
            {
                blade.transform.SetPositionAndRotation(Shoulder, Quaternion.Euler(0f, yaw, 0f));

                time.CombatTime += Step;
                executor.Tick(Step);

                yaw = Mathf.Min(90f, yaw + degreesPerTick);
            }
        }

        /// <summary>从肩部外半米延伸至两米的刀刃。</summary>
        private StrikeVolume SpawnBlade()
        {
            GameObject bladeObject = Spawn("Blade");
            bladeObject.transform.SetPositionAndRotation(Shoulder, Quaternion.Euler(0f, -90f, 0f));

            var blade = bladeObject.AddComponent<StrikeVolume>();
            blade.Configure(StrikerParts.Weapon, 0.5f, 2f, 0.1f);

            return blade;
        }

        private RecordingTarget SpawnTarget(Vector3 position)
        {
            GameObject targetObject = Spawn("Target");
            targetObject.transform.position = position;
            var target = targetObject.AddComponent<RecordingTarget>();

            GameObject hurtboxObject = Spawn("Hurtbox");
            hurtboxObject.transform.SetParent(targetObject.transform, false);
            hurtboxObject.layer = Layers.BossHurtbox;

            SphereCollider collider = hurtboxObject.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = 0.4f;
            hurtboxObject.AddComponent<Hurtbox>();

            return target;
        }

        /// <summary>宽且长的扇形，使旧体积检测能触及刀刃够不到的地方。</summary>
        private AttackDefinition MakeAttack(StrikerParts strikers)
        {
            _attack = ScriptableObject.CreateInstance<AttackDefinition>();

            // 通过反射：此程序集为所有平台构建，因此无法访问 UnityEditor。
            Set("_startupSeconds", Step);
            Set("_activeSeconds", 0.3f);
            Set("_recoverySeconds", 0.05f);
            Set("_range", 4f);
            Set("_arcDegrees", 160f);
            Set("_damage", 10f);
            Set("_showTelegraph", false);
            Set("_strikers", strikers);

            return _attack;
        }

        private void Set(string field, object value) =>
            typeof(AttackDefinition)
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_attack, value);

        private GameObject Spawn(string name)
        {
            var created = new GameObject(name);
            _spawned.Add(created);

            return created;
        }
    }
}
