namespace AdaptiveBossArena.Core.Services
{
    /// <summary>玩家可见的画质等级，各平台均为三档。</summary>
    /// <remarks>
    /// 各档具体含义因平台不同：浏览器的 High 低于 Windows 构建版，但玩家在当前机器上始终只选择 Low、
    /// Medium 和 High 三档。
    /// </remarks>
    public enum GraphicsQuality
    {
        /// <summary>面向集成显卡：减少特效，降低阴影成本，使用最轻的后处理。</summary>
        Low = 0,

        /// <summary>完整场景，带柔和阴影和完整调色。</summary>
        Medium = 1,

        /// <summary>启用平台渲染器提供的全部效果。</summary>
        High = 2
    }
}
