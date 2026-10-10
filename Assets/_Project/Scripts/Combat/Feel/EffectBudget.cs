using UnityEngine;

namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 战斗效果可使用的预算，浏览器中较少。
    /// </summary>
    /// <remarks>
    /// 浏览器构建运行于集成显卡笔记本，每个活动粒子系统和每个逐像素灯光都与绘制战斗共用同一块较弱
    /// GPU。池只在构建时读取一次，不会在每次命中时检查平台。
    /// </remarks>
    public static class EffectBudget
    {
        /// <summary>游戏是否作为 WebGL 播放器运行。</summary>
        public static bool IsWebPlayer => Application.platform == RuntimePlatform.WebGLPlayer;

        /// <summary>
        /// 效果是否缩减到 Low 预算。
        /// </summary>
        /// <remarks>
        /// 遵循玩家图形质量而不是平台：使用独立显卡的浏览器可以承载全部效果，
        /// 之前只是因为最初调参的机器无法承载而被缩减。
        /// </remarks>
        public static bool IsReduced => Core.Services.GraphicsTier.Current == Core.Services.GraphicsQuality.Low;

        /// <summary>浏览器中同时存活的命中爆发数量，足够连击与交锋，不追求烟花效果。</summary>
        public const int WebImpactBursts = 6;
    }
}
