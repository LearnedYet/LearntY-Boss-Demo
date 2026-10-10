using UnityEngine;

namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 将攻击时间轴映射到片段，让动画恰好在命中盒生效时打击。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 命中检测由攻击自身帧数据定义，是挥击何时命中的权威依据。片段有自己的刀刃落下时机，两者不会匹配：
    /// 按自然速度播放，刀刃会在命中盒开启之前或之后明显抵达。
    /// </para>
    /// <para>
    /// 因此对片段分段变形。前摇播放蓄势直到接触点之前，生效窗口播放接触动作，后摇播放余下部分。
    /// 同一片段能服务快速轻击和缓慢重击，并分别在自身生效窗口内落下。输入是攻击经过时间，顿帧期间停止，
    /// 因此姿势和其他对象一起冻结在命中时刻。
    /// </para>
    /// </remarks>
    public readonly struct AttackClipTimeWarp
    {
        /// <summary>为一次攻击和一个片段创建时间变形。</summary>
        /// <param name="startupSeconds">攻击前摇。</param>
        /// <param name="activeSeconds">攻击生效窗口。</param>
        /// <param name="recoverySeconds">攻击后摇。</param>
        /// <param name="clipLengthSeconds">片段自然时长。</param>
        /// <param name="clipContactSeconds">刀刃在片段自身时间中的落下时刻。</param>
        /// <param name="contactSpanSeconds">生效窗口播放的接触点周围片段时间。</param>
        public AttackClipTimeWarp(
            float startupSeconds,
            float activeSeconds,
            float recoverySeconds,
            float clipLengthSeconds,
            float clipContactSeconds,
            float contactSpanSeconds)
        {
            StartupSeconds = Mathf.Max(0f, startupSeconds);
            ActiveSeconds = Mathf.Max(0f, activeSeconds);
            RecoverySeconds = Mathf.Max(0f, recoverySeconds);
            ClipLengthSeconds = Mathf.Max(Mathf.Epsilon, clipLengthSeconds);

            float halfSpan = Mathf.Max(0f, contactSpanSeconds) * 0.5f;
            float contact = Mathf.Clamp(clipContactSeconds, 0f, ClipLengthSeconds);

            ContactStart = Mathf.Clamp(contact - halfSpan, 0f, ClipLengthSeconds);
            ContactEnd = Mathf.Clamp(contact + halfSpan, ContactStart, ClipLengthSeconds);
        }

        /// <summary>攻击前摇。</summary>
        public float StartupSeconds { get; }

        /// <summary>攻击生效窗口。</summary>
        public float ActiveSeconds { get; }

        /// <summary>攻击后摇。</summary>
        public float RecoverySeconds { get; }

        /// <summary>片段自然时长。</summary>
        public float ClipLengthSeconds { get; }

        /// <summary>生效窗口开始时的片段时间。</summary>
        public float ContactStart { get; }

        /// <summary>生效窗口结束时的片段时间。</summary>
        public float ContactEnd { get; }

        /// <summary>攻击某一时刻应显示的片段时间。</summary>
        /// <param name="attackElapsedSeconds">自攻击开始以来的时间。</param>
        /// <returns>片段内的秒数。</returns>
        public float ClipTimeAt(float attackElapsedSeconds)
        {
            float t = Mathf.Max(0f, attackElapsedSeconds);

            if (t < StartupSeconds)
            {
                return Remap(t, 0f, StartupSeconds, 0f, ContactStart);
            }

            float activeEnd = StartupSeconds + ActiveSeconds;

            if (t < activeEnd)
            {
                return Remap(t, StartupSeconds, activeEnd, ContactStart, ContactEnd);
            }

            return Remap(t, activeEnd, activeEnd + RecoverySeconds, ContactEnd, ClipLengthSeconds);
        }

        /// <summary>以片段比例表示的相同位置，供 Animator 的归一化时间使用。</summary>
        /// <param name="attackElapsedSeconds">自攻击开始以来的时间。</param>
        /// <returns>归一化片段时间，从零到一。</returns>
        public float NormalizedTimeAt(float attackElapsedSeconds) =>
            Mathf.Clamp01(ClipTimeAt(attackElapsedSeconds) / ClipLengthSeconds);

        private static float Remap(float value, float fromStart, float fromEnd, float toStart, float toEnd)
        {
            if (fromEnd - fromStart <= Mathf.Epsilon)
            {
                return toEnd;
            }

            return Mathf.Lerp(toStart, toEnd, Mathf.Clamp01((value - fromStart) / (fromEnd - fromStart)));
        }
    }
}
