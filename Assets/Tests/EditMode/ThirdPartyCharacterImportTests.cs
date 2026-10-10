using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 断言导入的角色美术符合游戏所依赖的结构。
    /// </summary>
    /// <remarks>
    /// 导入配置可能无声失败。骨架回退为 Generic 时仍能导入、仍可在预览中播放，
    /// 却无法驱动游戏的 Humanoid 身体，也找不到手骨；只有运行游戏才会显现。此处固定代码依赖的配置。
    /// </remarks>
    [TestFixture]
    public sealed class ThirdPartyCharacterImportTests
    {
        private const string Library1 =
            "Assets/_Project/Art/ThirdParty/Quaternius/UniversalAnimationLibrary/UAL1_Standard.fbx";

        private const string Library2 =
            "Assets/_Project/Art/ThirdParty/Quaternius/UniversalAnimationLibrary2/UAL2_Standard.fbx";

        private static IEnumerable<AnimationClip> ClipsIn(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__"));

        [TestCase(Library1)]
        [TestCase(Library2)]
        public void EachLibraryImportsAValidHumanoidAvatar(string path)
        {
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();

            Assert.IsNotNull(avatar, $"{path} produced no avatar.");
            Assert.IsTrue(avatar.isHuman, $"{path} imported as a non-humanoid rig.");
            Assert.IsTrue(avatar.isValid, $"{path} imported an invalid avatar; its bones did not map.");
        }

        [Test]
        public void TheClipsTheGameUsesArePresent()
        {
            var names = new HashSet<string>(ClipsIn(Library1).Concat(ClipsIn(Library2)).Select(c => c.name));

            string[] required =
            {
                "Idle_Loop", "Walk_Loop", "Jog_Fwd_Loop", "Sprint_Loop", "Roll", "Sword_Idle",
                "Sword_Attack", "Hit_Chest", "Death01", "Sword_Regular_A", "Sword_Regular_B",
                "Sword_Regular_C", "Sword_Block", "Hit_Knockback", "LayToIdle"
            };

            foreach (string clip in required)
            {
                Assert.IsTrue(
                    names.Contains(clip),
                    "Required clip " + clip + " is missing. Present: " + string.Join(", ", names.OrderBy(n => n)));
            }
        }

        [Test]
        public void LocomotionLoopsAndAttacksPlayOnce()
        {
            Dictionary<string, AnimationClip> clips = ClipsIn(Library1)
                .Concat(ClipsIn(Library2))
                .GroupBy(c => c.name)
                .ToDictionary(g => g.Key, g => g.First());

            Assert.IsTrue(clips["Walk_Loop"].isLooping, "Walking does not loop.");
            Assert.IsTrue(clips["Sword_Idle"].isLooping, "The sword idle does not loop.");
            Assert.IsFalse(clips["Sword_Regular_A"].isLooping, "An attack loops.");
            Assert.IsFalse(clips["Roll"].isLooping, "The roll loops.");
        }
    }
}
