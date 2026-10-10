using AdaptiveBossArena.Utilities.Audio;
using NUnit.Framework;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试生成而非人工编写的合成音频。
    /// </summary>
    /// <remarks>
    /// 游戏每个声音都是启动时构建的数字缓冲区，因此故障属于算术问题，可通过断言而非听觉检查。
    /// 这里固定曾让声音单薄的三个问题：响度依赖音色，缓冲区没有淡出就停止，循环末尾不接合开头。
    /// </remarks>
    [TestFixture]
    public sealed class ToneGeneratorTests
    {
        /// <summary>峰值容差，足够宽松以承受重采样差异。</summary>
        private const float PeakTolerance = 0.02f;

        private static float[] Read(AudioClip clip)
        {
            var samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);

            return samples;
        }

        private static float PeakOf(float[] samples)
        {
            float loudest = 0f;

            foreach (float sample in samples)
            {
                loudest = Mathf.Max(loudest, Mathf.Abs(sample));
            }

            return loudest;
        }

        [Test]
        public void AClipIsBuiltAtTheRequestedLoudness()
        {
            AudioClip clip = ToneGenerator.CreateImpact("test.peak", peak: 0.6f);

            Assert.AreEqual(0.6f, PeakOf(Read(clip)), PeakTolerance);

            Object.DestroyImmediate(clip);
        }

        [Test]
        public void LoudnessDoesNotDependOnTimbre()
        {
            // 此测试针对的故障：单极低通按截止频率衰减，因此声音越沉闷，音量就越低。重击、
            // 处决和低生命心跳曾比轻刺安静数倍，恰好颠倒。截止频率只能决定音色，不能影响其它内容。
            AudioClip bright = ToneGenerator.CreateImpact("test.bright", lowPassHz: 2200f, peak: 0.7f);
            AudioClip dull = ToneGenerator.CreateImpact("test.dull", lowPassHz: 85f, peak: 0.7f);

            Assert.AreEqual(PeakOf(Read(bright)), PeakOf(Read(dull)), PeakTolerance,
                "A duller impact should not be a quieter one.");

            Object.DestroyImmediate(bright);
            Object.DestroyImmediate(dull);
        }

        [Test]
        public void AOneShotFadesOutRatherThanStopping()
        {
            // 指数衰减永远不达到零，因此直接结束缓冲区会产生阶跃，而阶跃就是咔哒声。踉跄音效最严重，
            // 结束时仍有峰值的五分之一。
            AudioClip clip = ToneGenerator.CreateStumble("test.stumble");
            float[] samples = Read(clip);

            Assert.Less(Mathf.Abs(samples[samples.Length - 1]), 0.001f,
                "The clip ends abruptly, which is heard as a click.");

            Object.DestroyImmediate(clip);
        }

        [Test]
        public void EveryOneShotEndsQuietly()
        {
            AudioClip[] clips =
            {
                ToneGenerator.CreateImpact("test.impact"),
                ToneGenerator.CreateMetallicRing("test.ring"),
                ToneGenerator.CreateShimmer("test.shimmer"),
                ToneGenerator.CreateWhoosh("test.whoosh"),
                ToneGenerator.CreatePerilWarning("test.peril"),
                ToneGenerator.CreateStumble("test.stumble2"),
                ToneGenerator.CreateRoar("test.roar")
            };

            foreach (AudioClip clip in clips)
            {
                float[] samples = Read(clip);

                Assert.Less(Mathf.Abs(samples[samples.Length - 1]), 0.001f,
                    $"{clip.name} ends abruptly.");

                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void ALoopMeetsItsOwnBeginning()
        {
            // 循环永远从末尾回到开头，因此末采样与首采样不一致会每周期产生一次杂音，
            // 比承载它的持续低鸣明显得多。
            AudioClip clip = ToneGenerator.CreateDrone("test.drone");
            float[] samples = Read(clip);

            float seam = Mathf.Abs(samples[samples.Length - 1] - samples[0]);

            Assert.Less(seam, 0.05f, "The loop's end does not meet its start.");

            Object.DestroyImmediate(clip);
        }

        [Test]
        public void ALoopIsNotFadedToSilenceAtItsEnd()
        {
            // 淡出循环会每周期造成一次声音缺口，因此单次音效使用的释放斜坡不能用于这里。
            AudioClip clip = ToneGenerator.CreatePulse("test.pulse");
            float[] samples = Read(clip);

            Assert.Greater(PeakOf(samples), 0.1f);
            Assert.Greater(
                Mathf.Abs(samples[samples.Length - 1]) + Mathf.Abs(samples[samples.Length - 2]),
                0f,
                "A looping clip should not fade to digital silence at its end.");

            Object.DestroyImmediate(clip);
        }

        [Test]
        public void ClipsAreBuiltAtTheOutputSampleRate()
        {
            // 以固定采样率生成而输出使用另一采样率，会无意义地在每次播放时重采样所有片段。
            AudioClip clip = ToneGenerator.CreateImpact("test.rate");

            Assert.AreEqual(ToneGenerator.SampleRate, clip.frequency);

            Object.DestroyImmediate(clip);
        }
    }
}
