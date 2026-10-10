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
    /// 测试命中的攻击会明显移动身体，却不使战斗者失去站姿。
    /// </summary>
    /// <remarks>
    /// 此前不会造成硬直的命中仅微移角色整个视觉根节点，看起来像模型抽动，而非身体挨打。
    /// </remarks>
    [TestFixture]
    public sealed class HitReactionTests
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
        public IEnumerator ABlowFlinchesTheUpperBodyAndThenSettles()
        {
            var player = Object.FindAnyObjectByType<PlayerController>();
            var bridge = player.GetComponentInChildren<CharacterAnimationBridge>();
            Assume.That(bridge != null && bridge.HasSkeleton, "The player is not rigged.");

            // 等待期间首领会持续攻击，每次命中都会重新开始受击动作。
            Object.FindAnyObjectByType<AI.BossController>().gameObject.SetActive(false);

            player.TakeDamage(new DamageInfo
            {
                Amount = 1f,
                Type = DamageType.BossMelee,
                SourceTeam = CombatantTeam.Boss,
                SourceInstanceId = 99,
                HitDirection = Vector3.forward,
                Stagger = StaggerStrength.None
            });

            // 按时间而非帧数等待：无界面模式下一帧约一毫秒，远短于受击动作的上升阶段。
            float peak = 0f, watched = 0f;

            while (watched < 0.4f)
            {
                yield return null;
                watched += Time.deltaTime;
                peak = Mathf.Max(peak, bridge.HitWeight);
            }

            Assert.Greater(peak, 0.5f, "A landed blow did not flinch the upper body.");

            yield return new WaitForSeconds(2.5f);

            Assert.AreEqual(0f, bridge.HitWeight, 0.001f, "The flinch never settled back to the stance.");
        }
    }
}
