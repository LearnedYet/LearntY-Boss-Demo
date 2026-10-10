using System.Collections;
using AdaptiveBossArena.AI;
using AdaptiveBossArena.Combat.Feel;
using AdaptiveBossArena.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AdaptiveBossArena.Tests.PlayMode
{
    /// <summary>
    /// 断言战斗者身体播放所有位移动画时始终位于自身根节点之上，不会远离根节点。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mixamo 攻击和翻滚片段携带地面位移。该位移烘焙进姿势后，
    /// 使身体离开由移动器驱动且承载受击框的根节点数米：滑行攻击让巨兽髋部远在可受击位置前方，
    /// 身体突进两次，一次来自移动器、一次来自动画；片段结束时又跳回。这正是玩家报告的滑动和跳变。
    /// </para>
    /// <para>
    /// 运行时通过真实 Animator 摆姿势，因为实际位移是否存在取决于此路径；
    /// 编辑器采样同一片段时使用不同的根运动规则。
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class BodyStaysOnRootTests
    {
        /// <summary>
        /// 髋部可偏离根节点的距离。突进或下蹲会使它少量偏移，烘焙的滑行却使它移动数米。
        /// </summary>
        private const float MaxHipsOffset = 0.9f;

        private const int Samples = 20;

        [UnitySetUp]
        public IEnumerator LoadTheArena()
        {
            SceneManager.LoadScene("Arena", LoadSceneMode.Single);

            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator TheBruteNeverLeavesItsRoot()
        {
            var boss = Object.FindAnyObjectByType<BossController>();
            Assert.IsNotNull(boss, "No boss in the arena scene.");
            yield return AssertStaysOnRoot(boss.transform, "Dash", "Spin", "Leap", "Grab", "Overhead", "Light2");
        }

        [UnityTest]
        public IEnumerator TheKnightNeverLeavesItsRoot()
        {
            var player = Object.FindAnyObjectByType<PlayerController>();
            Assert.IsNotNull(player, "No player in the arena scene.");
            yield return AssertStaysOnRoot(player.transform, CharacterAnimatorParameters.RollState, "Light1", "Heavy", "Riposte");
        }

        private static IEnumerator AssertStaysOnRoot(Transform root, params string[] states)
        {
            Animator animator = root.GetComponentInChildren<Animator>();
            Assume.That(animator != null && animator.isHuman, "Not rigged.");

            // 测量姿势期间，不能有其它模块驱动姿势。
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>())
            {
                behaviour.enabled = false;
            }

            yield return null;

            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            int attackTime = Animator.StringToHash(CharacterAnimatorParameters.AttackTime);
            var failures = new System.Collections.Generic.List<string>();

            foreach (string state in states)
            {
                if (!animator.HasState(0, Animator.StringToHash(state)))
                {
                    continue;
                }

                float worst = 0f;

                for (int i = 0; i < Samples; i++)
                {
                    float fraction = i / (Samples - 1f);
                    animator.SetFloat(attackTime, fraction);
                    animator.Play(state, 0, fraction);
                    animator.Update(0f);

                    Vector3 offset = hips.position - root.position;
                    offset.y = 0f;
                    worst = Mathf.Max(worst, offset.magnitude);
                }

                if (worst > MaxHipsOffset)
                {
                    failures.Add($"{state} ({worst:F2} m)");
                }
            }

            Assert.IsEmpty(failures,
                "These clips carry the body away from the root its hurtbox rides on: " + string.Join(", ", failures));
        }
    }
}
