using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 断言角色播放的每个片段都让身体面向角色朝向。
    /// </summary>
    /// <remarks>
    /// 绑定姿势朝前，片段却可能转开全身；片段携带捕捉时身体方向，结束转身会让角色背离战斗。
    /// 运行截图曾显示骑士受击后朝摄像机。只查绑定姿势无法发现，必须播放各状态并测髋部。
    /// </remarks>
    [TestFixture]
    public sealed class ClipFacingTests
    {
        /// <summary>
        /// 约 70 度以内。剑盾格挡侧身实测 68 度；错误朝向转向背后，评分接近负一。
        /// </summary>
        private const float MinimumAgreement = 0.3f;

        [TestCase("Assets/_Project/Prefabs/Player.prefab")]
        [TestCase("Assets/_Project/Prefabs/Boss.prefab")]
        public void EveryStateFacesTheCharactersForward(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var instance = (GameObject)Object.Instantiate(prefab);

            try
            {
                instance.transform.rotation = Quaternion.identity;
                Animator animator = instance.GetComponentInChildren<Animator>();
                Assume.That(animator != null && animator.isHuman && animator.runtimeAnimatorController != null, "Not rigged.");

                var controller = (AnimatorController)animator.runtimeAnimatorController;
                var failures = new System.Collections.Generic.List<string>();

                foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
                {
                    string state = child.state.name;

                    // 倒地和死亡朝向由倒下位置决定，不是站姿。
                    if (state == "Death" || state == "Airborne" || state == "Roll")
                    {
                        continue;
                    }

                    animator.Rebind();

                    // 起身按最终站立时判断；结束转身会将控制交回背对战斗的骑士。
                    if (state == "KnockedDown")
                    {
                        animator.SetFloat("ReactionTime", 1f);
                    }

                    animator.Play(state, 0, 0.15f);
                    animator.Update(0f);

                    Vector3 hips = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).position -
                                   animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
                    float agreement = Vector3.Dot(Vector3.Cross(hips, Vector3.up).normalized, instance.transform.forward);

                    if (agreement < MinimumAgreement)
                    {
                        failures.Add($"{state} ({agreement:F2})");
                    }
                }

                Assert.IsEmpty(failures, "These states turn the body away from the character's forward: " + string.Join(", ", failures));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
