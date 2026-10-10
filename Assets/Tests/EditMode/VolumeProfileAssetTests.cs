using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试竞技场后处理能否在保存并重新加载后保留。
    /// </summary>
    /// <remarks>
    /// 配置的效果仅在内存中添加，从未保存进资源。运行设置的编辑器会话看起来已有调色，
    /// 而所有构建和后续会话都加载全空配置：没有泛光、色调映射、暗角或调色。
    /// </remarks>
    [TestFixture]
    public sealed class VolumeProfileAssetTests
    {
        [Test]
        public void EveryEffectIsSavedInsideTheProfileAsset()
        {
            const string path = "Assets/_Project/Settings/ArenaVolumeProfile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            Assume.That(profile != null, "Setup has not generated the profile.");

            Assert.Greater(profile.components.Count, 0, "The profile carries no effects.");

            foreach (VolumeComponent component in profile.components)
            {
                Assert.IsNotNull(component, "The profile holds an effect that was never saved.");
                Assert.AreEqual(path, AssetDatabase.GetAssetPath(component), component.GetType().Name + " is not saved in the profile.");
            }

            Assert.IsTrue(profile.Has<Bloom>(), "No bloom.");
            Assert.IsTrue(profile.Has<Tonemapping>(), "No tonemapping.");
            Assert.IsTrue(profile.Has<ColorAdjustments>(), "No grade.");
        }

        [Test]
        public void TheBrowserProfileKeepsTheGradeButDropsTheExpensivePasses()
        {
            var web = AssetDatabase.LoadAssetAtPath<VolumeProfile>(AdaptiveBossArena.Editor.PostProcessingConfigurator.WebProfilePath);
            Assume.That(web != null, "Setup has not generated the browser profile.");

            foreach (VolumeComponent component in web.components)
            {
                Assert.IsNotNull(component, "The browser profile holds an effect that was never saved.");
            }

            Assert.IsFalse(web.Has<FilmGrain>(), "The browser pays for film grain.");
            Assert.IsFalse(web.Has<ChromaticAberration>(), "The browser pays for chromatic aberration.");
            Assert.IsTrue(web.Has<ColorAdjustments>(), "The browser lost the grade.");
            Assert.IsTrue(web.Has<Tonemapping>(), "The browser lost tonemapping.");

            Assert.IsTrue(web.TryGet(out Bloom bloom), "The browser lost bloom.");
            Assert.IsFalse(bloom.highQualityFiltering.value, "The browser's bloom uses high-quality filtering.");
            Assert.AreEqual(BloomDownscaleMode.Quarter, bloom.downscale.value, "The browser's bloom is not quarter resolution.");
        }

        [TestCase("Assets/_Project/Settings/UniversalRenderer.asset")]
        [TestCase("Assets/_Project/Settings/UniversalRenderer_Desktop.asset")]
        public void EveryRendererCarriesThePostProcessingResources(string path)
        {
            // 没有这些配置，无论相机和 Volume 如何设置，URP 都会跳过该渲染器的全部效果。
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            Assume.That(renderer != null, "Setup has not generated the renderer.");

            Assert.IsNotNull(renderer.postProcessData, path + " has no post-processing resources.");
        }
    }
}
