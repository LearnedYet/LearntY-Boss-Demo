using UnityEngine;

namespace AdaptiveBossArena.Core.Services
{
    /// <summary>
    /// 所有涉及概率的玩法决策使用的可设种子随机源。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 项目中任何对象都不能直接调用 <c>UnityEngine.Random</c>。Boss 反制策略刻意采用概率选择，
    /// 避免相同玩家行为产生可被记住的固定响应。若随机性不可控制，这一特点也会让 AI 无法测试。
    /// </para>
    /// <para>
    /// 注入此接口可让测试固定种子，断言指定行为画像产生指定适应，并让错误报告携带能复现战斗的种子。
    /// </para>
    /// </remarks>
    public interface IRandomProvider
    {
        /// <summary>序列开始时使用的种子。</summary>
        uint Seed { get; }

        /// <summary>返回零到一的左闭右开区间内的值。</summary>
        /// <returns>均匀分布的值。</returns>
        float NextFloat01();

        /// <summary>返回 <paramref name="min"/> 到 <paramref name="max"/> 的左闭右开区间内的值。</summary>
        /// <param name="min">包含的下界。</param>
        /// <param name="max">不包含的上界。</param>
        /// <returns>均匀分布的值。</returns>
        float NextFloat(float min, float max);

        /// <summary>返回 <paramref name="minInclusive"/> 到 <paramref name="maxExclusive"/> 的左闭右开区间内的整数。</summary>
        /// <param name="minInclusive">包含的下界。</param>
        /// <param name="maxExclusive">不包含的上界。</param>
        /// <returns>均匀分布的整数。</returns>
        int NextInt(int minInclusive, int maxExclusive);

        /// <summary>按所提供概率返回 true。</summary>
        /// <param name="probability">返回 true 的概率，范围零到一。</param>
        /// <returns>抽取的结果。</returns>
        bool NextBool(float probability = 0.5f);

        /// <summary>返回竞技场水平面上均匀分布的单位向量。</summary>
        /// <returns>单位方向。</returns>
        Vector2 NextDirectionOnPlane();

        /// <summary>用新种子重新开始序列。</summary>
        /// <param name="seed">重新开始使用的种子。零会被映射，因为它是退化状态。</param>
        void Reseed(uint seed);
    }
}
