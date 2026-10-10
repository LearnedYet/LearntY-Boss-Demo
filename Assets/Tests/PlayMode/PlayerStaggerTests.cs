using System.Collections;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Perception;
using AdaptiveBossArena.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AdaptiveBossArena.Tests.PlayMode
{
    /// <summary>
    /// 测试玩家可被打断，但不能永久困在硬直中。
    /// </summary>
    /// <remarks>
    /// 试玩反馈是“下一动作前感觉卡住一秒”和“被击飞后无法落下来”。两者是同一故障：
    /// 每次命中都直接请求硬直，新请求到来时硬直状态会延长，
    /// 因此首领以小于 0.35秒间隔命中就会将玩家定住，无法脱离。首领不会遭遇该问题，
    /// 因为其相同情况通过架势池处理，而架势池破防期间丢弃架势伤害。
    /// </remarks>
    [TestFixture]
    public sealed class PlayerStaggerTests
    {
        private const int WarmUpFrames = 10;

        private PlayerController _player;

        [UnitySetUp]
        public IEnumerator LoadTheArena()
        {
            SceneManager.LoadScene("Arena", LoadSceneMode.Single);

            for (int i = 0; i < WarmUpFrames; i++)
            {
                yield return null;
            }

            _player = Object.FindAnyObjectByType<PlayerController>();
        }

        /// <summary>首领攻击，正是过去每次都直接打断玩家的类型。</summary>
        private static DamageInfo BossBlow(float poiseDamage) => new DamageInfo
        {
            Amount = 1f,
            Type = DamageType.BossMelee,
            SourceTeam = CombatantTeam.Boss,
            SourceInstanceId = 99,
            HitDirection = Vector3.forward,
            PoiseDamage = poiseDamage,
            Stagger = StaggerStrength.Interrupt
        };

        private bool IsStaggered =>
            _player.CaptureObservation(0f).ActionState == ObservableActionState.Staggered;

        /// <summary>等待开场动画放行，且战斗时钟确实运行。</summary>
        /// <remarks>
        /// 遭遇在准备战斗开场期间冻结，因此玩家状态机尚未更新，此时请求硬直只会停留未消费，
        /// 永远不成为状态，看起来与未曾请求硬直完全相同。等待时钟而非固定帧数，
        /// 使测试不依赖开场动画时长。
        /// </remarks>
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

        /// <summary>让延后的硬直请求转为状态。</summary>
        private static IEnumerator LetReactionsResolve()
        {
            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator ARecoilHoldsForTheWholeFreezeItCausedInsteadOfFadingThroughIt()
        {
            // 后仰包络原先按真实时间递减。重击冻结时长与后仰相近，因此世界恢复时推力已消失；
            // 冻结与冲击接连播放，两者都不像命中。
            Assert.IsNotNull(_player, "No player in the arena scene.");
            yield return WaitForTheFightToStart();

            var body = _player.GetComponentInChildren<Combat.Feel.CharacterAnimator>();
            Assert.IsNotNull(body, "The player has no procedural animator.");

            Assert.IsTrue(
                Core.Services.ServiceRegistry.Current.TryGet(out Core.Services.ITimeService time),
                "No time service.");

            Vector3 before = body.transform.localPosition;
            var rig = body.GetComponent<Combat.Feel.CharacterAnimationBridge>();
            bool rigged = rig != null && rig.HasSkeleton;

            time.RequestHitStop(0.5f);
            _player.TakeDamage(BossBlow(1f));

            // 真实时间远超后仰的 0.22 秒，但仍处于冻结期内。
            yield return new WaitForSecondsRealtime(0.3f);

            Vector3 shove = body.transform.localPosition - before;
            shove.y = 0f;

            if (rigged)
            {
                // 绑定骨架的身体以自身受击动画承受攻击，绝不整个人被推过地面：
                // 在动作捕捉片段上再推移整个模型，正是它滑离战斗判定框的原因。
                Assert.Greater(
                    rig.HitWeight, 0.5f,
                    "The flinch had faded before the freeze it belongs to was over.");
                Assert.Less(
                    shove.magnitude, 0.001f,
                    "A rigged body was pushed bodily by the recoil, which slides it off its hitbox.");
            }
            else
            {
                Assert.Greater(
                    shove.magnitude, 0.15f,
                    "The recoil had faded before the freeze it belongs to was over.");
            }
        }

        /// <summary>直接开始翻滚，使测试不依赖模拟输入。</summary>
        private Player.States.PlayerDashState ForceRoll(out PlayerContext context)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

            context = (PlayerContext)typeof(PlayerController).GetField("_context", flags).GetValue(_player);
            var machine = (Core.StateMachine.StateMachine<PlayerContext>)
                typeof(PlayerController).GetField("_machine", flags).GetValue(_player);
            var roll = (Player.States.PlayerDashState)
                typeof(PlayerController).GetField("_dashState", flags).GetValue(_player);

            machine.ForceState(roll);

            return roll;
        }

        [UnityTest]
        public IEnumerator TheRollKeepsItsDistanceAndItsInvincibilityWindow()
        {
            // 闪避变为更长的翻滚。首领调节所依据的两个数值不能随之变化：闪避位移距离，
            // 以及无敌结束时刻。
            Assert.IsNotNull(_player);
            yield return WaitForTheFightToStart();

            PlayerContext context = ForceRollAwayFromTheBoss();
            Vector3 start = _player.transform.position;
            float startedAt = context.Time.CombatTime;
            float lastInvincible = 0f;

            while (context.Time.CombatTime - startedAt < context.Config.DashDurationSeconds + 0.05f)
            {
                if (context.IsInvulnerable)
                {
                    lastInvincible = context.Time.CombatTime - startedAt;
                }

                yield return null;
            }

            Vector3 travelled = _player.transform.position - start;
            travelled.y = 0f;

            Assert.AreEqual(context.Config.DashDistance, travelled.magnitude, 0.6f,
                "The roll no longer carries the player its configured distance.");
            Assert.AreEqual(0.135f, lastInvincible, 0.03f,
                "The roll's invincibility no longer ends where the boss was tuned to expect.");
        }

        [UnityTest]
        public IEnumerator ABufferedAttackComesOutOfTheRollsTail()
        {
            Assert.IsNotNull(_player);
            yield return WaitForTheFightToStart();

            PlayerContext context = ForceRollAwayFromTheBoss();
            float startedAt = context.Time.CombatTime;

            // 在允许取消的翻滚尾段按下。
            while (context.Time.CombatTime - startedAt < context.Config.DashDurationSeconds * 0.75f)
            {
                yield return null;
            }

            context.InputBuffer.Record(Player.Controls.PlayerInputAction.LightAttack, context.Time.CombatTime);

            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }

            Assert.IsTrue(context.Attacks.IsRunning, "An attack pressed in the roll's tail did not come out.");
            Assert.Less(context.Time.CombatTime - startedAt, context.Config.DashDurationSeconds + 0.02f,
                "The attack waited for the roll to finish instead of cancelling it.");
        }

        private PlayerContext ForceRollAwayFromTheBoss()
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var context = (PlayerContext)typeof(PlayerController).GetField("_context", flags).GetValue(_player);

            // 背向首领并面向空地，使首领和墙壁都不会缩短翻滚距离。
            context.Motor.SnapToDirection(Vector3.back);
            ForceRoll(out _);

            return context;
        }

        [UnityTest]
        public IEnumerator ASingleHitDoesNotStopThePlayer()
        {
            Assert.IsNotNull(_player, "No player in the arena scene.");
            yield return WaitForTheFightToStart();

            Assert.Greater(_player.Posture.Current, 30f, "The player began with no posture to spend.");

            _player.TakeDamage(BossBlow(30f));

            yield return LetReactionsResolve();

            Assert.IsFalse(
                IsStaggered,
                "One hit interrupted a player whose posture was nowhere near spent.");

            Assert.Less(_player.Posture.Current, _player.Posture.Maximum, "The hit cost no posture.");
        }

        [UnityTest]
        public IEnumerator EnoughHitsInARowStillBreakThePlayer()
        {
            Assert.IsNotNull(_player, "No player in the arena scene.");
            yield return WaitForTheFightToStart();

            // 吸收攻击不能等同忽略攻击。持续连击最终必须奏效，否则玩家根本无法被压制。
            for (int i = 0; i < 5; i++)
            {
                _player.TakeDamage(BossBlow(30f));
            }

            Assert.IsTrue(_player.Posture.IsBroken, "A sustained flurry never broke the player's posture.");

            yield return LetReactionsResolve();

            Assert.IsTrue(IsStaggered, "The posture broke but the player was never interrupted.");
        }

        [UnityTest]
        public IEnumerator BeingHitWhileBrokenDoesNotHoldThePlayerThere()
        {
            Assert.IsNotNull(_player, "No player in the arena scene.");
            yield return WaitForTheFightToStart();

            for (int i = 0; i < 5; i++)
            {
                _player.TakeDamage(BossBlow(30f));
            }

            yield return LetReactionsResolve();
            Assert.IsTrue(IsStaggered, "Setup failed: the player was not broken to begin with.");

            // 确切的故障：持续攻击已倒地的玩家。每次攻击原先都请求新的硬直，状态取两者较长值，
            // 因此只要首领继续挥击，玩家就永远无法起身。
            float waited = 0f;

            while (waited < 4f)
            {
                _player.TakeDamage(BossBlow(30f));

                waited += Time.deltaTime;

                yield return null;

                if (!IsStaggered)
                {
                    break;
                }
            }

            Assert.IsFalse(
                IsStaggered,
                "The player was still pinned after four seconds of being hit while down.");
        }
    }
}
