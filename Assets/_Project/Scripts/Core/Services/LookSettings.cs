namespace AdaptiveBossArena.Core.Services
{
    /// <summary>
    /// 玩家自己的相机设置：视野转动的速度和方向。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="FeedbackSettings"/>一样位于 Core：设置菜单写入，相机读取，双方不能互相依赖。
    /// 菜单在加载时应用存档值，因此域重载恢复默认值不会造成问题。
    /// </remarks>
    public static class LookSettings
    {
        /// <summary>新存档的初始灵敏度。</summary>
        public const float DefaultSensitivity = 1f;

        /// <summary>菜单提供的最低灵敏度。</summary>
        public const float MinimumSensitivity = 0.25f;

        /// <summary>菜单提供的最高灵敏度。</summary>
        public const float MaximumSensitivity = 2.5f;

        /// <summary>每单位鼠标或摇杆移动对应的视野转动倍率。</summary>
        public static float Sensitivity { get; set; } = DefaultSensitivity;

        /// <summary>鼠标向上移动时是否向下看。</summary>
        public static bool InvertY { get; set; }
    }
}
