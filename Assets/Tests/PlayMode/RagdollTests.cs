using System.Collections;
using AdaptiveBossArena.Combat.Feel;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AdaptiveBossArena.Tests.PlayMode
{
    /// <summary>
    /// 测试战斗者死亡时身体会按物理倒下，重试时重新站起。
    /// </summary>
    [TestFixture]
    public sealed class RagdollTests
    {
        private PlayerController _player;
        private RagdollActivator _ragdoll;

        [UnitySetUp]
        public IEnumerator LoadTheArena()
        {
            SceneManager.LoadScene("Arena", LoadSceneMode.Single);

            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            _player = Object.FindAnyObjectByType<PlayerController>();
            Assert.IsNotNull(_player, "No player in the arena scene.");

            _ragdoll = _player.GetComponentInChildren<RagdollActivator>();
            Assert.IsNotNull(_ragdoll, "The rigged player was built without a ragdoll.");

            float waited = 0f;

            while (Time.timeScale <= 0f && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private static DamageInfo KillingBlow => new DamageInfo
        {
            Amount = 10000f,
            Type = DamageType.BossMelee,
            SourceTeam = CombatantTeam.Boss,
            SourceInstanceId = 99,
            HitDirection = Vector3.forward,
            KnockbackSpeed = 6f,
            Stagger = StaggerStrength.None
        };

        [UnityTest]
        public IEnumerator TheBodyIsDormantWhileTheFighterLives()
        {
            yield return null;

            Assert.AreEqual(11, _ragdoll.Bodies.Length, "The ragdoll is missing bones.");

            foreach (Rigidbody body in _ragdoll.Bodies)
            {
                Assert.IsTrue(body.isKinematic, body.name + " is simulated on a living fighter.");
                Assert.IsFalse(body.GetComponent<Collider>().enabled, body.name + " collides on a living fighter.");
            }
        }

        [UnityTest]
        public IEnumerator DeathDropsTheBodyAndARetryStandsItBackUp()
        {
            Rigidbody hips = _ragdoll.Bodies[0];
            Rigidbody head = _ragdoll.Bodies[2];

            // 倒下意味着头部或髋部大幅下降，而非要求其中指定一处下降；身体最终姿势取决于落地方式。
            // 目前观察到俯卧与跪倒（头高 0.14 米，髋仍高 0.58 米），以及瘫坐（髋高 0.29 米，
            // 头仍高 0.77 米）。两者明显在地面，而站立身体两者均不会如此。
            float standingHead = head.position.y;
            float standingHips = hips.position.y;

            _player.TakeDamage(KillingBlow);

            Assert.IsTrue(_ragdoll.IsActive, "The killing blow did not wake the ragdoll.");

            // 按游戏时间而非真实时间：死亡以慢动作播放，固定真实时间窗口会在身体尚未完全倒下时测量，
            // 测试通过或失败便取决于慢动作恰好怎样发生。
            float waited = 0f, realWaited = 0f;

            while (waited < 2f && realWaited < 12f)
            {
                waited += Time.deltaTime;
                realWaited += Time.unscaledDeltaTime;

                Assert.IsFalse(float.IsNaN(hips.position.y), "The ragdoll produced a NaN position.");

                yield return null;
            }

            // 按站立高度比例而非固定下降距离：身体可能最终跪倒，使头下降约三分之一，
            // 远超任何硬直或晃动，却小于固定半米。
            bool headDown = head.position.y < standingHead * 0.75f;
            bool hipsDown = hips.position.y < standingHips * 0.7f;

            Assert.IsTrue(headDown || hipsDown,
                $"The body never went down: head {head.position.y:F2} m from {standingHead:F2}, hips {hips.position.y:F2} m from {standingHips:F2}, " +
                $"after {waited:F2} s game time / {realWaited:F2} s real, time scale {Time.timeScale:F2}, " +
                $"player at {_player.transform.position}.");

            _player.ResetForNewAttempt(new Vector3(0f, 0f, -6f));

            yield return null;

            Assert.IsFalse(_ragdoll.IsActive);
            Assert.IsTrue(_player.GetComponentInChildren<Animator>().enabled, "The retry left the Animator off.");

            foreach (Rigidbody body in _ragdoll.Bodies)
            {
                Assert.IsTrue(body.isKinematic, body.name + " is still simulated after a retry.");
            }
        }
    }
}
