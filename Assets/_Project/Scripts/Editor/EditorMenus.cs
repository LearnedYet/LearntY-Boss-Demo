namespace AdaptiveBossArena.Editor
{
    /// <summary>项目编辑器工具共用的菜单路径和资产位置。</summary>
    /// <remarks>
    /// 集中管理必须顺序执行的设置命令优先级，仅定义一次，并在新增工具时保持一致。
    /// </remarks>
    internal static class EditorMenus
    {
        /// <summary>所有项目工具所在的根菜单。</summary>
        public const string Root = "Adaptive Boss Arena/";

        /// <summary>一次性项目设置命令的子菜单。</summary>
        public const string Setup = Root + "Setup/";

        /// <summary>验证命令的子菜单。</summary>
        public const string Validate = Root + "Validate/";

        /// <summary>首个设置命令优先级，用于确定执行顺序。</summary>
        public const int SetupPriorityConfigureProject = 0;

        /// <summary>资产生成命令的优先级。</summary>
        public const int SetupPriorityGenerateAssets = 1;

        /// <summary>场景构建命令的优先级。</summary>
        public const int SetupPriorityBuildScene = 2;

        /// <summary>组合执行全部步骤命令的优先级。</summary>
        public const int SetupPriorityRunAll = 20;

        /// <summary>写入生成配置资产的文件夹。</summary>
        public const string GeneratedAssetFolder = "Assets/_Project/ScriptableObjects";

        /// <summary>写入生成材质的文件夹。</summary>
        public const string GeneratedMaterialFolder = "Assets/_Project/Materials";

        /// <summary>写入生成场景的文件夹。</summary>
        public const string GeneratedSceneFolder = "Assets/_Project/Scenes";

        /// <summary>构建器生成的竞技场场景路径。</summary>
        public const string ArenaScenePath = GeneratedSceneFolder + "/Arena.unity";

        /// <summary>构建器生成的标题场景路径。</summary>
        public const string MainMenuScenePath = GeneratedSceneFolder + "/MainMenu.unity";
    }
}
