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
    /// 测试攻击确实会对真实碰撞体解析命中。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 编辑模式测试已经覆盖时间轴在长帧下的行为，而且结果正确：即使一帧吞下整次攻击，
    /// 它也会发布生效窗口。未覆盖的是执行器在阶段推进完成后才读取阶段，从而丢弃该保障；
    /// 此时攻击已进入后摇，从未执行命中查询。
    /// </para>
    /// <para>
    /// 故障可能恰好位于两个充分测试的模块接缝处，因此此测试驱动真实执行器攻击真实碰撞体，
    /// 而非分别隔离两部分。
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class AttackExecutorHitTests
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

        /// <summary>准确拒绝所有攻击，充当能够招架的防御者。</summary>
        private sealed class ParryingTarget : MonoBehaviour, IDamageable
        {
            public int Offers { get; private set; }

            public CombatantTeam Team => CombatantTeam.Boss;

            public bool IsAlive => true;

            public DamageResult TakeDamage(in DamageInfo damage)
            {
                Offers++;

                return DamageResult.NoDamage(DamageOutcome.Deflected);
            }
        }

        /// <summary>由测试手动驱动的时钟，使帧长成为输入。</summary>
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

        private GameObject Spawn(string name)
        {
            var created = new GameObject(name);
            _spawned.Add(created);

            return created;
        }

        /// <summary>构建宽大的近距离扇形，确保瞄准不是测试对象。</summary>
        private AttackDefinition MakeAttack()
        {
            _attack = ScriptableObject.CreateInstance<AttackDefinition>();

            // 通过反射而非 SerializedObject 设置：此程序集为所有平台构建，因此无法访问 UnityEditor。
            Set("_startupSeconds", 0.09f);
            Set("_activeSeconds", 0.07f);
            Set("_recoverySeconds", 0.2f);
            Set("_range", 3f);
            Set("_arcDegrees", 160f);
            Set("_damage", 10f);
            Set("_showTelegraph", false);

            return _attack;
        }

        [UnityTest]
        public IEnumerator AParriedSwingReportsItselfExactlyOnce()
        {
            var time = new ScriptedTime();
            var events = new CombatEventBus();

            GameObject attacker = Spawn("Attacker");

            GameObject targetObject = Spawn("Parrier");
            targetObject.transform.position = Vector3.forward * 1.5f;
            var target = targetObject.AddComponent<ParryingTarget>();

            GameObject hurtboxObject = Spawn("Hurtbox");
            hurtboxObject.transform.SetParent(targetObject.transform, false);
            hurtboxObject.layer = Layers.BossHurtbox;

            SphereCollider collider = hurtboxObject.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = 0.85f;
            hurtboxObject.AddComponent<Hurtbox>();

            yield return null;

            var executor = new AttackExecutor(
                attacker.transform, CombatantTeam.Player, Layers.PlayerAttackMask, time, events);

            int parried = 0;
            bool runningInsideHandler = false;

            executor.Parried += () =>
            {
                parried++;

                // 事件文档约定：它在时间轴自身的阶段推进循环内触发。若处理器在此取消，
                // 就会重入尚未结束的循环，因此后仰以硬直请求延后。断言时间轴仍运行证明此危险真实存在，
                // 而非理论可能，也确保没有悄然改成同步取消。
                runningInsideHandler = executor.IsRunning;
            };

            executor.Begin(MakeAttack(), 1.5f);

            for (int i = 0; i < 40; i++)
            {
                time.DeltaTime = 0.01f;
                time.CombatTime += 0.01f;
                executor.Tick(0.01f);
            }

            Assert.AreEqual(1, target.Offers, "One swing was offered to the defender more than once.");
            Assert.AreEqual(1, parried, "A parried swing did not report itself exactly once.");
            Assert.IsTrue(runningInsideHandler, "The swing was already cancelled when the handler ran.");
        }

        [UnityTest]
        public IEnumerator AParriedSwingPublishesTheDefenderAsTheActor()
        {
            // CombatMemory 路由所依据的约定。若以攻击者为主体发布，被招架的玩家会被记为成功弹反，
            // 首领便会适应从未出现的统计量。
            var time = new ScriptedTime();
            var events = new CombatEventBus();

            GameObject attacker = Spawn("Attacker");

            GameObject targetObject = Spawn("Parrier");
            targetObject.transform.position = Vector3.forward * 1.5f;
            targetObject.AddComponent<ParryingTarget>();

            GameObject hurtboxObject = Spawn("Hurtbox");
            hurtboxObject.transform.SetParent(targetObject.transform, false);
            hurtboxObject.layer = Layers.BossHurtbox;

            SphereCollider collider = hurtboxObject.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = 0.85f;
            hurtboxObject.AddComponent<Hurtbox>();

            yield return null;

            CombatantTeam? actor = null;
            events.EventRecorded += recorded =>
            {
                if (recorded.Kind == CombatEventKind.Parried)
                {
                    actor = recorded.Actor;
                }
            };

            var executor = new AttackExecutor(
                attacker.transform, CombatantTeam.Player, Layers.PlayerAttackMask, time, events);

            executor.Begin(MakeAttack(), 1.5f);

            for (int i = 0; i < 40; i++)
            {
                time.DeltaTime = 0.01f;
                time.CombatTime += 0.01f;
                executor.Tick(0.01f);
            }

            Assert.AreEqual(
                CombatantTeam.Boss, actor,
                "A player swing parried by the boss must name the boss as the actor.");
        }

        /// <summary>为同一战斗者叠加第二个伤害倍率更高的受击框。</summary>
        private void AddWeakPoint(RecordingTarget owner, float multiplier)
        {
            GameObject weakPointObject = Spawn("WeakPoint");
            weakPointObject.transform.SetParent(owner.transform, false);
            weakPointObject.layer = Layers.BossHurtbox;

            SphereCollider collider = weakPointObject.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = 0.45f;

            var hurtbox = weakPointObject.AddComponent<Hurtbox>();

            FieldInfo field = typeof(Hurtbox).GetField(
                "_damageMultiplier", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, "Hurtbox has no field '_damageMultiplier'.");
            field.SetValue(hurtbox, multiplier);
        }

        [UnityTest]
        public IEnumerator AWeakPointWinsOverTheBodyItSitsInside()
        {
            // 首领弱点是完全位于身体胶囊体内的小球，因此正面挥击同时覆盖两者。
            // 执行器已命中集合以所有者为键，只能应用其中一个，而 Physics 返回重叠结果的顺序未定义。
            // 若检测器不处理冲突，瞄准核心就成了抛硬币，比没有弱点还糟。
            GameObject attacker = Spawn("Attacker");
            RecordingTarget target = SpawnTarget(Vector3.forward * 1.5f);
            AddWeakPoint(target, 1.6f);

            yield return null;

            SwingAt(MakeAttack(), attacker.transform);

            Assert.AreEqual(1, target.Hits.Count, "Two hurtboxes on one body dealt damage twice.");
            Assert.AreEqual(
                16f, target.Hits[0].Amount, 0.001f,
                "The hit was not scaled by the weak point's multiplier.");
        }

        /// <summary>构建突进刺击的形状：直达前方的狭窄盒体。</summary>
        /// <remarks>
        /// Box 已在检测器、辅助图形和预警网格中实现，却未被任何攻击资源引用，因此从未进行完整链路验证。
        /// 现在刺击使用它，这些测试证明路径可用。数值与资源吻合：半深度 1.4米，中心位于前方 2 米，
        /// 宽半米。
        /// </remarks>
        private AttackDefinition MakeBoxAttack()
        {
            MakeAttack();

            Set("_shape", AttackShape.Box);
            Set("_offset", new Vector3(0f, 1.1f, 2f));
            Set("_boxHalfExtents", new Vector3(0.5f, 0.6f, 1.4f));

            return _attack;
        }

        /// <summary>向已生成的目标执行一次完整攻击。</summary>
        private void SwingAt(AttackDefinition attack, Transform attacker)
        {
            var time = new ScriptedTime();
            var events = new CombatEventBus();

            var executor = new AttackExecutor(
                attacker, CombatantTeam.Player, Layers.PlayerAttackMask, time, events);

            executor.Begin(attack, 1.5f);

            for (int i = 0; i < 40; i++)
            {
                time.DeltaTime = 0.01f;
                time.CombatTime += 0.01f;
                executor.Tick(0.01f);
            }
        }

        [UnityTest]
        public IEnumerator ABoxAttackLandsOnATargetInItsPath()
        {
            GameObject attacker = Spawn("Attacker");
            RecordingTarget target = SpawnTarget(Vector3.forward * 2f);

            // Awake 必须先运行，受击框才能确定所有者。
            yield return null;

            SwingAt(MakeBoxAttack(), attacker.transform);

            Assert.AreEqual(
                1, target.Hits.Count, "A thrust did not hit a target standing in front of it.");
        }

        [UnityTest]
        public IEnumerator ABoxAttackMissesATargetStandingAside()
        {
            GameObject attacker = Spawn("Attacker");
            RecordingTarget target = SpawnTarget(new Vector3(3f, 0f, 2f));

            yield return null;

            SwingAt(MakeBoxAttack(), attacker.transform);

            // 将刺击改为盒体的意义：此角度下，经目标自身宽度扩张的旧 40 度锥形仍能命中，
            // 因此侧移躲突进几乎无效。
            Assert.AreEqual(
                0, target.Hits.Count, "A thrust hit a target standing well off to the side.");
        }

        /// <summary>写入攻击的一个序列化字段。</summary>
        private void Set(string fieldName, object value)
        {
            FieldInfo field = typeof(AttackDefinition).GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, $"AttackDefinition has no field '{fieldName}'.");
            field.SetValue(_attack, value);
        }

        /// <summary>放置一个带受击框的目标，使攻击者层掩码可以发现它。</summary>
        private RecordingTarget SpawnTarget(Vector3 position)
        {
            GameObject target = Spawn("Target");
            target.transform.position = position;

            var owner = target.AddComponent<RecordingTarget>();

            GameObject hurtboxObject = Spawn("Hurtbox");
            hurtboxObject.transform.SetParent(target.transform, false);
            hurtboxObject.layer = Layers.BossHurtbox;

            SphereCollider collider = hurtboxObject.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = 0.85f;

            hurtboxObject.AddComponent<Hurtbox>();

            return owner;
        }

        [UnityTest]
        public IEnumerator AnAttackLandsOnATargetInFront()
        {
            // 基线。若此处失败，错误在测试装置，而非执行器。
            var time = new ScriptedTime();
            var events = new CombatEventBus();

            GameObject attacker = Spawn("Attacker");
            RecordingTarget target = SpawnTarget(Vector3.forward * 1.5f);

            // Awake 必须先运行，受击框才能确定所有者。
            yield return null;

            var executor = new AttackExecutor(
                attacker.transform, CombatantTeam.Player, Layers.PlayerAttackMask, time, events);

            executor.Begin(MakeAttack(), 1.5f);

            for (int i = 0; i < 40; i++)
            {
                time.DeltaTime = 0.01f;
                time.CombatTime += 0.01f;
                executor.Tick(0.01f);
            }

            Assert.AreEqual(1, target.Hits.Count, "A swing at a target directly in front did not land.");
        }

        [UnityTest]
        public IEnumerator AnAttackStillLandsWhenOneFrameSwallowsTheWholeSwing()
        {
            // 回归案例。轻击生效窗口在每秒六十帧下约为四帧，因此一次卡顿可同时覆盖前摇、生效和后摇。
            // 修复前，执行器仅在时间轴推进结束后读取阶段，发现已是后摇，便完全不查询；
            // 挥击穿过目标却毫无结果，让玩家感觉游戏无视输入。
            var time = new ScriptedTime();
            var events = new CombatEventBus();

            GameObject attacker = Spawn("Attacker");
            RecordingTarget target = SpawnTarget(Vector3.forward * 1.5f);

            yield return null;

            var executor = new AttackExecutor(
                attacker.transform, CombatantTeam.Player, Layers.PlayerAttackMask, time, events);

            executor.Begin(MakeAttack(), 1.5f);

            // 单帧时长超过整次攻击。
            time.DeltaTime = 1f;
            time.CombatTime += 1f;
            executor.Tick(1f);

            Assert.AreEqual(
                1, target.Hits.Count,
                "A frame that spanned the whole attack registered no hit at all.");
        }

        [UnityTest]
        public IEnumerator ATargetIsStruckOnlyOncePerSwing()
        {
            // 生效窗口跨越多帧，每帧都进行查询。若无已命中集合，一次挥击会每帧重复造成伤害。
            var time = new ScriptedTime();
            var events = new CombatEventBus();

            GameObject attacker = Spawn("Attacker");
            RecordingTarget target = SpawnTarget(Vector3.forward * 1.5f);

            yield return null;

            var executor = new AttackExecutor(
                attacker.transform, CombatantTeam.Player, Layers.PlayerAttackMask, time, events);

            executor.Begin(MakeAttack(), 1.5f);

            for (int i = 0; i < 40; i++)
            {
                time.DeltaTime = 0.01f;
                time.CombatTime += 0.01f;
                executor.Tick(0.01f);
            }

            Assert.AreEqual(1, target.Hits.Count, "One swing dealt damage more than once.");
        }
    }
}
