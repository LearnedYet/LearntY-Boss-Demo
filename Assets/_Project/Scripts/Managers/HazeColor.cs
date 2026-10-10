using UnityEngine;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 大教堂空气的颜色：来自房间自身光照，并始终保持最低亮度。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 此前尝试过雾，因“近处交战者之外陷入黑暗”而移除。历史未记录原因，
    /// 但问题形态符合雾色取自暗淡的环境地面色，或保持默认黑色，从而让远处表面逐渐消失。薄雾应恰恰相反：
    /// 远处应趋近空气中的亮光。
    /// </para>
    /// <para>
    /// 所以颜色由房间光照构成：环境水平色加上一部分太阳光，再维持亮度下限。无论阶段配色怎样变化，
    /// 远处石材都会融入可见薄雾。计算保持为纯逻辑，让下限可被测试，而非寄希望于效果。
    /// </para>
    /// </remarks>
    public static class HazeColor
    {
        /// <summary>薄雾允许的最低相对亮度。</summary>
        public const float MinimumLuminance = 0.08f;

        /// <summary>太阳颜色散射进入薄雾的比例。</summary>
        private const float SunScatter = 0.12f;

        /// <summary>根据房间光照推导雾色。</summary>
        /// <param name="ambientEquator">环境水平颜色。</param>
        /// <param name="sunColour">方向光颜色。</param>
        /// <param name="sunIntensity">方向光强度。</param>
        /// <returns>亮度至少为 <see cref="MinimumLuminance"/> 的不透明颜色。</returns>
        public static Color Derive(Color ambientEquator, Color sunColour, float sunIntensity)
        {
            Color haze = ambientEquator + sunColour * (Mathf.Max(0f, sunIntensity) * SunScatter);
            haze.a = 1f;

            float luminance = Luminance(haze);

            if (luminance >= MinimumLuminance)
            {
                return haze;
            }

            // 沿自身色相提高亮度，使红色阶段仍保持红色薄雾；完全无光的颜色没有可保留的色相，
            // 因此变为处于亮度下限的中性灰。
            if (luminance <= 0.0001f)
            {
                return new Color(MinimumLuminance, MinimumLuminance, MinimumLuminance, 1f);
            }

            Color lifted = haze * (MinimumLuminance / luminance);
            lifted.a = 1f;

            return lifted;
        }

        /// <summary>线性颜色的相对亮度。</summary>
        /// <param name="colour">颜色。</param>
        /// <returns>从零开始的亮度。</returns>
        public static float Luminance(Color colour) =>
            0.2126f * colour.r + 0.7152f * colour.g + 0.0722f * colour.b;
    }
}
