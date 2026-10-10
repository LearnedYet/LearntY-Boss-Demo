using System;
using UnityEngine;

namespace AdaptiveBossArena.Core.Services
{
    /// <summary>
    /// 确定性的 xorshift128 随机源。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 在项目内实现，而非委托给 <see cref="System.Random"/>，因为后者没有固定算法契约，
    /// 且曾随 .NET 运行时变化。错误报告中的种子必须在任意机器和 Unity版本复现同一战斗，
    /// 因此需要由我们控制算法。
    /// </para>
    /// <para>
    /// Xorshift128 速度快、无分配，周期远长于任何战斗可能消耗的范围。它不具密码学安全性，
    /// 绝不能当作安全随机源使用。
    /// </para>
    /// </remarks>
    public sealed class XorShiftRandomProvider : IRandomProvider
    {
        /// <summary>调用者提供零种子时使用替代值，因为 xorshift 无法从零状态退出。</summary>
        private const uint FallbackSeed = 0x9E3779B9u;

        /// <summary>2^32 的倒数，用于将完整范围的 uint 转为零到一区间。</summary>
        private const double UintToUnitScale = 1.0d / 4294967296.0d;

        private uint _seed;
        private uint _x;
        private uint _y;
        private uint _z;
        private uint _w;

        /// <summary>通过显式种子创建生成器。</summary>
        /// <param name="seed">初始种子。零映射为固定的非零常量。</param>
        public XorShiftRandomProvider(uint seed) => Reseed(seed);

        /// <summary>通过系统时钟种子创建生成器。</summary>
        public XorShiftRandomProvider() => Reseed(unchecked((uint)Environment.TickCount));

        /// <inheritdoc />
        public uint Seed => _seed;

        /// <inheritdoc />
        public void Reseed(uint seed)
        {
            _seed = seed == 0u ? FallbackSeed : seed;

            // 使种子生成的四路状态去相关，避免相邻种子产生明显相似的开场序列。
            _x = _seed;
            _y = _seed ^ 0x6C078965u;
            _z = _seed ^ 0x8F1BBCDCu;
            _w = _seed ^ 0xB504F333u;

            // 丢弃初始值，因为播种后的状态混合不足。
            for (int i = 0; i < WarmUpIterations; i++)
            {
                NextUInt();
            }
        }

        /// <summary>播种后为充分混合状态而丢弃的值数。</summary>
        private const int WarmUpIterations = 16;

        /// <inheritdoc />
        public float NextFloat01() => (float)(NextUInt() * UintToUnitScale);

        /// <inheritdoc />
        public float NextFloat(float min, float max) => min + NextFloat01() * (max - min);

        /// <inheritdoc />
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                return minInclusive;
            }

            uint range = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % range);
        }

        /// <inheritdoc />
        public bool NextBool(float probability = 0.5f)
        {
            if (probability <= 0f)
            {
                return false;
            }

            return probability >= 1f || NextFloat01() < probability;
        }

        /// <inheritdoc />
        public Vector2 NextDirectionOnPlane()
        {
            float angle = NextFloat01() * 2f * Mathf.PI;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        /// <summary>推进生成器并返回下一个原始值。</summary>
        private uint NextUInt()
        {
            uint t = _x ^ (_x << 11);
            _x = _y;
            _y = _z;
            _z = _w;
            _w = _w ^ (_w >> 19) ^ t ^ (t >> 8);
            return _w;
        }
    }
}
