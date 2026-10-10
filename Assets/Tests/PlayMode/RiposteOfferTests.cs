using System.Collections;
using AdaptiveBossArena.AI;
using AdaptiveBossArena.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AdaptiveBossArena.Tests.PlayMode
{
    /// <summary>
    /// 测试首领破防的惩罚机会由玩家选择，而非自动执行。
    /// </summary>
    /// <remarks>
    /// 反击原先在破防瞬间自动触发，无论玩家处于何种状态、何种距离，都会打断其当前行为；
    /// 战斗最精彩的时刻无需玩家参与就发生。现在它等待玩家走近、面向首领并按下攻击。
    /// </remarks>
    [TestFixture]
    public sealed class RiposteOfferTests
    {
        [UnitySetUp]
        public IEnumerator LoadTheArena()
        {
            SceneManager.LoadScene("Arena", LoadSceneMode.Single);

            float waited = 0f;

            do
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            while (Time.timeScale <= 0f && waited < 15f);
        }

        [UnityTest]
        public IEnumerator ABrokenGuardIsNotPunishedFromAcrossTheArena()
        {
            var player = Object.FindAnyObjectByType<PlayerController>();
            var boss = Object.FindAnyObjectByType<BossController>();

            Assert.IsNotNull(player);
            Assert.IsNotNull(boss);

            // 明显超出攻击范围，因此无论防御破得多严重，都不能提供可执行机会。
            boss.transform.position = player.transform.position + Vector3.forward * 9f;
            boss.Poise.ApplyPoiseDamage(1000f);

            yield return null;

            Assert.IsTrue(boss.Poise.IsBroken, "The boss's guard did not break.");

            float watched = 0f;

            while (watched < 0.6f)
            {
                yield return null;
                watched += Time.deltaTime;

                Assert.IsFalse(player.RiposteOffered, "A riposte was offered from across the arena.");
            }

            Assert.IsTrue(boss.Poise.IsBroken, "The break was spent without the player pressing anything.");
        }
    }
}
