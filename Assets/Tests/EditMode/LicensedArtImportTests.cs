using System.Collections.Generic;
using System.IO;
using System.Linq;
using AdaptiveBossArena.Editor.Art;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 断言授权 Mixamo 角色及动画导入结果符合游戏所依赖的结构。
    /// </summary>
    /// <remarks>
    /// 这些文件不在仓库中，因此缺失时每个测试会跳过而非失败。导入配置可能无声失败：
    /// 动画文件的 Avatar 未从角色复制时仍能导入和预览，却无法驱动 Humanoid 身体，
    /// 只有运行游戏才会显现。
    /// </remarks>
    [TestFixture]
    public sealed class LicensedArtImportTests
    {
        private static IEnumerable<string> AnimationFiles() =>
            Directory.Exists(LicensedArtPostprocessor.AnimationFolder)
                ? Directory.GetFiles(LicensedArtPostprocessor.AnimationFolder, "*.fbx", SearchOption.AllDirectories)
                    .Select(p => p.Replace('\\', '/'))
                : Enumerable.Empty<string>();

        [SetUp]
        public void RequireTheLicensedArt()
        {
            Assume.That(LicensedArtPostprocessor.Available, "The licensed Mixamo art is not in this checkout.");
        }

        [TestCase(LicensedArtPostprocessor.KnightCharacter)]
        [TestCase(LicensedArtPostprocessor.BossCharacter)]
        public void EachCharacterImportsAValidHumanoid(string path)
        {
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();

            Assert.IsNotNull(avatar, path + " produced no avatar.");
            Assert.IsTrue(avatar.isHuman, path + " imported as a non-humanoid rig.");
            Assert.IsTrue(avatar.isValid, path + " imported an invalid avatar.");
        }

        [Test]
        public void EveryAnimationIsOneHumanoidClipDrivenByItsCharactersSkeleton()
        {
            string[] files = AnimationFiles().ToArray();
            Assert.Greater(files.Length, 0, "No licensed animations found.");

            foreach (string file in files)
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(file);
                AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(file)
                    .OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__"))
                    .ToArray();

                Assert.AreEqual(ModelImporterAnimationType.Human, importer.animationType, file + " is not Humanoid.");
                Assert.AreEqual(ModelImporterAvatarSetup.CopyFromOther, importer.avatarSetup, file + " does not copy its avatar.");
                Assert.IsNotNull(importer.sourceAvatar, file + " has no source avatar.");
                Assert.AreEqual(LicensedArtPostprocessor.CharacterFor(file), AssetDatabase.GetAssetPath(importer.sourceAvatar),
                    file + " copies the wrong character's skeleton.");
                Assert.AreEqual(1, clips.Length, file + " should hold exactly one clip.");
                Assert.AreEqual(LicensedArtPostprocessor.ClipNameFor(file), clips[0].name,
                    file + "'s clip is not named after its file. Mixamo prefixes some downloads with the character " +
                    "it previewed them on, and that prefix is dropped.");
                Assert.IsTrue(clips[0].isHumanMotion, file + "'s clip is not humanoid motion.");
            }
        }

        [Test]
        public void MovementLoopsAndOneShotsDoNot()
        {
            Assert.IsTrue(LicensedArtPostprocessor.IsLooping("sword and shield idle"));
            Assert.IsTrue(LicensedArtPostprocessor.IsLooping("great sword run (2)"));
            Assert.IsTrue(LicensedArtPostprocessor.IsLooping("sword and shield block idle"));
            Assert.IsFalse(LicensedArtPostprocessor.IsLooping("sword and shield slash"));
            Assert.IsFalse(LicensedArtPostprocessor.IsLooping("Getting Up"));
            Assert.IsFalse(LicensedArtPostprocessor.IsLooping("sword and shield 180 turn"));
        }
    }
}
