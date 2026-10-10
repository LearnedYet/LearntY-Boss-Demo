using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 保护构建确实包含两个场景，且标题在前。
    /// </summary>
    /// <remarks>
    /// 空场景列表也能编译，运行却黑屏，产物交给人后才发现。生成器固定顺序，
    /// 此测试让回归在 CI 而非玩家手上失败。
    /// </remarks>
    [TestFixture]
    public sealed class BuildSettingsTests
    {
        private const string TitleScene = "Assets/_Project/Scenes/MainMenu.unity";
        private const string ArenaScene = "Assets/_Project/Scenes/Arena.unity";

        private static string[] EnabledScenes() =>
            EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();

        [Test]
        public void BothScenesAreRegisteredAndEnabled()
        {
            string[] enabled = EnabledScenes();

            CollectionAssert.Contains(enabled, TitleScene, "The title scene is not in the build.");
            CollectionAssert.Contains(enabled, ArenaScene, "The arena scene is not in the build.");
        }

        [Test]
        public void TheTitleSceneLoadsFirst()
        {
            Assert.AreEqual(TitleScene, EnabledScenes().FirstOrDefault(),
                "The title scene must be first, or the game boots straight into the fight.");
        }
    }
}
