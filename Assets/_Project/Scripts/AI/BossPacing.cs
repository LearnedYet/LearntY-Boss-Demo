using UnityEngine;

namespace AdaptiveBossArena.AI
{
    /// <summary>
    /// Boss 攻击之间移动的速度和方向。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 玩家希望 Boss 有压迫感：阶段间缓慢厚重行走，而非快速抽动。此前恰好相反：
    /// Motor 将全部方向归一化到全速，使巨兽跑着绕圈；接近、后退和绕行又在 0.75米硬边界突然切换，
    /// 使腿部每隔几帧就改主意。
    /// </para>
    /// <para>
    /// 现在它缓步逼近：绕行和衡量玩家时慢走，接近与让步之间平滑混合，只有确实需要跨越距离时才奔跑。
    /// 采用纯函数，使节奏可脱离场景阅读和测试；数值放在 <see cref="BossConfig"/>。
    /// </para>
    /// </remarks>
    public static class BossPacing
    {
        /// <summary>
        /// Boss 偏离首选距离多远时完全投入接近或后退。
        /// </summary>
        /// <remarks>此范围内吸引强度平滑减为零，因此不会跨边界闪烁。</remarks>
        public const float RangeBlendMetres = 2f;

        /// <summary>缓步移动中的横向比例，使 Boss 绕圈而非直线走向玩家。</summary>
        public const float StrafeWeight = 0.6f;

        /// <summary>
        /// 观察期间的移动：方向长度表示行走速度占最高速度的比例。
        /// </summary>
        /// <param name="toPlayer">指向玩家的单位方向。</param>
        /// <param name="distance">与玩家的距离。</param>
        /// <param name="preferredRange">Boss 希望保持的距离。</param>
        /// <param name="stalkFraction">缓步行走速度占最高速度的比例。</param>
        /// <returns>长度表示最高移动速度比例的运动向量。</returns>
        public static Vector3 Stalk(Vector3 toPlayer, float distance, float preferredRange, float stalkFraction)
        {
            toPlayer.y = 0f;

            if (toPlayer.sqrMagnitude < Mathf.Epsilon)
            {
                return Vector3.zero;
            }

            toPlayer.Normalize();

            // 过远为正，过近为负，在首选距离附近平滑经过零。
            float pull = Mathf.Clamp((distance - preferredRange) / RangeBlendMetres, -1f, 1f);
            Vector3 strafe = Vector3.Cross(Vector3.up, toPlayer) * StrafeWeight;
            Vector3 heading = toPlayer * pull + strafe;

            return heading.sqrMagnitude < Mathf.Epsilon
                ? Vector3.zero
                : heading.normalized * Mathf.Clamp01(stalkFraction);
        }

        /// <summary>
        /// 接近攻击目标时的速度，占最高速度的比例。
        /// </summary>
        /// <remarks>
        /// 攻击接近够到目标时厚重行走，只有确实需要跨越距离时才加速跑动；按学习后的进攻性缩放，
        /// 使学会施压的 Boss 更积极接近。只改变数值，不增加规则。
        /// </remarks>
        /// <param name="distanceBeyondReach">玩家位置超出所选攻击范围的距离。</param>
        /// <param name="runDistance">超出攻击范围的距离，达到或超过时 Boss 跑动。</param>
        /// <param name="walkFraction">接近目标时行走速度占最高速度的比例。</param>
        /// <param name="aggression">学习后的进攻性，范围零到一。</param>
        /// <returns>接近速度占最高速度的比例。</returns>
        public static float ApproachSpeed(float distanceBeyondReach, float runDistance, float walkFraction, float aggression)
        {
            float urgency = runDistance <= 0f ? 1f : Mathf.Clamp01(distanceBeyondReach / runDistance);
            float speed = Mathf.Lerp(Mathf.Clamp01(walkFraction), 1f, urgency * urgency);

            return speed * Mathf.Lerp(0.8f, 1f, Mathf.Clamp01(aggression));
        }
    }
}
