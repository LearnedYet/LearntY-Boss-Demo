using System.Collections;
using System.Reflection;
using AdaptiveBossArena.AI;
using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Combat.Movement;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Constants;
using AdaptiveBossArena.Core.Events;
using AdaptiveBossArena.Core.Services;
using AdaptiveBossArena.Player;
using AdaptiveBossArena.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AdaptiveBossArena.Tests.PlayMode
{
    /// <summary>
    /// 断言组合完成的战斗在真实场景中确实可运行。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这些测试源于整个编辑模式套件都看不见的故障：试玩者报告整场战斗没有任何资源条变化，无人死亡，
    /// 结算界面也从未出现；控制台无报错，237 个测试全绿。每个模块隔离时都正确。
    /// </para>
    /// <para>
    /// 缺口在于编辑模式测试从不加载场景、实例化预制体、运行物理或接触界面。
    /// 从“资源池算术正确”到“玩家能看到生命下降”之间完全未经测试。故障恰在这里，
    /// 因此此处在发布场景中遍历完整路径。
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class FightIntegrityTests
    {
        /// <summary>等待若干帧，使 <c>Awake</c>/<c>Start</c> 及首次物理步进稳定。</summary>
        private const int WarmUpFrames = 10;

        /// <summary>足够明显但不致命的攻击。</summary>
        private const float TestDamage = 25f;

        private PlayerController _player;
        private BossController _boss;

        [UnitySetUp]
        public IEnumerator LoadTheArena()
        {
            SceneManager.LoadScene("Arena", LoadSceneMode.Single);

            for (int i = 0; i < WarmUpFrames; i++)
            {
                yield return null;
            }

            _player = Object.FindAnyObjectByType<PlayerController>();
            _boss = Object.FindAnyObjectByType<BossController>();
        }

        /// <summary>等待开场动画放行，且战斗时钟确实运行。</summary>
        private static IEnumerator WaitForTheFightToStart()
        {
            const float GiveUpAfterSeconds = 15f;

            float waited = 0f;

            while (Time.timeScale <= 0f && waited < GiveUpAfterSeconds)
            {
                waited += Time.unscaledDeltaTime;

                yield return null;
            }

            Assert.Less(waited, GiveUpAfterSeconds, "The fight never started.");
        }

        [UnityTest]
        public IEnumerator TheArenaBringsUpBothCombatants()
        {
            Assert.IsNotNull(_player, "No player in the arena scene.");
            Assert.IsNotNull(_boss, "No boss in the arena scene.");

            // 设置时提前退出的控制器仍会响应 TakeDamage，只是忽略全部攻击，
            // 因此未初始化的战斗者与不可伤害的战斗者无法区分。
            Assert.IsTrue(_player.IsInitialised, "The player failed to initialise, silently.");
            Assert.IsTrue(_boss.IsInitialised, "The boss failed to initialise, silently.");

            Assert.Greater(_player.Health.Maximum, 0f);
            Assert.Greater(_boss.Health.Maximum, 0f);

            yield return null;
        }

        [UnityTest]
        public IEnumerator TheDrawnWeaponDecidesWhatADeflectIsWorth()
        {
            Assert.IsNotNull(_player, "No player in the arena scene.");

            // 通过查找而非注入获得：武器确实因玩家配置引用而加载，因此武器列表为空时此测试也会失败。
            WeaponDefinition[] weapons = Resources.FindObjectsOfTypeAll<WeaponDefinition>();
            Assert.AreEqual(3, weapons.Length, "The three generated weapons are not all loaded.");

            // 装备方法刻意保持私有，控制器外部不应选择玩家武器。测试通过反射驱动才符合设计，
            // 而非为一个断言扩大 API。
            MethodInfo equip = typeof(PlayerController).GetMethod(
                "EquipWeapon", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(equip, "PlayerController has no EquipWeapon method.");

            foreach (WeaponDefinition weapon in weapons)
            {
                equip.Invoke(_player, new object[] { weapon });

                // 回归案例：该属性曾读取角色配置，因此三把武器弹反都造成相同架势伤害，
                // 编写的数值从未应用。
                Assert.AreEqual(
                    weapon.DeflectPostureDamage, _player.DeflectPostureDamage, 0.001f,
                    $"With '{weapon.DisplayName}' drawn, a deflect pays out the wrong posture.");
            }

            yield return null;
        }

        /// <summary>读取战斗者私有上下文，外部无需公开访问。</summary>
        private static T ContextOf<T>(object controller)
        {
            FieldInfo field = controller.GetType().GetField(
                "_context", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, $"{controller.GetType().Name} has no _context field.");

            return (T)field.GetValue(controller);
        }

        /// <summary>具有固定击退且无其它值得测量副作用的攻击。</summary>
        private static DamageInfo Shove(CombatantTeam source, float knockback) => new DamageInfo
        {
            Amount = 1f,
            Type = DamageType.Heavy,
            SourceTeam = source,
            SourceInstanceId = 7,
            HitDirection = Vector3.forward,
            KnockbackSpeed = knockback,
            Stagger = StaggerStrength.None
        };

        [UnityTest]
        public IEnumerator KnockbackOnThePlayerIsAShoveTheirOwnMovementCannotErase()
        {
            // 在冻结的开场期间读取，此时移动器不更新，因此数值正是攻击施加的结果。
            // 击退原先覆盖玩家速度，下一帧自身移动输入又将其覆盖回去。
            Assert.IsNotNull(_player);
            var motor = ContextOf<PlayerContext>(_player).Motor;

            _player.TakeDamage(Shove(CombatantTeam.Boss, 9f));

            Assert.AreEqual(9f, motor.ImpulseVelocity.magnitude, 0.01f,
                "A knockback did not become an imposed shove on the player.");

            yield return null;
        }

        [UnityTest]
        public IEnumerator ABrokenBossIsThrownFurtherThanAFreshOne()
        {
            Assert.IsNotNull(_boss);
            var motor = ContextOf<AI.BossContext>(_boss).Motor;

            _boss.TakeDamage(Shove(CombatantTeam.Player, 10f));
            float freshShove = motor.ImpulseVelocity.magnitude;

            Assert.Greater(freshShove, 0f, "The boss ignored knockback entirely, as it used to.");

            _boss.ApplyDeflectPosture(_boss.Poise.Maximum * 2f);
            Assert.IsTrue(_boss.Poise.IsBroken, "Setup failed: the boss's stance did not break.");

            _boss.TakeDamage(Shove(CombatantTeam.Player, 10f));
            float brokenShove = motor.ImpulseVelocity.magnitude - freshShove;

            Assert.Greater(
                brokenShove, freshShove * 3f,
                "Breaking the boss's stance did not make it markedly easier to push.");

            yield return null;
        }

        [UnityTest]
        public IEnumerator AKnightPlacedInsideTheBossIsPushedOutOfIt()
        {
            // 控制器已不再互相阻挡，因此唯一维持身体分离的是导演的分离处理。若此连接损坏，
            // 双方就会互相穿过，其它测试完全无法指出问题。
            Assert.IsNotNull(_player);
            Assert.IsNotNull(_boss);

            yield return WaitForTheFightToStart();

            IPushBody knight = _player;
            IPushBody brute = _boss;

            var motor = ContextOf<PlayerContext>(_player).Motor;
            motor.Teleport(brute.BodyPosition + new Vector3(0.3f, 0f, 0f));

            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            Vector3 between = brute.BodyPosition - knight.BodyPosition;
            between.y = 0f;

            float contact = knight.BodyRadius + brute.BodyRadius;

            Assert.GreaterOrEqual(
                between.magnitude, contact * 0.9f,
                "The knight is still standing inside the boss.");
        }

        [UnityTest]
        public IEnumerator SparksAndHazardsDrawWithMaterialsTheBuildCannotStrip()
        {
            // 两者原先运行时按名称查找着色器并创建材质。构建只包含被资源引用的着色器，
            // 因此无法保证它们随包发布：WebGL构建可能将其剔除，所有火花和裂痕便无声消失。
            // 材质现在是场景引用的生成资源。
            var effects = Object.FindAnyObjectByType<Game.CombatEffectsDirector>();
            var hazards = Object.FindAnyObjectByType<HazardField>();

            Assert.IsNotNull(effects, "No combat effects director in the arena.");
            Assert.IsNotNull(hazards, "No hazard field in the arena.");

            Assert.IsNotNull(effects.ImpactMaterial, "Impact sparks have no material asset assigned.");
            Assert.IsNotNull(hazards.DiscMaterial, "Hazard discs have no material asset assigned.");

            Assert.IsTrue(effects.ImpactMaterial.shader.isSupported, "The spark shader is not supported.");
            Assert.IsNotNull(effects.MatterMaterial, "Blood and dust have no material asset assigned.");
            Assert.IsTrue(effects.MatterMaterial.shader.isSupported, "The blood and dust shader is not supported.");
            Assert.IsTrue(hazards.DiscMaterial.shader.isSupported, "The hazard shader is not supported.");

            yield return null;
        }

        [UnityTest]
        public IEnumerator TheBossInTheSceneCarriesItsWeakPoint()
        {
            Assert.IsNotNull(_boss, "No boss in the arena scene.");

            Hurtbox[] hurtboxes = _boss.GetComponentsInChildren<Hurtbox>();

            // 弱点位于预制体，而场景由预制体构建。这条链路的每个部分在本项目中都至少无声失效过一次；
            // 不存在的弱点，看起来与玩家总打不中的弱点完全一样。
            Assert.AreEqual(
                1,
                System.Array.FindAll(hurtboxes, h => h.DamageMultiplier > 1f).Length,
                "The boss in the arena has no weak point, or more than one.");

            Assert.AreEqual(
                1,
                System.Array.FindAll(hurtboxes, h => Mathf.Approximately(h.DamageMultiplier, 1f)).Length,
                "The boss lost the ordinary body hurtbox its weak point is meant to sit inside.");

            yield return null;
        }

        [UnityTest]
        public IEnumerator AHitOnThePlayerReducesTheirHealth()
        {
            Assert.IsNotNull(_player);

            Hurtbox hurtbox = _player.GetComponentInChildren<Hurtbox>();
            Assert.IsNotNull(hurtbox, "The player has no hurtbox, so nothing can ever hit them.");
            Assert.IsTrue(hurtbox.IsValid, "The player's hurtbox found no owner.");

            float before = _player.Health.Current;
            hurtbox.Receive(DamageFrom(CombatantTeam.Boss));

            yield return null;

            Assert.Less(
                _player.Health.Current, before,
                "A clean hit on the player's hurtbox did not reduce their health.");
        }

        [UnityTest]
        public IEnumerator AHitOnTheBossReducesItsHealth()
        {
            Assert.IsNotNull(_boss);

            Hurtbox hurtbox = _boss.GetComponentInChildren<Hurtbox>();
            Assert.IsNotNull(hurtbox, "The boss has no hurtbox, so nothing can ever hit it.");
            Assert.IsTrue(hurtbox.IsValid, "The boss's hurtbox found no owner.");

            float before = _boss.Health.Current;
            hurtbox.Receive(DamageFrom(CombatantTeam.Player));

            yield return null;

            Assert.Less(
                _boss.Health.Current, before,
                "A clean hit on the boss's hurtbox did not reduce its health.");
        }

        [UnityTest]
        public IEnumerator ThePlayerHealthBarFollowsTheirHealth()
        {
            // 报告症状的完整链路：资源池 → 事件通道 → 资源条。编辑模式套件从不构建界面，
            // 因此永远无法进行此断言。
            ResourceBar bar = FindBar("PlayerVitals", "Health");
            Assert.IsNotNull(bar, "No player health bar in the heads-up display.");

            float shownBefore = bar.DisplayedFraction;

            _player.GetComponentInChildren<Hurtbox>().Receive(DamageFrom(CombatantTeam.Boss));

            yield return null;

            Assert.Less(
                bar.DisplayedFraction, shownBefore,
                "The player's health fell but the bar did not follow it.");
        }

        [UnityTest]
        public IEnumerator TheBossHealthBarFollowsItsHealth()
        {
            ResourceBar bar = FindBar("BossVitals", "Health");
            Assert.IsNotNull(bar, "No boss health bar in the heads-up display.");

            float shownBefore = bar.DisplayedFraction;

            _boss.GetComponentInChildren<Hurtbox>().Receive(DamageFrom(CombatantTeam.Player));

            yield return null;

            Assert.Less(
                bar.DisplayedFraction, shownBefore,
                "The boss's health fell but the bar did not follow it.");
        }

        [UnityTest]
        public IEnumerator TheBossAttacksAPlayerWhoJustStandsThere()
        {
            // 完全没有输入。若首领在这么长时间内都无法向静止目标发起一次攻击，就称不上战斗；
            // 无需任何伤害故障，也能解释试玩者看到资源条静止。
            Assert.IsTrue(
                ServiceRegistry.Current.TryGet(out ICombatEventBus events),
                "No combat event bus, so nothing can be learned from or reacted to.");

            int attacksStarted = 0;
            void Count(CombatEvent recorded)
            {
                if (recorded.Kind == CombatEventKind.AttackStarted &&
                    recorded.Actor == CombatantTeam.Boss)
                {
                    attacksStarted++;
                }
            }

            events.EventRecorded += Count;

            try
            {
                yield return new WaitForSeconds(25f);
            }
            finally
            {
                events.EventRecorded -= Count;
            }

            Assert.Greater(
                attacksStarted, 0,
                "The boss threw no attacks in 25 seconds against a stationary player.");
        }

        [UnityTest]
        public IEnumerator ABrokenGuardStillAnnouncesItself()
        {
            // 一次险些随版本发布的回归。首领硬直状态原先每次进入都发布 PoiseBroken；原因随请求传递后，
            // 发布只允许原因是破防的情况，而所有架势崩溃调用点却仍默认普通受击。结果可编译、测试通过，
            // 却无声移除了战斗最响亮的节拍。此前从未断言该事件确实发布。
            Assert.IsNotNull(_boss, "No boss in the arena scene.");
            Assert.IsTrue(ServiceRegistry.Current.TryGet(out ICombatEventBus events), "No event bus.");

            bool announced = false;
            void Listen(CombatEvent recorded)
            {
                if (recorded.Kind == CombatEventKind.PoiseBroken)
                {
                    announced = true;
                }
            }

            // 遭遇以冻结时间的准备战斗开场开始，因此首领状态机尚未更新，
            // 此时请求硬直只会留在未消费状态。等待时钟而非固定帧数，使测试不依赖开场动画调节时长。
            yield return WaitForTheFightToStart();

            events.EventRecorded += Listen;

            try
            {
                // 远超资源池容量的架势伤害，使破防确定发生，而非依赖调节参数。
                _boss.ApplyDeflectPosture(_boss.Poise.Maximum * 2f);

                for (int i = 0; i < WarmUpFrames; i++)
                {
                    yield return null;
                }
            }
            finally
            {
                events.EventRecorded -= Listen;
            }

            Assert.IsTrue(announced, "The boss's guard broke and nothing announced it.");
        }

        [UnityTest]
        public IEnumerator ABossParryCostsThePlayerPosture()
        {
            // 用一个断言覆盖完整链路：通道资源存在，首领预制体与导演指向同一通道，导演已订阅，
            // 玩家架势池作出响应。每个引用均使用空条件访问，因此任何环节断开都会无声失败；
            // 本项目曾正因如此，在资源条从不变化的游戏上得到全绿测试套件。
            Assert.IsNotNull(_player, "No player in the arena scene.");

            VoidEventChannel parried = null;

            foreach (VoidEventChannel channel in Resources.FindObjectsOfTypeAll<VoidEventChannel>())
            {
                if (channel.name == "OnBossParried")
                {
                    parried = channel;
                }
            }

            Assert.IsNotNull(parried, "The OnBossParried channel asset is missing.");

            yield return WaitForTheFightToStart();

            float postureBefore = _player.Posture.Current;
            Assert.Greater(postureBefore, 0f, "The player started with no posture to lose.");

            parried.Raise();

            yield return null;

            Assert.Less(
                _player.Posture.Current, postureBefore,
                "The boss parried and the player's posture did not move.");
        }

        [UnityTest]
        public IEnumerator TheHealthChannelStillHasItsListener()
        {
            // 进一步区分前两个故障。资源条立即应用事件，因此不变化的资源条要么没有监听者，
            // 要么从未收到事件。此测试区分两者，而通道类型正为此保留监听者数量。
            FloatEventChannel[] loaded = Resources.FindObjectsOfTypeAll<FloatEventChannel>();
            var names = new System.Text.StringBuilder();

            foreach (FloatEventChannel c in loaded)
            {
                names.Append(c.name).Append('(').Append(c.ListenerCount).Append(") ");
            }

            Debug.Log($"[DIAG] float channels loaded={loaded.Length}: {names}");

            ResourceBar bar = FindBar("PlayerVitals", "Health");
            Assert.IsNotNull(bar);
            Debug.Log($"[DIAG] player health bar hasChannel={bar.HasChannel} " +
                      $"shows={bar.DisplayedFraction:0.000}");

            FloatEventChannel channel = FindChannel("OnPlayerHealthChanged");
            Assert.IsNotNull(channel, "The player health channel asset is not loaded.");

            Debug.Log($"[DIAG] {channel.name} listeners={channel.ListenerCount} " +
                      $"barShows={bar.DisplayedFraction:0.000}");

            Assert.Greater(
                channel.ListenerCount, 0,
                "Nothing is listening to the player health channel, so no bar can ever move.");

            // 再手动触发通道，证明监听者确实是资源条。
            channel.Raise(0.5f);
            yield return null;

            Assert.AreEqual(
                0.5f, bar.DisplayedFraction, 0.001f,
                "The channel has a listener, but raising it did not move the bar.");
        }

        [UnityTest]
        public IEnumerator TheEndScreenTextDoesNotOverlapItself()
        {
            // 试玩截图显示摘要直接穿过标题。每个 UiBuilder 文本均使用中心轴心，
            // 因此位于 y 的矩形覆盖 y +/- height/2，顶部锚定的文本块从矩形上缘开始；凭肉眼容易弄错，
            // 其它测试也无法看见。
            RectTransform headline = FindText("Headline");
            RectTransform summary = FindText("Summary");
            RectTransform dossier = FindText("Dossier");

            Assert.IsNotNull(headline, "No end-screen headline.");
            Assert.IsNotNull(summary, "No end-screen summary.");
            Assert.IsNotNull(dossier, "No end-screen dossier.");

            Assert.GreaterOrEqual(
                BottomOf(headline), TopOf(summary),
                "The end-screen summary overlaps the headline.");

            Assert.GreaterOrEqual(
                BottomOf(summary), TopOf(dossier),
                "The end-screen dossier overlaps the summary.");

            yield return null;
        }

        [UnityTest]
        public IEnumerator ThePlayerHoldsAVisibleWeapon()
        {
            // 挂点、装备调用和模型字段从一开始就存在，却没有内容可放入，
            // 因此换武器只改变标签和挥击颜色，玩家看不到实际变化。此断言确保手中不再空无一物。
            var socket = Object.FindAnyObjectByType<Combat.Feel.WeaponSocket>();
            Assert.IsNotNull(socket, "The player has no weapon socket.");

            MeshRenderer[] held = socket.GetComponentsInChildren<MeshRenderer>(includeInactive: true);

            Assert.Greater(held.Length, 0, "The weapon socket is empty; no model was equipped.");

            yield return null;
        }

        [UnityTest]
        public IEnumerator NothingDecorativeCanBeCollidedWith()
        {
            // 身体和竞技场装饰由基本图形构成，而 Unity基本图形自带碰撞体。
            // 任何残留碰撞体都会位于角色控制器内部或地面上改变战斗，因此必须全部移除。
            foreach (string rootName in new[] { "Dressing", "Cathedral" })
            {
                GameObject root = GameObject.Find(rootName);

                if (root == null)
                {
                    continue;
                }

                Assert.IsEmpty(
                    root.GetComponentsInChildren<Collider>(true),
                    $"{rootName} carries colliders and can interfere with the fight.");
            }

            var player = Object.FindAnyObjectByType<PlayerController>();
            Transform visual = player.transform.Find("Visual");

            Assert.IsNotNull(visual, "The player has no visual root.");
            // 布娃娃骨骼是唯一例外，但必须休眠且位于独立层：禁用时无法接触任何物体，
            // 死亡唤醒后该层只与世界碰撞，不与战斗者组成部分碰撞。
            foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
            {
                Assert.IsTrue(
                    collider.gameObject.layer == Layers.Ragdoll && !collider.enabled,
                    $"The player's body carries a live collider ({collider.name}), which would fight the character controller.");
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryVisibleSurfaceHasAMaterial()
        {
            // 删除生成材质以重新调节后，武器模型仍指向已不存在的资源，整个套件仍全绿，
            // 因为没有测试断言渲染器材质引用确实有效。材质缺失会显示 Unity洋红错误着色器，
            // 游玩时极其明显，测试却看不见。
            foreach (Renderer renderer in
                     Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                // 拖尾和粒子自带材质，允许与其它材质不同。
                if (renderer is TrailRenderer || renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                Assert.IsNotNull(
                    renderer.sharedMaterial,
                    $"'{renderer.name}' has no material and will render as the magenta error shader.");

                Assert.IsNotNull(
                    renderer.sharedMaterial.shader,
                    $"'{renderer.name}' has a material with no shader.");
            }

            yield return null;
        }

        private static float TopOf(RectTransform rect) =>
            rect.anchoredPosition.y + (rect.sizeDelta.y * 0.5f);

        private static float BottomOf(RectTransform rect) =>
            rect.anchoredPosition.y - (rect.sizeDelta.y * 0.5f);

        /// <summary>在界面任意位置查找命名文本元素，包括未激活面板。</summary>
        private static RectTransform FindText(string objectName)
        {
            foreach (UnityEngine.UI.Text text in
                     Resources.FindObjectsOfTypeAll<UnityEngine.UI.Text>())
            {
                // 包括未激活对象，因为结算面板在战斗结束前隐藏；仅取场景对象，
                // 因为 FindObjectsOfTypeAll也返回其对应预制体资源。
                if (text.name == objectName && text.gameObject.scene.IsValid())
                {
                    return text.rectTransform;
                }
            }

            return null;
        }

        /// <summary>按名称查找已加载通道资源。</summary>
        private static FloatEventChannel FindChannel(string assetName)
        {
            foreach (FloatEventChannel candidate in
                     Resources.FindObjectsOfTypeAll<FloatEventChannel>())
            {
                if (candidate.name == assetName)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>从一侧构建简单且不可避免的攻击。</summary>
        /// <remarks>
        /// 刻意忽略无敌：此处测试伤害管线是否可用，而非防御保护是否生效；
        /// 否则恰好开启的闪避无敌帧会使结果依赖时机。
        /// </remarks>
        private static DamageInfo DamageFrom(CombatantTeam source) => new DamageInfo
        {
            Amount = TestDamage,
            Type = DamageType.Light,
            SourceTeam = source,
            SourceInstanceId = -1,
            HitPoint = Vector3.zero,
            HitDirection = Vector3.forward,
            KnockbackSpeed = 0f,
            PoiseDamage = 0f,
            Stagger = StaggerStrength.None,
            HitStopSeconds = 0f,
            IgnoresInvulnerability = true
        };

        /// <summary>在命名的 HUD 分组内查找命名资源条。</summary>
        private static ResourceBar FindBar(string groupName, string barName)
        {
            foreach (ResourceBar bar in Object.FindObjectsByType<ResourceBar>(FindObjectsSortMode.None))
            {
                if (bar.name == barName && bar.transform.parent != null &&
                    bar.transform.parent.name == groupName)
                {
                    return bar;
                }
            }

            return null;
        }
    }
}
