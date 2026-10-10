using System;
using AdaptiveBossArena.Utilities.Statistics;
using NUnit.Framework;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试控制首领适应速度的平滑基础算法。
    /// </summary>
    /// <remarks>
    /// 半衰期约定属于设计承诺，而非仅仅实现细节。若十秒半衰期并未在十秒内将数值影响减半，
    /// 所有依据它调节的适应时机都会出错，且试玩无法诊断原因。
    /// </remarks>
    [TestFixture]
    public sealed class EwmaTests
    {
        private const float Tolerance = 0.001f;

        [Test]
        public void Constructor_WithNonPositiveHalfLife_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Ewma(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Ewma(-1f));
        }

        [Test]
        public void BeforeAnySample_ReportsNoSamples()
        {
            var average = new Ewma(1f);

            Assert.IsFalse(average.HasSamples);
            Assert.AreEqual(0, average.SampleCount);
            Assert.AreEqual(0f, average.Value, Tolerance);
        }

        [Test]
        public void FirstSample_IsAdoptedOutright()
        {
            var average = new Ewma(10f);

            average.AddSample(0.8f, 0.5f);

            // 将首次采样与隐含零值混合，会在战斗开头数秒使每个行为特征都偏低。
            Assert.AreEqual(0.8f, average.Value, Tolerance);
        }

        [Test]
        public void AfterOneHalfLife_InfluenceOfOldValueIsHalved()
        {
            const float halfLife = 4f;
            var average = new Ewma(halfLife);

            average.AddSample(0f, 0f);
            average.AddSample(1f, halfLife);

            // 从旧值到新值的距离应已缩短一半。
            Assert.AreEqual(0.5f, average.Value, Tolerance);
        }

        [Test]
        public void RepeatedSamples_ConvergeTowardTheSampledValue()
        {
            var average = new Ewma(1f);
            average.AddSample(0f, 0f);

            for (int i = 0; i < 50; i++)
            {
                average.AddSample(1f, 0.25f);
            }

            Assert.AreEqual(1f, average.Value, 0.01f);
            Assert.AreEqual(51, average.SampleCount);
        }

        [Test]
        public void DecayToward_ErodesTheValueWhenBehaviourStops()
        {
            const float halfLife = 2f;
            var average = new Ewma(halfLife);
            average.AddSample(1f, 0f);

            average.DecayToward(0f, halfLife);

            // 玩家放弃的习惯必须淡出，否则首领会持续反制已不再使用的战术。
            Assert.AreEqual(0.5f, average.Value, Tolerance);
        }

        [Test]
        public void DecayToward_BeforeAnySample_DoesNothing()
        {
            var average = new Ewma(1f);

            average.DecayToward(1f, 10f);

            Assert.AreEqual(0f, average.Value, Tolerance);
            Assert.IsFalse(average.HasSamples);
        }

        [Test]
        public void Reset_ReturnsToUnobservedState()
        {
            var average = new Ewma(1f);
            average.AddSample(0.7f, 0f);

            average.Reset();

            Assert.IsFalse(average.HasSamples);
            Assert.AreEqual(0f, average.Value, Tolerance);
        }
    }

    /// <summary>
    /// 测试判断玩家移动是否可利用的方向直方图。
    /// </summary>
    /// <remarks>
    /// 可预测性决定是否允许针对偏好闪避方向提前瞄准的反制策略。若均匀分布移动也被报告为高值，
    /// 首领就会像在预测没有依据的闪避，这恰是设计禁止的作弊印象。
    /// </remarks>
    [TestFixture]
    public sealed class DirectionHistogramTests
    {
        private const float Tolerance = 0.001f;

        [Test]
        public void BeforeAnyInput_ReportsNoSignal()
        {
            var histogram = new DirectionHistogram();

            Assert.IsFalse(histogram.HasSignal);
            Assert.AreEqual(-1, histogram.DominantBin);
            Assert.AreEqual(0f, histogram.Predictability, Tolerance);
        }

        [Test]
        public void Add_ZeroLengthDirection_IsDiscarded()
        {
            var histogram = new DirectionHistogram();

            histogram.Add(Vector2.zero);

            Assert.IsFalse(histogram.HasSignal);
        }

        [Test]
        public void RepeatedSameDirection_ProducesMaximumPredictability()
        {
            var histogram = new DirectionHistogram();

            for (int i = 0; i < 20; i++)
            {
                histogram.Add(Vector2.left);
            }

            Assert.AreEqual(1f, histogram.Predictability, 0.01f);
            Assert.AreEqual(DirectionHistogram.BinForDirection(Vector2.left), histogram.DominantBin);
            Assert.AreEqual(1f, histogram.NormalizedWeight(histogram.DominantBin), Tolerance);
        }

        [Test]
        public void UniformlySpreadDirections_ProduceMinimumPredictability()
        {
            var histogram = new DirectionHistogram();

            for (int bin = 0; bin < DirectionHistogram.BinCount; bin++)
            {
                histogram.Add(DirectionHistogram.BinCenterDirection(bin));
            }

            Assert.AreEqual(0f, histogram.Predictability, 0.01f);
        }

        [Test]
        public void BinForDirection_MapsCardinalsToDistinctBins()
        {
            int east = DirectionHistogram.BinForDirection(Vector2.right);
            int north = DirectionHistogram.BinForDirection(Vector2.up);
            int west = DirectionHistogram.BinForDirection(Vector2.left);
            int south = DirectionHistogram.BinForDirection(Vector2.down);

            Assert.AreEqual(0, east);
            Assert.AreEqual(2, north);
            Assert.AreEqual(4, west);
            Assert.AreEqual(6, south);
        }

        [Test]
        public void BinForDirection_AtTheAngleSeam_StaysInRange()
        {
            // 紧邻 pi 两侧的方向，是朴素舍入会推出有效范围的情况。
            for (float degrees = -180f; degrees <= 180f; degrees += 7.5f)
            {
                float radians = degrees * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));

                int bin = DirectionHistogram.BinForDirection(direction);

                Assert.GreaterOrEqual(bin, 0);
                Assert.Less(bin, DirectionHistogram.BinCount);
            }
        }

        [Test]
        public void Decay_ReducesAccumulatedWeightByHalfOverOneHalfLife()
        {
            const float halfLife = 5f;
            var histogram = new DirectionHistogram();
            histogram.Add(Vector2.right, 4f);

            histogram.Decay(halfLife, halfLife);

            Assert.AreEqual(2f, histogram.TotalWeight, Tolerance);
        }

        [Test]
        public void NormalizedWeight_OutsideBinRange_Throws()
        {
            var histogram = new DirectionHistogram();

            Assert.Throws<ArgumentOutOfRangeException>(() => histogram.NormalizedWeight(-1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => histogram.NormalizedWeight(DirectionHistogram.BinCount));
        }
    }
}
