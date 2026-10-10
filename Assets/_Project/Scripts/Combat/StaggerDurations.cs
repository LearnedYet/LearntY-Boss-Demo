using AdaptiveBossArena.Core.Combat;

namespace AdaptiveBossArena.Combat
{
    /// <summary>
    /// 各类硬直剥夺控制权的时长。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 玩家与首领共用，使姿态崩溃对双方造成相近的窗口。玩家只需学习一套硬直规则而非两套，
    /// 战斗才能保持可读。
    /// </para>
    /// <para>
    /// 这些是失去控制的时间，因此刻意保持短暂。足够接完整连段的硬直会让一次失误损失整条生命，
    /// 使动作游戏显得不公，而非要求高。
    /// </para>
    /// </remarks>
    public static class StaggerDurations
    {
        /// <summary>轻微受击不打断动作，只在视觉上表现。</summary>
        public const float FlinchSeconds = 0.12f;

        /// <summary>打断会取消当前动作，并损失一个节拍。</summary>
        public const float InterruptSeconds = 0.35f;

        /// <summary>姿态崩溃开启真实惩罚窗口。</summary>
        public const float BreakSeconds = 0.9f;

        /// <summary>确定某类硬直的失控时间。</summary>
        /// <param name="strength">硬直类别。</param>
        /// <returns>时长，单位为秒；命中不打断时为零。</returns>
        public static float For(StaggerStrength strength)
        {
            switch (strength)
            {
                case StaggerStrength.Flinch: return FlinchSeconds;
                case StaggerStrength.Interrupt: return InterruptSeconds;
                case StaggerStrength.Break: return BreakSeconds;
                default: return 0f;
            }
        }
    }
}
