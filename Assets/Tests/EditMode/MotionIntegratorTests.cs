using AdaptiveBossArena.Combat.Movement;
using NUnit.Framework;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试攻击如何推动试图向另一方向移动的目标。
    /// </summary>
    /// <remarks>
    /// 击退原先覆盖速度，随后在下一帧被受击者自身移动替换，首领则根本没有应用击退。
    /// 这里固定真实推力的三项属性：转向后仍存在，按独立于帧率的速率衰减，重力用于贴地而非持续积累。
    /// </remarks>
    [TestFixture]
    public sealed class MotionIntegratorTests
    {
        private const float Gravity = 25f;
        private const float Grounded = -2f;
        private const float Frame = 1f / 60f;
        private const float HalfLife = 0.1f;

        [Test]
        public void AShoveMovesItsVictimOnTheFrameItLands()
        {
            var motion = new MotionIntegrator(Gravity, Grounded);
            motion.AddImpulse(new Vector3(8f, 0f, 0f));

            Vector3 velocity = motion.Step(Vector3.zero, HalfLife, true, Frame);

            Assert.AreEqual(8f, velocity.x, 0.001f);
        }

        [Test]
        public void AShoveSurvivesTheVictimSteeringTheOtherWay()
        {
            // 确切的故障：转向每帧都写入自身速度。推力必须叠加，而非被其替换。
            var motion = new MotionIntegrator(Gravity, Grounded);
            motion.AddImpulse(new Vector3(8f, 0f, 0f));

            Vector3 velocity = motion.Step(new Vector3(-7f, 0f, 0f), HalfLife, true, Frame);

            Assert.AreEqual(1f, velocity.x, 0.001f, "Steering replaced the shove instead of adding to it.");
        }

        [Test]
        public void AShoveLosesHalfItsSpeedAfterOneHalfLife()
        {
            var motion = new MotionIntegrator(Gravity, Grounded);
            motion.AddImpulse(new Vector3(8f, 0f, 0f));

            int frames = Mathf.RoundToInt(HalfLife / Frame);

            for (int i = 0; i < frames; i++)
            {
                motion.Step(Vector3.zero, HalfLife, true, Frame);
            }

            Assert.AreEqual(4f, motion.Impulse.x, 0.05f);
        }

        [Test]
        public void TheSameShoveTravelsTheSameDistanceAtAnyFrameRate()
        {
            // 若 144 Hz 下的击退比 30 Hz 更远，同一击就会在不同机器上产生不同战斗结果。
            float distanceAt30 = DistanceTravelled(1f / 30f);
            float distanceAt144 = DistanceTravelled(1f / 144f);

            Assert.AreEqual(distanceAt30, distanceAt144, distanceAt144 * 0.15f);
        }

        [Test]
        public void TwoBlowsTogetherPushFurtherThanOne()
        {
            var one = new MotionIntegrator(Gravity, Grounded);
            one.AddImpulse(new Vector3(5f, 0f, 0f));

            var two = new MotionIntegrator(Gravity, Grounded);
            two.AddImpulse(new Vector3(5f, 0f, 0f));
            two.AddImpulse(new Vector3(5f, 0f, 0f));

            Assert.Greater(two.Impulse.x, one.Impulse.x);
        }

        [Test]
        public void AShoveNeverLiftsTheCharacter()
        {
            var motion = new MotionIntegrator(Gravity, Grounded);
            motion.AddImpulse(new Vector3(0f, 50f, 3f));

            Vector3 velocity = motion.Step(Vector3.zero, HalfLife, true, Frame);

            Assert.AreEqual(Grounded, velocity.y, 0.001f, "A horizontal shove launched the character.");
        }

        [Test]
        public void StandingStillNeverBuildsUpFallingSpeed()
        {
            // 在地面的角色保持接触速度。若重力持续积累，站立一分钟后走出边缘的角色会以从未实际下落至
            // 的速度被抛出。
            var motion = new MotionIntegrator(Gravity, Grounded);

            for (int i = 0; i < 600; i++)
            {
                motion.Step(Vector3.zero, HalfLife, true, Frame);
            }

            Assert.AreEqual(Grounded, motion.VerticalVelocity, 0.001f);
        }

        [Test]
        public void OffTheGroundGravityAccelerates()
        {
            var motion = new MotionIntegrator(Gravity, Grounded);

            motion.Step(Vector3.zero, HalfLife, false, 0.1f);

            Assert.AreEqual(Grounded - Gravity * 0.1f, motion.VerticalVelocity, 0.001f);
        }

        [Test]
        public void ResetClearsEveryImposedMotion()
        {
            var motion = new MotionIntegrator(Gravity, Grounded);
            motion.AddImpulse(new Vector3(9f, 0f, 9f));
            motion.Step(Vector3.zero, HalfLife, false, 0.5f);

            motion.Reset();

            Assert.AreEqual(Vector3.zero, motion.Impulse);
            Assert.AreEqual(Grounded, motion.VerticalVelocity, 0.001f);
        }

        private static float DistanceTravelled(float frame)
        {
            var motion = new MotionIntegrator(Gravity, Grounded);
            motion.AddImpulse(new Vector3(10f, 0f, 0f));

            float distance = 0f;

            for (float t = 0f; t < 1f; t += frame)
            {
                distance += motion.Step(Vector3.zero, HalfLife, true, frame).x * frame;
            }

            return distance;
        }

        [Test]
        public void ALaunchRisesAndComesBackDown()
        {
            var motion = new MotionIntegrator(Gravity, Grounded);
            motion.Launch(9f);

            float height = 0f, peak = 0f, airtime = 0f;

            // 第一步即在空中：身体离开的原地面不能取消挑飞。
            do
            {
                height += motion.Step(Vector3.zero, HalfLife, height <= 0f && airtime > 0f, Frame).y * Frame;
                peak = Mathf.Max(peak, height);
                airtime += Frame;
            }
            while (height > 0f && airtime < 5f);

            // v^2 / 2g 与 2v / g，允许一帧积分误差。
            Assert.AreEqual(9f * 9f / (2f * Gravity), peak, 0.1f);
            Assert.AreEqual(2f * 9f / Gravity, airtime, 0.05f);
        }

        [Test]
        public void AWallStopsTheShoveItWasCarrying()
        {
            var motion = new MotionIntegrator(Gravity, Grounded);
            motion.AddImpulse(new Vector3(8f, 0f, 0f));

            motion.StopImpulse();

            Assert.AreEqual(0f, motion.Step(Vector3.zero, HalfLife, true, Frame).x, 0.001f);
        }
    }
}
