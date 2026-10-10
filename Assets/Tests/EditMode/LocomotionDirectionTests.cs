using AdaptiveBossArena.Combat.Feel;
using NUnit.Framework;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试方向移动混合如何读取相对于朝向的移动。
    /// </summary>
    /// <remarks>
    /// 原先单一速度驱动只向前迈步的动画，因此侧向和后退移动时双腿仍直向前走，身体却滑动。
    /// </remarks>
    [TestFixture]
    public sealed class LocomotionDirectionTests
    {
        [Test]
        public void MovingTheWayYouFaceIsForward()
        {
            Vector2 blend = LocomotionDirection.BlendPosition(new Vector3(0f, 0f, 0.1f), Quaternion.identity, 1f, Vector2.zero);

            Assert.AreEqual(0f, blend.x, 0.001f);
            Assert.AreEqual(1f, blend.y, 0.001f);
        }

        [Test]
        public void MovingToYourRightWhileFacingEastIsAStrafeRight()
        {
            // 面向东（+x）、向南（-z）移动，相对于朝向是向右移动。
            Quaternion east = Quaternion.LookRotation(Vector3.right);
            Vector2 blend = LocomotionDirection.BlendPosition(new Vector3(0f, 0f, -0.1f), east, 0.3f, Vector2.zero);

            Assert.AreEqual(0.3f, blend.x, 0.001f);
            Assert.AreEqual(0f, blend.y, 0.001f);
        }

        [Test]
        public void BackingAwayIsBackwards()
        {
            Vector2 blend = LocomotionDirection.BlendPosition(new Vector3(0f, 0f, -0.1f), Quaternion.identity, 0.5f, Vector2.zero);

            Assert.AreEqual(-0.5f, blend.y, 0.001f);
        }

        [Test]
        public void AFrozenFrameKeepsTheLastDirection()
        {
            Vector2 blend = LocomotionDirection.BlendPosition(Vector3.zero, Quaternion.identity, 0.4f, new Vector2(-1f, 0f));

            Assert.AreEqual(-0.4f, blend.x, 0.001f);
            Assert.AreEqual(0f, blend.y, 0.001f);
        }

        [Test]
        public void VerticalMovementIsIgnored()
        {
            Vector2 blend = LocomotionDirection.BlendPosition(new Vector3(0.1f, 5f, 0f), Quaternion.identity, 1f, Vector2.zero);

            Assert.AreEqual(1f, blend.x, 0.001f);
        }
    }
}
