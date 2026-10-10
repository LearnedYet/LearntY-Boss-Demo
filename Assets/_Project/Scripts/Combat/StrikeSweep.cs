using UnityEngine;

namespace AdaptiveBossArena.Combat
{
    /// <summary>
    /// 在两帧之间检测打击部位，避免快速挥击未被察觉地穿过身体。
    /// </summary>
    /// <remarks>
    /// 巨剑尖端在一次挥击的一帧中能移动超过一米。只检测最终位置会让它在两帧之间完整越过骑士；
    /// 检测全路径则需要物理引擎没有提供的胶囊扫掠体积查询。沿路径以不超过部位厚度的步长采样，
    /// 只需每帧少量重叠查询便能找出相同接触。
    /// </remarks>
    public static class StrikeSweep
    {
        /// <summary>
        /// 覆盖两姿态间路径所需的采样数。
        /// </summary>
        /// <param name="previousStart">上帧根部位置。</param>
        /// <param name="previousEnd">上帧尖端位置。</param>
        /// <param name="start">当前根部位置。</param>
        /// <param name="end">当前尖端位置。</param>
        /// <param name="radius">部位厚度。</param>
        /// <param name="maximum">采样上限，避免瞬移产生上百次查询。</param>
        /// <returns>至少一次采样；移动较快端距离越远，采样越多。</returns>
        public static int SampleCount(
            Vector3 previousStart, Vector3 previousEnd, Vector3 start, Vector3 end, float radius, int maximum)
        {
            float travel = Mathf.Max(Vector3.Distance(previousStart, start), Vector3.Distance(previousEnd, end));
            int samples = Mathf.CeilToInt(travel / Mathf.Max(0.01f, radius));

            return Mathf.Clamp(samples, 1, Mathf.Max(1, maximum));
        }

        /// <summary>两帧之间某个时刻的部位姿态。</summary>
        /// <param name="previousStart">上帧根部位置。</param>
        /// <param name="previousEnd">上帧尖端位置。</param>
        /// <param name="start">当前根部位置。</param>
        /// <param name="end">当前尖端位置。</param>
        /// <param name="t">零表示上帧姿态，一表示本帧姿态。</param>
        /// <param name="sampleStart">该时刻的根部位置。</param>
        /// <param name="sampleEnd">该时刻的尖端位置。</param>
        public static void Between(
            Vector3 previousStart, Vector3 previousEnd, Vector3 start, Vector3 end, float t,
            out Vector3 sampleStart, out Vector3 sampleEnd)
        {
            t = Mathf.Clamp01(t);
            sampleStart = Vector3.Lerp(previousStart, start, t);
            sampleEnd = Vector3.Lerp(previousEnd, end, t);
        }
    }
}
