using UnityEngine;

namespace AdaptiveBossArena.Combat.Movement
{
    /// <summary>
    /// 移动中的身体请求改变方向时，保留多少速度。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 过去骑士能毫无代价地以全速反向：0.06 秒达到最高速度，每秒转向 1080 度，
    /// 速度直接转向摇杆所指位置。这完全没有质量感，正是移动显得轻飘的主要原因。
    /// </para>
    /// <para>
    /// 带着速度的身体想去另一个方向，必须先消减部分速度。转弯越急、原速度越快，损失越多；
    /// 慢跑时平缓转弯几乎没有代价，冲刺时完全反向会损失大部分速度。这个规则让角色显得沉重，
    /// 却不会失去响应性，因为从静止起步不受影响。
    /// </para>
    /// </remarks>
    public static class MomentumRules
    {
        /// <summary>低于最高速度的此比例时，身体可自由转向。</summary>
        /// <remarks>
        /// 静止或缓慢移动的角色没有需要克服的动量，对它收取转向代价会让向新方向起步显得迟钝，
        /// 而不是有重量。
        /// </remarks>
        private const float FreeTurnSpeedFraction = 0.2f;

        /// <summary>
        /// 转向新方向时，身体可追求的最高速度比例。
        /// </summary>
        /// <param name="currentVelocity">当前水平速度。</param>
        /// <param name="desiredDirection">身体希望移动的方向，无需归一化。</param>
        /// <param name="topSpeed">身体的最高速度。</param>
        /// <param name="speedFloorOnReversal">
        /// 全速完全反向时保留的最高速度比例。
        /// </param>
        /// <returns>最高速度倍率，从下限到一。</returns>
        public static float TargetSpeedFactor(
            Vector3 currentVelocity,
            Vector3 desiredDirection,
            float topSpeed,
            float speedFloorOnReversal)
        {
            currentVelocity.y = 0f;
            desiredDirection.y = 0f;

            if (topSpeed <= 0f || desiredDirection.sqrMagnitude < Mathf.Epsilon)
            {
                return 1f;
            }

            float speedFraction = Mathf.Clamp01(currentVelocity.magnitude / topSpeed);

            if (speedFraction <= FreeTurnSpeedFraction)
            {
                return 1f;
            }

            float turn01 = Vector3.Angle(currentVelocity, desiredDirection) / 180f;

            // 按超出自由转向速度的程度缩放，让代价随动量平滑增长，而不是在阈值处突然开启。
            float momentum01 = Mathf.InverseLerp(FreeTurnSpeedFraction, 1f, speedFraction);
            float cost = turn01 * momentum01;

            return Mathf.Lerp(1f, Mathf.Clamp01(speedFloorOnReversal), cost);
        }
    }
}
