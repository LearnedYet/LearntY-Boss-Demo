using AdaptiveBossArena.Combat;
using NUnit.Framework;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试挥击何时停止追踪目标，保证晚闪避有效。
    /// </summary>
    [TestFixture]
    public sealed class AttackTrackingTests
    {
        private const float Startup = 0.5f;
        private const float Window = 0.6f;

        [Test]
        public void ASwingTracksEarlyInItsWindUp()
        {
            Assert.IsTrue(AttackTracking.CanTrack(AttackPhase.Startup, 0.1f, Startup, Window));
        }

        [Test]
        public void ASwingCommitsBeforeItsWindUpEnds()
        {
            // 前摇最后一段已投入攻击。此时还能转向就会追随晚闪避直接命中。
            Assert.IsFalse(AttackTracking.CanTrack(AttackPhase.Startup, 0.35f, Startup, Window));
        }

        [Test]
        public void ALiveOrRecoveringSwingNeverTurns()
        {
            Assert.IsFalse(AttackTracking.CanTrack(AttackPhase.Active, 0f, Startup, Window));
            Assert.IsFalse(AttackTracking.CanTrack(AttackPhase.Recovery, 0f, Startup, Window));
        }

        [Test]
        public void AZeroWindowMeansNoTrackingAtAll()
        {
            Assert.IsFalse(AttackTracking.CanTrack(AttackPhase.Startup, 0f, Startup, 0f));
        }
    }
}
