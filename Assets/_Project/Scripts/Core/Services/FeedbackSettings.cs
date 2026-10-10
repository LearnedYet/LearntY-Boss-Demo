namespace AdaptiveBossArena.Core.Services
{
    /// <summary>
    /// 任何系统都可读取的全局表现偏好，无需引用菜单。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是一小块刻意保留的全局状态。这里的无障碍设置确实作用于全局：
    /// 减少闪烁必须影响场景中每个受击闪光，而没有对象统一拥有它们。
    /// 为此采用事件通道或每对象引用会过度复杂；在少数热路径中读取两个标志则不会。
    /// </para>
    /// <para>
    /// 位于 Core，使写入的设置菜单与读取的战斗反馈都能访问，而不互相依赖。菜单在加载时写入，
    /// 因此域重载清回默认值也无妨：下一次战斗会重新应用存档值。
    /// </para>
    /// </remarks>
    public static class FeedbackSettings
    {
        /// <summary>
        /// 为 true 时抑制受击闪光。
        /// </summary>
        /// <remarks>
        /// 快速全屏闪烁有光敏风险，必须能在一个所有闪光效果都遵守的位置关闭。
        /// </remarks>
        public static bool ReducedFlashing { get; set; }

        /// <summary>
        /// 适应提示在屏幕上停留时间的倍率。
        /// </summary>
        /// <remarks>
        /// 需要更多阅读时间的玩家可设为大于一。默认为一，主动修改前此设置不影响表现。
        /// </remarks>
        public static float TellHoldMultiplier { get; set; } = 1f;
    }
}
