using AdaptiveBossArena.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering.Universal;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 固定浏览器渲染等级与桌面构建之间的划分。
    /// </summary>
    /// <remarks>
    /// 此处最重要的属性无法从桌面观察：环境光遮蔽曾直接破坏 WebGL构建，而它此前通过所有测试，
    /// 且 Windows 版本完整战斗正常，直到浏览器加载时才崩溃。若桌面等级泄漏至 WebGL，就会重现该故障，
    /// 仍只有浏览器能发现。这里断言结构上不可能泄漏，而非期待它不会发生。
    /// </remarks>
    [TestFixture]
    public sealed class RenderTierTests
    {
        private static bool HasAmbientOcclusion(string rendererPath)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            Assert.IsNotNull(renderer, $"Renderer '{rendererPath}' is missing.");

            foreach (ScriptableRendererFeature feature in renderer.rendererFeatures)
            {
                if (feature is ScreenSpaceAmbientOcclusion)
                {
                    return true;
                }
            }

            return false;
        }

        [Test]
        public void BothTiersHaveAPipeline()
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                RenderPipelineConfigurator.PipelineAssetPath), "The web tier pipeline is missing.");

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                RenderPipelineConfigurator.DesktopPipelineAssetPath), "The desktop tier pipeline is missing.");
        }

        [Test]
        public void TheWebTierIsCheapEnoughForIntegratedGraphics()
        {
            // 后处理正常渲染后，网页等级仍只是缩小阴影贴图的桌面配置，在笔记本集成显卡的浏览器中卡顿。
            var web = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RenderPipelineConfigurator.PipelineAssetPath);
            var desktop = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RenderPipelineConfigurator.DesktopPipelineAssetPath);

            Assert.Less(web.shadowCascadeCount, desktop.shadowCascadeCount, "The web tier renders as many shadow cascades as desktop.");
            Assert.LessOrEqual(web.mainLightShadowmapResolution, 1024, "The web tier's shadow map is too large.");
            Assert.IsFalse(web.supportsSoftShadows, "The web tier filters its shadows softly.");
            Assert.IsFalse(web.supportsCameraDepthTexture, "The web tier copies depth every frame for nothing.");
            Assert.IsFalse(web.supportsAdditionalLightShadows, "The web tier lets point lights cast shadows.");
        }

        [Test]
        public void OnlyTheDesktopTierCarriesAmbientOcclusion()
        {
            Assert.IsFalse(
                HasAmbientOcclusion(RenderPipelineConfigurator.RendererAssetPath),
                "The web tier has ambient occlusion, which broke the WebGL build.");

            Assert.IsTrue(
                HasAmbientOcclusion(RenderPipelineConfigurator.DesktopRendererAssetPath),
                "The desktop tier lost its ambient occlusion.");
        }

        [Test]
        public void EveryDesktopLevelIsExcludedFromWebGLAndEveryWebLevelFromDesktop()
        {
            SerializedObject quality = RenderPipelineConfigurator.LoadQualitySettings();
            SerializedProperty levels = quality.FindProperty("m_QualitySettings");
            Assert.IsNotNull(levels);

            int firstDesktop = RenderPipelineConfigurator.FirstDesktopLevel(levels.arraySize);

            for (int i = 0; i < levels.arraySize; i++)
            {
                SerializedProperty level = levels.GetArrayElementAtIndex(i);
                bool isDesktop = i >= firstDesktop;
                string name = level.FindPropertyRelative("name").stringValue;

                string[] tier = isDesktop
                    ? RenderPipelineConfigurator.DesktopPipelinePaths
                    : RenderPipelineConfigurator.WebPipelinePaths;
                string path = AssetDatabase.GetAssetPath(level.FindPropertyRelative("customRenderPipeline").objectReferenceValue);

                Assert.Contains(path, tier, $"Quality level '{name}' uses the wrong tier's pipeline.");

                string excluded = isDesktop
                    ? RenderPipelineConfigurator.WebGLPlatform
                    : RenderPipelineConfigurator.StandalonePlatform;

                Assert.IsTrue(
                    Excludes(level, excluded),
                    $"Quality level '{name}' is not excluded from {excluded}.");
            }
        }

        [Test]
        public void EachPlatformOffersALowMediumAndHigh()
        {
            // 游戏画质配置按名称选择其构建保留的级别。每个平台需有三个独立管线，
            // 通过阴影分辨率或级联数逐级提升，否则设置就没有实际作用。
            foreach (string[] tier in new[] { RenderPipelineConfigurator.WebPipelinePaths, RenderPipelineConfigurator.DesktopPipelinePaths })
            {
                var low = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(tier[0]);
                var medium = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(tier[1]);
                var high = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(tier[2]);

                Assert.IsNotNull(low, tier[0] + " is missing.");
                Assert.IsNotNull(medium, tier[1] + " is missing.");
                Assert.IsNotNull(high, tier[2] + " is missing.");

                Assert.That(
                    low.mainLightShadowmapResolution * low.shadowCascadeCount,
                    Is.LessThanOrEqualTo(medium.mainLightShadowmapResolution * medium.shadowCascadeCount),
                    tier[1] + " renders less shadow than " + tier[0]);
                Assert.That(
                    medium.mainLightShadowmapResolution * medium.shadowCascadeCount,
                    Is.LessThan(high.mainLightShadowmapResolution * high.shadowCascadeCount),
                    tier[2] + " renders no more shadow than " + tier[1]);
            }
        }

        [Test]
        public void NoBrowserLevelEverRendersWithAmbientOcclusion()
        {
            // 网页渲染器的环境光遮蔽曾以“递归过多（too much recursion）”错误导致浏览器构建崩溃。
            foreach (string path in RenderPipelineConfigurator.WebPipelinePaths)
            {
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                Assert.IsNotNull(pipeline, path + " is missing.");

                var serialized = new SerializedObject(pipeline);
                SerializedProperty renderers = serialized.FindProperty("m_RendererDataList");

                for (int i = 0; i < renderers.arraySize; i++)
                {
                    string renderer = AssetDatabase.GetAssetPath(renderers.GetArrayElementAtIndex(i).objectReferenceValue);
                    Assert.IsFalse(HasAmbientOcclusion(renderer), path + " renders with ambient occlusion.");
                }
            }
        }

        [Test]
        public void EachPlatformStartsInItsOwnTier()
        {
            SerializedObject quality = RenderPipelineConfigurator.LoadQualitySettings();
            int levelCount = quality.FindProperty("m_QualitySettings").arraySize;
            int firstDesktop = RenderPipelineConfigurator.FirstDesktopLevel(levelCount);

            Assert.Less(
                DefaultLevel(quality, RenderPipelineConfigurator.WebGLPlatform), firstDesktop,
                "WebGL starts in the desktop tier.");

            Assert.GreaterOrEqual(
                DefaultLevel(quality, RenderPipelineConfigurator.StandalonePlatform), firstDesktop,
                "The Windows build starts in the web tier and would never show the full graphics.");
        }

        [Test]
        public void BothBuildTargetsPassTheirTierCheck()
        {
            // 与构建脚本构建前运行的检查相同。若这里失败，构建会拒绝启动，这是预期行为，
            // 但应在当前检查中发现。
            Assert.IsNull(BuildScript.TierProblemFor(BuildTarget.WebGL));
            Assert.IsNull(BuildScript.TierProblemFor(BuildTarget.StandaloneWindows64));
        }

        private static bool Excludes(SerializedProperty level, string platform)
        {
            SerializedProperty excluded = level.FindPropertyRelative("excludedTargetPlatforms");

            for (int i = 0; i < excluded.arraySize; i++)
            {
                if (excluded.GetArrayElementAtIndex(i).stringValue == platform)
                {
                    return true;
                }
            }

            return false;
        }

        private static int DefaultLevel(SerializedObject quality, string platform)
        {
            SerializedProperty defaults = quality.FindProperty("m_PerPlatformDefaultQuality");

            for (int i = 0; i < defaults.arraySize; i++)
            {
                SerializedProperty pair = defaults.GetArrayElementAtIndex(i);

                if (pair.FindPropertyRelative("first").stringValue == platform)
                {
                    return pair.FindPropertyRelative("second").intValue;
                }
            }

            Assert.Fail($"No default quality level is set for {platform}.");
            return -1;
        }
    }
}
