using System;
using UnityEngine;

namespace AdaptiveBossArena.Utilities.Statistics
{
    /// <summary>
    /// 将移动方向分入八个罗盘扇区的加权直方图。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用于实现玩家常向左翻滚时Boss能够预测左侧的设计。每次冲刺写入扇区，旧权重按半衰期衰减，
    /// <see cref="Predictability"/>表示分布偏向程度。
    /// </para>
    /// <para>
    /// 可预测性约束反制是否合理。只有玩家确有偏好方向，Boss才应向该方向提前挥击；
    /// 接近均匀分布时可预测性接近零，依赖它的反制不应触发。
    /// </para>
    /// <para>
    /// 输入方向是水平面的(x,z)。第零扇区指向正X，之后每45度逆时针递增。
    /// </para>
    /// </remarks>
    public sealed class DirectionHistogram
    {
        /// <summary>跟踪的方向扇区数。</summary>
        public const int BinCount = 8;

        private const float RadiansPerBin = 2f * Mathf.PI / BinCount;

        /// <summary>总权重低于此值时视为没有有效信号。</summary>
        private const float MinimumMeaningfulWeight = 0.0001f;

        /// <summary>完全均匀分布的熵，用于归一化可预测性。</summary>
        private static readonly float MaxEntropy = Mathf.Log(BinCount);

        private readonly float[] _weights = new float[BinCount];
        private float _totalWeight;

        /// <summary>衰减后的所有扇区权重总和。</summary>
        public float TotalWeight => _totalWeight;

        /// <summary>积累足够权重、分布具有意义时为true。</summary>
        public bool HasSignal => _totalWeight > MinimumMeaningfulWeight;

        /// <summary>
        /// 记录移动方向。
        /// </summary>
        /// <param name="direction">水平面方向，忽略长度，丢弃接近零的向量。</param>
        /// <param name="weight">本次观察的相对重要程度，必须为正数。</param>
        public void Add(Vector2 direction, float weight = 1f)
        {
            if (weight <= 0f || direction.sqrMagnitude < Mathf.Epsilon)
            {
                return;
            }

            _weights[BinForDirection(direction)] += weight;
            _totalWeight += weight;
        }

        /// <summary>
        /// 将各扇区权重向零衰减，让过时移动习惯不再影响Boss。
        /// </summary>
        /// <param name="halfLifeSeconds">累计权重减半的时间，必须为正数。</param>
        /// <param name="deltaTimeSeconds">距上次衰减经过的时间。</param>
        public void Decay(float halfLifeSeconds, float deltaTimeSeconds)
        {
            if (halfLifeSeconds <= 0f || deltaTimeSeconds <= 0f || _totalWeight <= 0f)
            {
                return;
            }

            float retained = (float)Math.Pow(0.5d, deltaTimeSeconds / halfLifeSeconds);

            for (int i = 0; i < BinCount; i++)
            {
                _weights[i] *= retained;
            }

            _totalWeight *= retained;
        }

        /// <summary><paramref name="bin"/>占总权重的比例，范围零到一。</summary>
        /// <param name="bin">扇区索引，范围[0,<see cref="BinCount"/>)。</param>
        /// <returns>归一化权重，没有信号时为零。</returns>
        /// <exception cref="ArgumentOutOfRangeException">扇区索引越界时抛出。</exception>
        public float NormalizedWeight(int bin)
        {
            if ((uint)bin >= BinCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bin), bin, $"Bin must be in the range [0, {BinCount}).");
            }

            return HasSignal ? _weights[bin] / _totalWeight : 0f;
        }

        /// <summary>观察最多的扇区索引，没有信号时为-1。</summary>
        public int DominantBin
        {
            get
            {
                if (!HasSignal)
                {
                    return -1;
                }

                int best = 0;
                for (int i = 1; i < BinCount; i++)
                {
                    if (_weights[i] > _weights[best])
                    {
                        best = i;
                    }
                }

                return best;
            }
        }

        /// <summary>
        /// 分布偏向程度，零为完全均匀，一为全部集中在单个扇区。
        /// </summary>
        /// <remarks>
        /// 通过归一化后的逆Shannon熵计算。利用移动习惯的反制应在它超过阈值后才启用。
        /// </remarks>
        public float Predictability
        {
            get
            {
                if (!HasSignal)
                {
                    return 0f;
                }

                float entropy = 0f;
                for (int i = 0; i < BinCount; i++)
                {
                    float p = _weights[i] / _totalWeight;
                    if (p > 0f)
                    {
                        entropy -= p * Mathf.Log(p);
                    }
                }

                return Mathf.Clamp01(1f - entropy / MaxEntropy);
            }
        }

        /// <summary>指向指定扇区中心的单位向量。</summary>
        /// <param name="bin">扇区索引，范围[0,<see cref="BinCount"/>)。</param>
        /// <returns>水平面单位方向。</returns>
        /// <exception cref="ArgumentOutOfRangeException">扇区索引越界时抛出。</exception>
        public static Vector2 BinCenterDirection(int bin)
        {
            if ((uint)bin >= BinCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bin), bin, $"Bin must be in the range [0, {BinCount}).");
            }

            float angle = bin * RadiansPerBin;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        /// <summary>将方向映射到罗盘扇区。</summary>
        /// <param name="direction">水平面方向。</param>
        /// <returns>扇区索引，范围[0,<see cref="BinCount"/>)。</returns>
        public static int BinForDirection(Vector2 direction)
        {
            float angle = Mathf.Atan2(direction.y, direction.x);
            int bin = Mathf.RoundToInt(angle / RadiansPerBin);

            // Atan2返回有符号角，接缝处四舍五入可能达到BinCount，因此需要环绕。
            return ((bin % BinCount) + BinCount) % BinCount;
        }

        /// <summary>清除所有累计权重。</summary>
        public void Reset()
        {
            Array.Clear(_weights, 0, BinCount);
            _totalWeight = 0f;
        }
    }
}
