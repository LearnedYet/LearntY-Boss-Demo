using System;

namespace AdaptiveBossArena.Utilities.Statistics
{
    /// <summary>
    /// 收集帧时间，报告性能预算使用的百分位数。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 平均值会掩盖玩家感受到的卡顿：多数帧4 ms、砸地时40 ms，平均看似正常却仍卡顿。预算使用第95百分位，
    /// 中位数表示普通帧成本。
    /// </para>
    /// <para>
    /// 样本保存在一次分配的固定数组中，避免测量本身给被测帧增加GC峰值。
    /// </para>
    /// </remarks>
    public sealed class FrameTimeSampler
    {
        private readonly float[] _samples;
        private float[] _sortBuffer;
        private int _count;

        /// <summary>创建最多保存固定帧数的采样器。</summary>
        /// <param name="capacity">
        /// 最大样本数，超出时丢弃新帧而不覆盖旧帧，让采样始终描述窗口开头。
        /// </param>
        public FrameTimeSampler(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
            }

            _samples = new float[capacity];
        }

        /// <summary>已采集样本数。</summary>
        public int Count => _count;

        /// <summary>采样器是否已无剩余容量。</summary>
        public bool IsFull => _count >= _samples.Length;

        /// <summary>记录一帧耗时。</summary>
        /// <param name="seconds">帧耗时，单位为秒；忽略非正值。</param>
        public void Add(float seconds)
        {
            if (seconds <= 0f || IsFull)
            {
                return;
            }

            _samples[_count++] = seconds;
        }

        /// <summary>丢弃全部样本。</summary>
        public void Clear() => _count = 0;

        /// <summary>
        /// 指定比例样本低于的帧时间，单位为毫秒。
        /// </summary>
        /// <remarks>
        /// 采用最近秩法，始终返回实际发生过的帧时间，不在秩之间插值生成未出现的p95。
        /// </remarks>
        /// <param name="fraction">零到一的比例，0.95表示第95百分位。</param>
        /// <returns>百分位帧时间，单位为毫秒；没有样本时为零。</returns>
        public float PercentileMilliseconds(float fraction)
        {
            if (_count == 0)
            {
                return 0f;
            }

            if (_sortBuffer == null || _sortBuffer.Length < _samples.Length)
            {
                _sortBuffer = new float[_samples.Length];
            }

            Array.Copy(_samples, _sortBuffer, _count);
            Array.Sort(_sortBuffer, 0, _count);

            float clamped = Math.Max(0f, Math.Min(1f, fraction));
            int rank = (int)Math.Ceiling(clamped * _count) - 1;
            rank = Math.Max(0, Math.Min(_count - 1, rank));

            return _sortBuffer[rank] * 1000f;
        }

        /// <summary>记录的最长帧时间，单位为毫秒。</summary>
        public float MaxMilliseconds => PercentileMilliseconds(1f);
    }
}
