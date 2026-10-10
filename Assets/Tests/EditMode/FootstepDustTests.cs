using AdaptiveBossArena.Combat.Feel;
using NUnit.Framework;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试脚步扬起多少尘土。
    /// </summary>
    /// <remarks>
    /// 角色穿过石地板原本毫无扰动，但每步都扬尘更糟：缓慢靠近攻击距离不应拖出尘尾，因此慢移不扬尘。
    /// </remarks>
    [TestFixture]
    public sealed class FootstepDustTests
    {
        [Test]
        public void CreepingIntoRangeLiftsNoDust()
        {
            Assert.AreEqual(0, FootstepEmitter.PuffCount(0f));
            Assert.AreEqual(0, FootstepEmitter.PuffCount(0.2f));
        }

        [Test]
        public void ARunLiftsMoreThanAWalk()
        {
            int walk = FootstepEmitter.PuffCount(0.35f);
            int run = FootstepEmitter.PuffCount(1f);

            Assert.Greater(walk, 0, "A walk lifts nothing.");
            Assert.Greater(run, walk, "A run lifts no more than a walk.");
        }
    }
}
