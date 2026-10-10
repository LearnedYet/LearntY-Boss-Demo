using UnityEngine;

namespace AdaptiveBossArena.Utilities
{
    /// <summary>
    /// 玩法系统共用的小型数值工具。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这里不依赖<c>Time.deltaTime</c>等引擎状态，可在编辑模式直接测试。
    /// </para>
    /// <para>
    /// 刻意不放入嵌套<c>Math</c>命名空间，否则<c>AdaptiveBossArena.Utilities</c>内的<c>Math.Pow</c>
    /// 会解析到该命名空间，而非<see cref="System.Math"/>，产生误导性编译错误。
    /// 下方仍完整写出<c>System.Math</c>作进一步防护。
    /// </para>
    /// </remarks>
    public static class MathUtil
    {
        /// <summary>将值从一个范围线性映射到另一个范围，不钳制。</summary>
        /// <param name="value">输入值。</param>
        /// <param name="fromMin">输入范围下限。</param>
        /// <param name="fromMax">输入范围上限。</param>
        /// <param name="toMin">输出范围下限。</param>
        /// <param name="toMax">输出范围上限。</param>
        /// <returns>映射后的值；输入范围退化时返回<paramref name="toMin"/>。</returns>
        public static float Remap(float value, float fromMin, float fromMax, float toMin, float toMax)
        {
            float span = fromMax - fromMin;
            if (Mathf.Abs(span) < Mathf.Epsilon)
            {
                return toMin;
            }

            return toMin + (value - fromMin) / span * (toMax - toMin);
        }

        /// <summary>映射到零至一并钳制。</summary>
        /// <param name="value">输入值。</param>
        /// <param name="fromMin">映射为零的值。</param>
        /// <param name="fromMax">映射为一的值。</param>
        /// <returns>归一化并钳制后的值。</returns>
        public static float Remap01(float value, float fromMin, float fromMax) =>
            Mathf.Clamp01(Remap(value, fromMin, fromMax, 0f, 1f));

        /// <summary>
        /// 以不依赖帧率的指数平滑接近目标。
        /// </summary>
        /// <remarks>
        /// 优于<c>Mathf.Lerp(current, target, someRate * Time.deltaTime)</c>，后者会随帧率改变行为。
        /// 适应参数使用此方法，使Boss策略在60和144fps下变化一致。
        /// </remarks>
        /// <param name="current">当前值。</param>
        /// <param name="target">目标值。</param>
        /// <param name="halfLifeSeconds">消除剩余距离一半所需时间；非正数则直接到目标。</param>
        /// <param name="deltaTimeSeconds">本步经过时间。</param>
        /// <returns>平滑后的值。</returns>
        public static float Damp(float current, float target, float halfLifeSeconds, float deltaTimeSeconds)
        {
            if (halfLifeSeconds <= 0f)
            {
                return target;
            }

            float retained = (float)System.Math.Pow(0.5d, deltaTimeSeconds / halfLifeSeconds);
            return target + (current - target) * retained;
        }

        /// <summary>对向量进行不依赖帧率的指数平滑。</summary>
        /// <param name="current">当前值。</param>
        /// <param name="target">目标值。</param>
        /// <param name="halfLifeSeconds">消除剩余距离一半所需时间。</param>
        /// <param name="deltaTimeSeconds">本步经过时间。</param>
        /// <returns>平滑后的向量。</returns>
        public static Vector3 Damp(Vector3 current, Vector3 target, float halfLifeSeconds, float deltaTimeSeconds)
        {
            if (halfLifeSeconds <= 0f)
            {
                return target;
            }

            float retained = (float)System.Math.Pow(0.5d, deltaTimeSeconds / halfLifeSeconds);
            return target + (current - target) * retained;
        }

        /// <summary>
        /// 将观察样本数转换为零至一的置信权重。
        /// </summary>
        /// <remarks>
        /// 学习系统不能只凭单个数据点行动。每个行为特征都使用此曲线计算置信度，反制权重再乘以置信度，
        /// 让两次观察只造成轻微影响，三十次观察才能驱动明显反制。
        /// </remarks>
        /// <param name="sampleCount">累计观察数。</param>
        /// <param name="halfConfidenceSampleCount">置信度达到一半时的样本数，必须为正数。</param>
        /// <returns>零至一范围的置信度。</returns>
        public static float ConfidenceFromSampleCount(int sampleCount, int halfConfidenceSampleCount)
        {
            if (sampleCount <= 0)
            {
                return 0f;
            }

            if (halfConfidenceSampleCount <= 0)
            {
                return 1f;
            }

            double retained = System.Math.Pow(0.5d, (double)sampleCount / halfConfidenceSampleCount);
            return (float)(1d - retained);
        }

        /// <summary>将世界向量投影到竞技场水平面。</summary>
        /// <param name="worldVector">世界空间向量。</param>
        /// <returns>作为二维向量的x和z分量。</returns>
        public static Vector2 FlattenToPlane(Vector3 worldVector) => new Vector2(worldVector.x, worldVector.z);

        /// <summary>将水平面向量提升回指定高度的世界空间。</summary>
        /// <param name="planeVector">水平面向量。</param>
        /// <param name="height">世界空间y分量。</param>
        /// <returns>对应的世界向量。</returns>
        public static Vector3 ToWorld(Vector2 planeVector, float height = 0f) =>
            new Vector3(planeVector.x, height, planeVector.y);

        /// <summary>忽略高度的两个世界位置水平距离平方。</summary>
        /// <param name="a">第一个位置。</param>
        /// <param name="b">第二个位置。</param>
        /// <returns>水平面距离平方。</returns>
        public static float SqrPlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary>忽略高度的两个世界位置水平距离。</summary>
        /// <param name="a">第一个位置。</param>
        /// <param name="b">第二个位置。</param>
        /// <returns>水平面距离。</returns>
        public static float PlanarDistance(Vector3 a, Vector3 b) => Mathf.Sqrt(SqrPlanarDistance(a, b));

        /// <summary>归一化向量，退化时返回<see cref="Vector2.zero"/>。</summary>
        /// <param name="value">要归一化的向量。</param>
        /// <returns>单位向量或零。</returns>
        public static Vector2 SafeNormalize(Vector2 value)
        {
            float sqrMagnitude = value.sqrMagnitude;
            return sqrMagnitude < Mathf.Epsilon ? Vector2.zero : value / Mathf.Sqrt(sqrMagnitude);
        }

        /// <summary>归一化向量，退化时返回<see cref="Vector3.zero"/>。</summary>
        /// <param name="value">要归一化的向量。</param>
        /// <returns>单位向量或零。</returns>
        public static Vector3 SafeNormalize(Vector3 value)
        {
            float sqrMagnitude = value.sqrMagnitude;
            return sqrMagnitude < Mathf.Epsilon ? Vector3.zero : value / Mathf.Sqrt(sqrMagnitude);
        }
    }
}
