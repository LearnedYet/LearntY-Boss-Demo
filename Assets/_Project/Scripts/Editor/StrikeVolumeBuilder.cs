using AdaptiveBossArena.Combat;
using UnityEngine;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 安装蒙皮身体实际打击所用部位。
    /// </summary>
    /// <remarks>
    /// 仅用于骨架：基础身体没有随挥击移动的肢体，其攻击保留设定体积，
    /// 也符合所有构建裸战斗角色的测试预期。
    /// </remarks>
    public static class StrikeVolumeBuilder
    {
        /// <summary>沿 Transform 前向安装一个攻击部位。</summary>
        /// <param name="mount">其所依附的骨骼或武器。</param>
        /// <param name="part">部位类型。</param>
        /// <param name="baseDistance">沿挂点前向的起点。</param>
        /// <param name="tipDistance">终点。</param>
        /// <param name="radius">厚度。</param>
        /// <returns>已安装部位。</returns>
        public static StrikeVolume Attach(
            Transform mount, StrikerParts part, float baseDistance, float tipDistance, float radius)
        {
            var strike = new GameObject("Strike" + part);
            strike.transform.SetParent(mount, false);

            var volume = strike.AddComponent<StrikeVolume>();
            volume.Configure(part, baseDistance, tipDistance, radius);

            return volume;
        }

        /// <summary>
        /// 在骨架上安装拳、右脚和身体部位。
        /// </summary>
        /// <remarks>
        /// 各骨骼外放短胶囊。骨骼自身轴向无通用意义，因此跨度足够短，使任意朝向都像拳或脚，
        /// 主要由半径决定范围。
        /// </remarks>
        /// <param name="rig">蒙皮身体。</param>
        /// <param name="fistRadius">拳头厚度。</param>
        /// <param name="footRadius">脚部厚度。</param>
        /// <param name="bodyRadius">身体厚度，用于承载体重的跃击或冲锋。</param>
        public static void AttachLimbs(Animator rig, float fistRadius, float footRadius, float bodyRadius)
        {
            if (rig == null || !rig.isHuman)
            {
                return;
            }

            AttachToBone(rig, HumanBodyBones.RightHand, StrikerParts.RightHand, fistRadius);
            AttachToBone(rig, HumanBodyBones.LeftHand, StrikerParts.LeftHand, fistRadius);
            AttachToBone(rig, HumanBodyBones.RightFoot, StrikerParts.RightFoot, footRadius);
            AttachToBone(rig, HumanBodyBones.Hips, StrikerParts.Body, bodyRadius);
        }

        private static void AttachToBone(Animator rig, HumanBodyBones bone, StrikerParts part, float radius)
        {
            Transform mount = rig.GetBoneTransform(bone);

            if (mount != null)
            {
                Attach(mount, part, 0f, 0.1f, radius);
            }
        }
    }
}
