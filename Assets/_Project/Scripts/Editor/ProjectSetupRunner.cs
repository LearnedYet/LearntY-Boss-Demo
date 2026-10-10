using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 按步骤依赖顺序运行完整首次设置。
    /// </summary>
    /// <remarks>
    /// 三个设置命令必须顺序运行：场景需要配置资产，资产需要项目层。提供单一命令避免乱序，
    /// 让对象误用默认层而碰撞异常。
    /// </remarks>
    public static class ProjectSetupRunner
    {
        /// <summary>依次运行项目配置、资产生成和场景构建。</summary>
        [MenuItem(EditorMenus.Setup + "Run Full Setup", priority = EditorMenus.SetupPriorityRunAll)]
        public static void RunFullSetup()
        {
            // 构建机器无法回答模态对话框，因此批处理直接继续。命令行调用者已作出对话框所询问的决定。
            if (!Application.isBatchMode)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "Adaptive Boss Arena Setup",
                    "This will configure project layers and physics, generate configuration assets, " +
                    "input bindings and both character prefabs, then build the arena scene.\n\n" +
                    "Existing assets are kept. The current scene will be replaced.",
                    "Run Setup",
                    "Cancel");

                if (!proceed)
                {
                    return;
                }
            }

            // 顺序是严格依赖链：资产需要层，预制体需要配置与输入绑定，场景需要预制体。
            ProjectConfigurator.ConfigureProject();

            // 必须早于材质创建：未配置管线时着色器查找回退内置渲染器，所有生成表面变品红。
            RenderPipelineConfigurator.ConfigureRenderPipeline();
            PostProcessingConfigurator.ConfigurePostProcessing();

            DefaultAssetGenerator.GenerateDefaultAssets();
            InputActionsGenerator.GenerateInputActions();
            PlayerPrefabBuilder.GeneratePlayerPrefab();
            BossPrefabBuilder.GenerateBossPrefab();

            // 在预制体之后：范围需在巨兽真实身体及砍刀上测量。
            Art.AttackReachMeasure.MeasureBossReach();

            ArenaSceneBuilder.BuildArenaScene();

            // 最后构建，使明确写入的标题先、竞技场后顺序最终生效，而非将标题追加到竞技场后。
            MainMenuSceneBuilder.BuildMainMenuScene();

            IReadOnlyList<string> violations = ArchitectureValidator.FindViolations();

            foreach (string violation in violations)
            {
                Debug.LogError($"[Adaptive Boss Arena] Architecture violation: {violation}");
            }

            // 作为设置的一部分运行，因为悬空攻击引用会让游戏启动、编译、测试通过，却完全不可玩。
            IReadOnlyList<string> broken = AssetIntegrityValidator.FindBrokenReferences();

            foreach (string problem in broken)
            {
                Debug.LogError($"[Adaptive Boss Arena] Broken reference: {problem}");
            }

            if (broken.Count == 0 && violations.Count == 0)
            {
                Debug.Log("[Adaptive Boss Arena] Setup complete. Open the Arena scene and press Play.");
            }
        }
    }
}
