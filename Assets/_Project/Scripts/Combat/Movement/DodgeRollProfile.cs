using UnityEngine;

namespace AdaptiveBossArena.Combat.Movement
{
    /// <summary>
    /// 闪避翻滚过程中随时间变化的速度。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 过去闪避是以固定 25 米每秒滑行 0.18 秒：恒定速度、瞬间起步、瞬间停止。它能移动位置，却缺少身体感，
    /// 看起来像带拖影的传送。
    /// </para>
    /// <para>
    /// 翻滚先爆发，再随着收势减速。速度按平方曲线下降，因此大部分距离在前段完成——也就是无敌阶段——尾段
    /// 足够缓慢，可以自然取消。峰值根据距离和时长推导，因此无论持续时长怎样调整，
    /// 翻滚总会移动恰好配置的距离。
    /// </para>
    /// </remarks>
    public static class DodgeRollProfile
    {
        /// <summary>翻滚在某个时刻的速度。</summary>
        /// <param name="elapsedSeconds">自翻滚开始以来的时间。</param>
        /// <param name="durationSeconds">翻滚总时长。</param>
        /// <param name="distance">翻滚在持续时间内经过的地面距离。</param>
        /// <returns>速度，单位为米每秒；翻滚之外为零。</returns>
        public static float SpeedAt(float elapsedSeconds, float durationSeconds, float distance)
        {
            if (durationSeconds <= 0f || elapsedSeconds < 0f || elapsedSeconds >= durationSeconds)
            {
                return 0f;
            }

            float remaining = 1f - elapsedSeconds / durationSeconds;

            return PeakSpeed(durationSeconds, distance) * remaining * remaining;
        }

        /// <summary>翻滚最初时刻的速度。</summary>
        /// <remarks>平方衰减曲线下的面积等于其高度的三分之一，因此峰值为平均值的三倍。</remarks>
        /// <param name="durationSeconds">翻滚总时长。</param>
        /// <param name="distance">翻滚经过的地面距离。</param>
        /// <returns>峰值速度，单位为米每秒。</returns>
        public static float PeakSpeed(float durationSeconds, float distance) =>
            durationSeconds <= 0f ? 0f : 3f * Mathf.Max(0f, distance) / durationSeconds;
    }
}
