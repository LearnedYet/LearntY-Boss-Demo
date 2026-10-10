using AdaptiveBossArena.Utilities.Statistics;
using NUnit.Framework;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试项目全部性能预算所依据的百分位计算。
    /// </summary>
    /// <remarks>
    /// 预算的可信度取决于比较数值。插值或把尖峰平均掉的百分位，会让卡顿构建通过。
    /// </remarks>
    [TestFixture]
    public sealed class FrameTimeSamplerTests
    {
        [Test]
        public void AnEmptySamplerReportsZero()
        {
            var sampler = new FrameTimeSampler(10);

            Assert.AreEqual(0f, sampler.PercentileMilliseconds(0.95f));
            Assert.AreEqual(0f, sampler.MaxMilliseconds);
        }

        [Test]
        public void TheMedianOfOrderedFramesIsTheMiddleFrame()
        {
            var sampler = new FrameTimeSampler(10);

            for (int i = 1; i <= 9; i++)
            {
                sampler.Add(i / 1000f);
            }

            Assert.AreEqual(5f, sampler.PercentileMilliseconds(0.5f), 0.001f);
        }

        [Test]
        public void ASingleSpikeShowsUpInTheMaxButNotTheMedian()
        {
            // 百分位的意义：普通帧中的一次 40 毫秒卡顿必须可见，又不拖高典型帧。
            var sampler = new FrameTimeSampler(100);

            for (int i = 0; i < 99; i++)
            {
                sampler.Add(0.004f);
            }

            sampler.Add(0.040f);

            Assert.AreEqual(4f, sampler.PercentileMilliseconds(0.5f), 0.001f);
            Assert.AreEqual(40f, sampler.MaxMilliseconds, 0.001f);
        }

        [Test]
        public void ThePercentileIsAlwaysAFrameThatActuallyHappened()
        {
            // 使用最近秩而非插值：两帧差异很大时，p95 必须是其中一帧，而非没有任何帧经历的中间值。
            var sampler = new FrameTimeSampler(10);
            sampler.Add(0.010f);
            sampler.Add(0.030f);

            float p95 = sampler.PercentileMilliseconds(0.95f);

            Assert.IsTrue(
                System.Math.Abs(p95 - 10f) < 0.001f || System.Math.Abs(p95 - 30f) < 0.001f,
                $"p95 was {p95} ms, which no recorded frame took.");
        }

        [Test]
        public void SamplesPastCapacityAreDroppedNotWrapped()
        {
            var sampler = new FrameTimeSampler(3);
            sampler.Add(0.001f);
            sampler.Add(0.002f);
            sampler.Add(0.003f);
            sampler.Add(0.900f);

            Assert.IsTrue(sampler.IsFull);
            Assert.AreEqual(3, sampler.Count);
            Assert.AreEqual(3f, sampler.MaxMilliseconds, 0.001f);
        }

        [Test]
        public void TheWebPageAsksForACaptureThroughItsQueryString()
        {
            // 已部署 WebGL 没有命令行，这是开始采样的唯一方式。
            Assert.IsTrue(PerfCaptureRequest.TryReadQuerySeconds(
                "https://example.test/arena/?perf=60", out float seconds));
            Assert.AreEqual(60f, seconds, 0.001f);

            Assert.IsTrue(PerfCaptureRequest.TryReadQuerySeconds(
                "https://example.test/?a=1&perf=12.5#top", out seconds));
            Assert.AreEqual(12.5f, seconds, 0.001f);
        }

        [Test]
        public void APageWithoutTheQueryKeyDoesNotCapture()
        {
            Assert.IsFalse(PerfCaptureRequest.TryReadQuerySeconds("https://example.test/arena/", out _));
            Assert.IsFalse(PerfCaptureRequest.TryReadQuerySeconds("https://example.test/?perfx=5", out _));
            Assert.IsFalse(PerfCaptureRequest.TryReadQuerySeconds("https://example.test/?perf=0", out _));
            Assert.IsFalse(PerfCaptureRequest.TryReadQuerySeconds(string.Empty, out _));
        }

        [Test]
        public void ADesktopRunAsksForACaptureOnTheCommandLine()
        {
            PerfCaptureRequest request = PerfCaptureRequest.Read(
                new[] { "game.exe", "-perfCapture", "60", "-perfLog", @"C:\logs\perf.txt", "-perfQuit" },
                string.Empty);

            Assert.IsTrue(request.IsRequested);
            Assert.AreEqual(60f, request.Seconds, 0.001f);
            Assert.AreEqual(@"C:\logs\perf.txt", request.LogPath);
            Assert.IsTrue(request.QuitWhenDone);
        }

        [Test]
        public void AnOrdinaryLaunchRequestsNothing()
        {
            // 发布游戏绝不能自动跳过标题或自行退出。
            PerfCaptureRequest request = PerfCaptureRequest.Read(new[] { "game.exe" }, string.Empty);

            Assert.IsFalse(request.IsRequested);
            Assert.IsFalse(request.QuitWhenDone);
            Assert.IsNull(request.ShotFolder);
        }

        [Test]
        public void ACaptureCanRecordASequenceOfFrames()
        {
            // 运动必须动态判断，单帧看不出挥击重量。
            PerfCaptureRequest request = PerfCaptureRequest.Read(
                new[] { "game.exe", "-perfCapture", "10", "-perfShots", @"C:\frames", "-perfShotEvery", "0.1" },
                string.Empty);

            Assert.AreEqual(@"C:\frames", request.ShotFolder);
            Assert.AreEqual(0.1f, request.ShotEverySeconds, 0.0001f);
        }

        [Test]
        public void AFrameSequenceDefaultsToTenFramesASecond()
        {
            PerfCaptureRequest request = PerfCaptureRequest.Read(
                new[] { "game.exe", "-perfCapture", "10", "-perfShots", @"C:\frames" }, string.Empty);

            Assert.AreEqual(0.1f, request.ShotEverySeconds, 0.0001f);
        }

        [Test]
        public void NonPositiveFramesAreIgnored()
        {
            // 暂停后首帧可能 delta 为零，计入会将百分位拉向没有成本的帧。
            var sampler = new FrameTimeSampler(3);
            sampler.Add(0f);
            sampler.Add(-1f);

            Assert.AreEqual(0, sampler.Count);
        }
    }
}
