using System.IO;
using AdaptiveBossArena.Editor;
using NUnit.Framework;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 断言录音能到达预期播放时刻。
    /// </summary>
    /// <remarks>
    /// 玩家觉得合成声音随机。下载录音按文件名逐标识替换，模式无匹配时会继续播放旧声且不报错。
    /// </remarks>
    [TestFixture]
    public sealed class AudioMappingTests
    {
        [TestCase(@"^impactWood_heavy_\d+$")]
        [TestCase(@"^impactMetal_heavy_\d+$")]
        [TestCase(@"^impactBell_heavy_\d+$")]
        [TestCase(@"^impactPunch_heavy_\d+$")]
        [TestCase(@"^footstep_concrete_\d+$")]
        public void EachKenneyCueHasSeveralTakes(string pattern)
        {
            Assume.That(Directory.Exists(AudioAssetBuilder.SoundFolder + "/Kenney"), "The Kenney packs are not downloaded here.");

            AudioClip[] clips = AudioAssetBuilder.ClipsMatching(pattern);

            Assert.GreaterOrEqual(clips.Length, 3, pattern + " found too few takes to vary.");
        }

        [TestCase(@"^507470_take\d+$", 5)]
        [TestCase(@"^(317849|471097)_take\d+$", 10)]
        [TestCase(@"^(125405|869056)_take\d+$", 10)]
        public void EachCompilationWasCutIntoSingleSounds(string pattern, int atLeast)
        {
            // 完整播放合集会让一次打击触发一串挥击声。每个变体必须是短单声。
            Assume.That(Directory.Exists(AudioAssetBuilder.SoundFolder + "/Freesound/Takes"), "The Freesound takes are not here.");

            AudioClip[] takes = AudioAssetBuilder.ClipsMatching(pattern);

            Assert.GreaterOrEqual(takes.Length, atLeast, pattern + " has too few takes.");

            foreach (AudioClip take in takes)
            {
                Assert.Less(take.length, 2.5f, take.name + " is long enough to hold more than one sound.");
            }
        }

        [Test]
        public void TheBlockAndTheParryNeverShareASound()
        {
            // 格挡为盾木声，招架为明亮金属声，玩家必须听出发生了哪一种。
            Assume.That(Directory.Exists(AudioAssetBuilder.SoundFolder + "/Kenney"), "The Kenney packs are not downloaded here.");

            AudioClip[] block = AudioAssetBuilder.ClipsMatching(@"^impactWood_heavy_\d+$");
            AudioClip[] parry = AudioAssetBuilder.ClipsMatching(@"^impactBell_heavy_\d+$");

            CollectionAssert.IsNotEmpty(block);
            CollectionAssert.IsNotEmpty(parry);
            CollectionAssert.AreNotEquivalent(block, parry);
        }
    }
}
