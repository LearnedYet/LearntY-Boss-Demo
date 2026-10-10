using UnityEngine;

namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 战斗者相对于朝向的移动方向，用于方向性移动混合。
    /// </summary>
    /// <remarks>
    /// 过去单一速度驱动始终向前迈步的行走和奔跑，因此绕对手转圈的首领，或举着防御后退的骑士，
    /// 会用向前走的双腿向侧面或后方滑动。将移动拆成朝向的横向与纵向分量，让混合能选择侧移和后退步伐。
    /// </remarks>
    public static class LocomotionDirection
    {
        /// <summary>低于此位移时，单帧无法提供可靠方向，单位为米。</summary>
        public const float MinimumDisplacement = 0.0005f;

        /// <summary>本帧移动对应的混合位置。</summary>
        /// <param name="worldDisplacement">战斗者本帧在世界空间的位移。</param>
        /// <param name="facing">战斗者朝向。</param>
        /// <param name="speed01">水平速度占最高速度的比例，决定与混合中心的距离。</param>
        /// <param name="previous">上帧结果；本帧移动太小时保留。</param>
        /// <returns>按速度缩放的朝向横向（x）与纵向（y）分量。</returns>
        public static Vector2 BlendPosition(Vector3 worldDisplacement, Quaternion facing, float speed01, Vector2 previous)
        {
            speed01 = Mathf.Clamp01(speed01);
            var planar = new Vector3(worldDisplacement.x, 0f, worldDisplacement.z);

            if (planar.magnitude < MinimumDisplacement)
            {
                // 静止或冻结帧时保留方向，让混合缓慢退出而不是跳变。
                return previous.sqrMagnitude > 1e-8f ? previous.normalized * speed01 : new Vector2(0f, speed01);
            }

            Vector3 local = Quaternion.Inverse(facing) * planar.normalized;
            return new Vector2(local.x, local.z).normalized * speed01;
        }
    }
}
