using AdaptiveBossArena.Combat;
using NUnit.Framework;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 断言打击部位两帧间的路径被足够密集地覆盖，不会跳过任何身体。
    /// </summary>
    /// <remarks>
    /// 如今命中由刀刃实际接触决定。巨剑剑尖在挥击一帧内可跨过一米以上，
    /// 仅检测终点会让它在帧间直接穿过骑士；这与“刀刃从旁边经过却仍伤害我”的故障相反，同样不可信。
    /// </remarks>
    [TestFixture]
    public sealed class StrikeSweepTests
    {
        [Test]
        public void APartThatHasNotMovedIsTestedOnce()
        {
            int samples = StrikeSweep.SampleCount(
                Vector3.zero, Vector3.forward, Vector3.zero, Vector3.forward, radius: 0.1f, maximum: 6);

            Assert.AreEqual(1, samples);
        }

        [Test]
        public void NoStepIsLongerThanThePartIsThick()
        {
            // 剑尖扫过 0.5 米；宽 0.1 米的刀刃需五步采样，使采样间距始终不超过刀刃宽度。
            int samples = StrikeSweep.SampleCount(
                Vector3.zero, Vector3.forward, Vector3.zero, new Vector3(0.5f, 0f, 1f), radius: 0.1f, maximum: 10);

            Assert.AreEqual(5, samples);
        }

        [Test]
        public void ATeleportCannotCostUnboundedQueries()
        {
            int samples = StrikeSweep.SampleCount(
                Vector3.zero, Vector3.forward, Vector3.one * 100f, Vector3.one * 101f, radius: 0.1f, maximum: 6);

            Assert.AreEqual(6, samples);
        }

        [Test]
        public void TheSweepRunsFromLastFramesPoseToThisOne()
        {
            Vector3 previousStart = Vector3.zero;
            Vector3 previousEnd = Vector3.forward;
            Vector3 start = Vector3.right;
            Vector3 end = Vector3.right + Vector3.forward;

            StrikeSweep.Between(previousStart, previousEnd, start, end, 0f, out Vector3 a0, out Vector3 b0);
            StrikeSweep.Between(previousStart, previousEnd, start, end, 0.5f, out Vector3 aHalf, out Vector3 bHalf);
            StrikeSweep.Between(previousStart, previousEnd, start, end, 1f, out Vector3 a1, out Vector3 b1);

            Assert.AreEqual(previousStart, a0);
            Assert.AreEqual(previousEnd, b0);
            Assert.AreEqual(new Vector3(0.5f, 0f, 0f), aHalf);
            Assert.AreEqual(new Vector3(0.5f, 0f, 1f), bHalf);
            Assert.AreEqual(start, a1);
            Assert.AreEqual(end, b1);
        }
    }
}
