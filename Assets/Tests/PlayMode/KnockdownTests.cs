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
    /// 在真实竞技场中测试玩家击倒、挑飞和撞墙。
    /// </summary>
    /// <remarks>
    /// 反应门控规则已由 EditMode 测试固定。此处检查单元测试无法覆盖的部分：状态机、
    /// 移动器和伤害路径确实遵守规则，使倒地玩家真的不可伤害，也真的能够起身。
    /// </remarks>
    [TestFixture]
    public sealed class KnockdownTests
    {
        private const int WarmUpFrames = 10;

        private PlayerController _player;
        private PlayerContext _context;

        [UnitySetUp]
        public IEnumerator LoadTheArena()
        {
            SceneManager.LoadScene("Arena", LoadSceneMode.Single);

            for (int i = 0; i < WarmUpFrames; i++)
            {
                yield return null;
            }

            _player = Object.FindAnyObjectByType<PlayerController>();
            Assert.IsNotNull(_player, "No player in the arena scene.");

            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            _context = (PlayerContext)typeof(PlayerController).GetField("_context", flags).GetValue(_player);

            yield return WaitForTheFightToStart();
        }

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

        /// <summary>只请求受击反应的首领攻击，避免其它系统干扰结果。</summary>
        private static DamageInfo Blow(ImpactReaction reaction, float amount = 0.001f) => new DamageInfo
        {
            Amount = amount,
            Type = DamageType.BossMelee,
            SourceTeam = CombatantTeam.Boss,
            SourceInstanceId = 99,
            HitDirection = Vector3.forward,
            Stagger = StaggerStrength.None,
            Reaction = reaction
        };

        private ObservableActionState Seen => _player.CaptureObservation(0f).ActionState;

        private IEnumerator WaitUntilSeen(ObservableActionState state, float giveUpAfterSeconds)
        {
            float waited = 0f;

            while (Seen != state && waited < giveUpAfterSeconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(state, Seen, "The player never reached " + state + ".");
        }

        [UnityTest]
        public IEnumerator ALaunchLandsIntoAKnockdownWhereNothingHurts()
        {
            _player.TakeDamage(Blow(ImpactReaction.Launch));

            yield return WaitUntilSeen(ObservableActionState.Airborne, 1f);

            float start = _player.transform.position.y;
            float peak = start;

            while (Seen == ObservableActionState.Airborne)
            {
                peak = Mathf.Max(peak, _player.transform.position.y);
                yield return null;
            }

            Assert.Greater(peak - start, 0.5f, "The launch never lifted the player off the floor.");
            Assert.AreEqual(ObservableActionState.KnockedDown, Seen, "Landing did not floor the player.");

            float health = _context.Health.Current;
            DamageResult slam = _player.TakeDamage(Blow(ImpactReaction.Knockdown, 25f));

            Assert.AreEqual(DamageOutcome.Ignored, slam.Outcome, "A slam hurt a player lying on the floor.");
            Assert.AreEqual(health, _context.Health.Current, 0.0001f);

            // 玩家也确实起身。
            float waited = 0f;

            while (Seen == ObservableActionState.KnockedDown && waited < 3f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreNotEqual(ObservableActionState.KnockedDown, Seen, "The player never got up.");
        }

        [UnityTest]
        public IEnumerator ABarrageOfSlamsNeverHoldsThePlayerDownPastTheCap()
        {
            PlayerConfig config = _context.Config;
            float cap = Combat.Vitals.ReactionGate.WorstCaseSecondsWithoutControl(
                config.MaximumAirborneSeconds, config.KnockdownSeconds, config.GetUpSeconds);

            float lostSince = float.NaN, longest = 0f;
            float barrageEnds = _context.Time.CombatTime + 10f;
            int floorings = 0;
            bool wasDown = false;

            while (_context.Time.CombatTime < barrageEnds)
            {
                _player.TakeDamage(Blow(Time.frameCount % 2 == 0 ? ImpactReaction.Knockdown : ImpactReaction.Launch));

                yield return null;

                float now = _context.Time.CombatTime;
                bool down = Seen == ObservableActionState.KnockedDown || Seen == ObservableActionState.Airborne;

                if (down && !wasDown)
                {
                    floorings++;
                    lostSince = now;
                }

                if (!down && wasDown)
                {
                    longest = Mathf.Max(longest, now - lostSince);
                }

                wasDown = down;
            }

            Assert.GreaterOrEqual(floorings, 2, "The barrage did not floor the player repeatedly, so it proved nothing.");
            Assert.LessOrEqual(longest, cap + 0.25f, "A barrage held the player without control past the cap.");
        }

        [UnityTest]
        public IEnumerator APlayerShovedIntoAWallStaggers()
        {
            Vector3 outward = _player.transform.position;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.back;

            Vector3 nearWall = outward * 14f;
            nearWall.y = _player.transform.position.y;
            _context.Motor.Teleport(nearWall);

            yield return null;

            _context.Motor.AddImpulse(outward * 20f);

            float waited = 0f;

            while (Seen != ObservableActionState.Staggered && waited < 1f)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            Assert.AreEqual(ObservableActionState.Staggered, Seen, "A hard shove into the wall did not stagger the player.");
        }
    }
}
