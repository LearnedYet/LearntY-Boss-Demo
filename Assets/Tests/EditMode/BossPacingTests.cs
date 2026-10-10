using AdaptiveBossArena.AI;
using NUnit.Framework;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试首领在攻击之间的移动。
    /// </summary>
    /// <remarks>
    /// 玩家要求缓慢沉重的行走，结果首领却跑步绕圈，在接近和后退硬边界间闪烁。
    /// </remarks>
    [TestFixture]
    public sealed class BossPacingTests
    {
        [Test]
        public void TheBossStalksAtAWalkNotARun()
        {
            Vector3 move = BossPacing.Stalk(Vector3.forward, distance: 3f, preferredRange: 3f, stalkFraction: 0.35f);

            Assert.AreEqual(0.35f, move.magnitude, 0.001f, "The stalk is not at the configured walk.");
        }

        [Test]
        public void AtItsPreferredRangeItCirclesRatherThanClosing()
        {
            Vector3 move = BossPacing.Stalk(Vector3.forward, distance: 3f, preferredRange: 3f, stalkFraction: 0.35f);

            Assert.AreEqual(0f, Vector3.Dot(move.normalized, Vector3.forward), 0.001f, "It walks toward the player at its preferred range.");
        }

        [Test]
        public void ClosingAndGivingGroundBlendSmoothlyThroughThePreferredRange()
        {
            float previous = float.PositiveInfinity;

            // 从远到近行走时，朝玩家移动的驱动力必须持续平缓下降，没有突变边缘。
            for (float distance = 6f; distance >= 0f; distance -= 0.25f)
            {
                Vector3 move = BossPacing.Stalk(Vector3.forward, distance, preferredRange: 3f, stalkFraction: 1f);
                float pull = Vector3.Dot(move, Vector3.forward);

                Assert.LessOrEqual(pull, previous + 0.0001f, $"The pull jumped at {distance} m.");
                previous = pull;
            }
        }

        [Test]
        public void ItWalksTheLastStretchAndRunsOnlyAcrossRealDistance()
        {
            float near = BossPacing.ApproachSpeed(distanceBeyondReach: 0.5f, runDistance: 6f, walkFraction: 0.45f, aggression: 1f);
            float far = BossPacing.ApproachSpeed(distanceBeyondReach: 8f, runDistance: 6f, walkFraction: 0.45f, aggression: 1f);

            Assert.Less(near, 0.5f, "It runs the last stretch to a blow.");
            Assert.AreEqual(1f, far, 0.001f, "It does not run across real distance.");
        }

        [Test]
        public void LearnedAggressionMakesItComeOnHarder()
        {
            float calm = BossPacing.ApproachSpeed(3f, 6f, 0.45f, aggression: 0f);
            float pressing = BossPacing.ApproachSpeed(3f, 6f, 0.45f, aggression: 1f);

            Assert.Greater(pressing, calm);
        }
    }
}
