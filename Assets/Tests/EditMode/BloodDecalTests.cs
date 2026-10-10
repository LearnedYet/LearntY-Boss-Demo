using AdaptiveBossArena.Combat.Feel;
using NUnit.Framework;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试血迹在地面保留多久。
    /// </summary>
    /// <remarks>
    /// 打击喷血但地面仍干净，长战斗也不留痕迹。
    /// </remarks>
    [TestFixture]
    public sealed class BloodDecalTests
    {
        [Test]
        public void FreshBloodIsAtFullStrength()
        {
            Assert.AreEqual(1f, BloodDecalPool.AlphaAt(0f));
            Assert.AreEqual(1f, BloodDecalPool.AlphaAt(BloodDecalPool.HoldSeconds));
        }

        [Test]
        public void OldBloodFadesAwayCompletely()
        {
            float midFade = BloodDecalPool.AlphaAt(BloodDecalPool.HoldSeconds + BloodDecalPool.FadeSeconds * 0.5f);

            Assert.That(midFade, Is.InRange(0.1f, 0.9f));
            Assert.AreEqual(0f, BloodDecalPool.AlphaAt(BloodDecalPool.HoldSeconds + BloodDecalPool.FadeSeconds), 0.0001f);
        }
    }
}
