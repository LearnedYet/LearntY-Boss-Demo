using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 断言每个绑定骨架的角色身体与根节点朝向一致。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 移动器转动根节点以面向对手，所有判定框也沿根节点前方向编写，因此身体必须与之吻合，
    /// 否则角色会明显背向战斗：挥击绕到身后，伤害却命中前方。导入的人形模型面向 -Z，
    /// 以单位旋转挂接便造成了这种情况：双方都转身背离，镜头从正面观察的首领尤其明显。
    /// 其它所有测试均通过，因为没有任何测试测量身体朝向。
    /// </para>
    /// <para>
    /// 在绑定姿势中根据肩部和髋部测量，避免动画片段的站姿扭转掩盖翻转或伪造翻转。
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class RigFacingTests
    {
        /// <summary>容许约 25 度误差，足以容纳骨架自然的不对称，又远离翻转程度。</summary>
        private const float MinimumAgreement = 0.9f;

        [TestCase("Assets/_Project/Prefabs/Player.prefab")]
        [TestCase("Assets/_Project/Prefabs/Boss.prefab")]
        public void TheBodyFacesTheWayTheCharacterFaces(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, prefabPath + " is missing.");

            var instance = (GameObject)Object.Instantiate(prefab);

            try
            {
                instance.transform.rotation = Quaternion.identity;

                Animator animator = instance.GetComponentInChildren<Animator>();
                Assume.That(animator != null && animator.isHuman, "The character is not rigged; nothing to measure.");

                animator.Rebind();

                Vector3 shoulders = animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position -
                                    animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
                Vector3 hips = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).position -
                               animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;

                // 面向 +Z 的身体右侧为 +X，+X cross +Y 为 +Z，即右向叉乘上向得到朝向。
                float chest = Vector3.Dot(Vector3.Cross(shoulders, Vector3.up).normalized, instance.transform.forward);
                float pelvis = Vector3.Dot(Vector3.Cross(hips, Vector3.up).normalized, instance.transform.forward);

                Assert.GreaterOrEqual(chest, MinimumAgreement,
                    $"The chest faces away from the character's forward (agreement {chest:F2}).");
                Assert.GreaterOrEqual(pelvis, MinimumAgreement,
                    $"The hips face away from the character's forward (agreement {pelvis:F2}).");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
