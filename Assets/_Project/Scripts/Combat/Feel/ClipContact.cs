namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 根据武器手移动速度，找出攻击片段中打击落下的时刻。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 片段时间变形将攻击的生效命中窗口映射到片段接触时刻，让刀刃可见的打击与实际命中一致。
    /// 这只在每个片段的时刻正确时成立。挥击在命中时最快，因此接触点取武器手速度达到峰值时的片段进度。
    /// </para>
    /// <para>
    /// 忽略片段开头和结尾：Mixamo 片段常在末尾将手快速收回防御，或在开头突然离开前一个姿势，
    /// 两者都不是打击。
    /// </para>
    /// </remarks>
    public static class ClipContact
    {
        /// <summary>认为打击可能落下的最早位置，以片段比例表示。</summary>
        public const float EarliestContact = 0.15f;

        /// <summary>认为打击可能落下的最晚位置，以片段比例表示。</summary>
        public const float LatestContact = 0.85f;

        /// <summary>无法测量片段时使用的接触点。</summary>
        public const float DefaultContact = 0.45f;

        /// <summary>可信窗口内，手速度最快时的片段比例。</summary>
        /// <param name="handSpeeds">从片段开始到结束均匀采样的手部速度。</param>
        /// <returns>以片段时长比例表示的接触点。</returns>
        public static float PeakFraction(float[] handSpeeds)
        {
            if (handSpeeds == null || handSpeeds.Length < 5)
            {
                return DefaultContact;
            }

            int last = handSpeeds.Length - 1;
            int best = -1;
            float fastest = float.MinValue;

            for (int i = 0; i <= last; i++)
            {
                float fraction = i / (float)last;

                if (fraction < EarliestContact || fraction > LatestContact)
                {
                    continue;
                }

                if (handSpeeds[i] > fastest)
                {
                    fastest = handSpeeds[i];
                    best = i;
                }
            }

            return best < 0 ? DefaultContact : best / (float)last;
        }
    }
}
