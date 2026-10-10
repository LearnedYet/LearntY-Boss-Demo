using UnityEngine;
using UnityEngine.Rendering;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 将阶段氛围映射到按开场阶段颜色烘焙的光照中。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 光照探针在冷色开场氛围下仅烘焙一次。探针照亮的战斗角色和石材从探针而非房间环境色取得环境光，
    /// 因此若不处理，竞技场转红时反而无法影响视线最关注的对象。
    /// </para>
    /// <para>
    /// 依据当前氛围相对于烘焙颜色的变化比例缩放各颜色通道，既保留反弹光的空间分布——受日照地面较亮、
    /// 角落较暗——又融入当前阶段色相。保持为纯逻辑，让运算可以测试。
    /// </para>
    /// </remarks>
    public static class ProbeTint
    {
        /// <summary>增益上限，防止接近黑色的烘焙通道被放大成刺眼亮光。</summary>
        public const float MaximumGain = 4f;

        /// <summary>从烘焙时颜色映射到当前目标颜色的逐通道增益。</summary>
        /// <param name="current">当前氛围颜色。</param>
        /// <param name="baked">探针烘焙时的氛围颜色。</param>
        /// <returns>每通道增益，范围为零至 <see cref="MaximumGain"/>。</returns>
        public static Color Gain(Color current, Color baked) => new Color(
            Channel(current.r, baked.r),
            Channel(current.g, baked.g),
            Channel(current.b, baked.b),
            1f);

        /// <summary>
        /// 将烘焙探针按增益缩放并维持房间环境光下限后，写入目标数组。
        /// </summary>
        /// <remarks>
        /// 亮度下限与染色同样重要。探针烘焙的是穿过废墟后的天空光，
        /// 墙边光照远少于烘焙前使用的无遮挡环境光；实测墙边探针暗了两倍半。
        /// 角色走到墙边不能仅因进行了烘焙就变暗，所以每个通道的基础项至少保持环境光水平。
        /// 日照地面反弹更强光照的位置，仍可体现烘焙结果。
        /// </remarks>
        /// <param name="baked">烘焙后的探针，绝不修改。</param>
        /// <param name="destination">接收染色后的探针。</param>
        /// <param name="gain">逐通道增益。</param>
        /// <param name="floor">房间环境光，任何探针都不能低于它。</param>
        public static void Apply(
            SphericalHarmonicsL2[] baked, SphericalHarmonicsL2[] destination, Color gain, SphericalHarmonicsL2 floor)
        {
            for (int probe = 0; probe < baked.Length && probe < destination.Length; probe++)
            {
                // 这是结构体副本，因此绝不会写入烘焙数组。
                SphericalHarmonicsL2 tinted = baked[probe];

                for (int coefficient = 0; coefficient < 9; coefficient++)
                {
                    tinted[0, coefficient] *= gain.r;
                    tinted[1, coefficient] *= gain.g;
                    tinted[2, coefficient] *= gain.b;
                }

                for (int channel = 0; channel < 3; channel++)
                {
                    tinted[channel, 0] = Mathf.Max(tinted[channel, 0], floor[channel, 0]);
                }

                destination[probe] = tinted;
            }
        }

        private static float Channel(float current, float baked) =>
            Mathf.Clamp(current / Mathf.Max(0.001f, baked), 0f, MaximumGain);
    }
}
