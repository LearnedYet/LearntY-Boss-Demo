namespace AdaptiveBossArena.Core.Constants
{
    /// <summary>项目依赖的场景对象标签。</summary>
    /// <remarks>
    /// 仅在场景标记确实最清晰时少量使用标签。战斗期间查询的对象都通过初始化时取得的接口引用解析，
    /// 绝不每帧搜索标签。
    /// </remarks>
    public static class TagNames
    {
        /// <summary>玩家角色根对象。</summary>
        public const string Player = "Player";

        /// <summary>Boss 根对象。</summary>
        public const string Boss = "Boss";

        /// <summary>战斗重启时参与者重生位置的标记。</summary>
        public const string ArenaSpawnPoint = "ArenaSpawnPoint";

        /// <summary>项目配置器创建的全部自定义标签。</summary>
        public static readonly string[] All =
        {
            Boss,
            ArenaSpawnPoint
        };
    }

    /// <summary>持久化记录的标识键。</summary>
    /// <remarks>
    /// 采用集中定义，因为存档键拼写错误不会报错：写入成功，读取却找不到数据，
    /// 游戏每次启动都悄悄回退到默认值。
    /// </remarks>
    public static class SaveKeys
    {
        /// <summary>音频、视频、输入和无障碍设置。</summary>
        public const string Settings = "settings";

        /// <summary>对战 Boss 的最佳结果。</summary>
        public const string Records = "records";

        /// <summary>本次挑战选择的修正项。</summary>
        public const string RunModifiers = "run-modifiers";
    }
}
