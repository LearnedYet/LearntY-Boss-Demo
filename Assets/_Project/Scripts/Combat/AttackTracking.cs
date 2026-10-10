using UnityEngine;

namespace AdaptiveBossArena.Combat
{
    /// <summary>
    /// 攻击者是否仍可转向追踪目标。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 过去首领在整个前摇持续转向目标，并逐帧沿新朝向应用突进，导致冲锋直到命中瞬间仍会追踪。
    /// 较晚侧闪永远无效，违背此类游戏投入挥击的表现：攻击者早期追踪，之后锁定，
    /// 精准闪避击败的正是锁定部分。
    /// </para>
    /// <para>
    /// 追踪只持续前摇最初一部分，之后锁定，直到挥击结束。锁定也覆盖剩余前摇，
    /// 因为前摇最后几帧若仍可转向，就会追着闪避进入命中阶段。
    /// </para>
    /// </remarks>
    public static class AttackTracking
    {
        /// <summary>攻击者是否仍可转向目标。</summary>
        /// <param name="phase">挥击当前阶段。</param>
        /// <param name="elapsedSeconds">挥击开始后经过的时间。</param>
        /// <param name="startupSeconds">前摇时长。</param>
        /// <param name="trackingWindowFraction">允许追踪的前摇时间比例。</param>
        /// <returns>允许追踪期间为 true。</returns>
        public static bool CanTrack(
            AttackPhase phase,
            float elapsedSeconds,
            float startupSeconds,
            float trackingWindowFraction)
        {
            if (phase != AttackPhase.Startup)
            {
                return false;
            }

            return elapsedSeconds < startupSeconds * Mathf.Clamp01(trackingWindowFraction);
        }
    }
}
