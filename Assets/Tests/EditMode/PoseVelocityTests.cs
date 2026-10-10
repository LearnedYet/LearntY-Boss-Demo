using AdaptiveBossArena.Combat.Feel;
using NUnit.Framework;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试布娃娃各刚体继承原动画速度的规则。
    /// </summary>
    /// <remarks>
    /// 刚体原先从静止开始，因此挥击中被杀死的战斗者会突然停止，并垂直落下。
    /// </remarks>
    [TestFixture]
    public sealed class PoseVelocityTests
    {
        [Test]
        public void ABoneMovingHalfAMetreInATenthOfASecondIsMovingAtFiveMetresASecond()
        {
            Vector3 velocity = PoseVelocity.Linear(Vector3.zero, new Vector3(0.5f, 0f, 0f), 0.1f);

            Assert.AreEqual(5f, velocity.x, 0.0001f);
        }

        [Test]
        public void AFrozenFrameGivesNoVelocityRatherThanInfinity()
        {
            Assert.AreEqual(Vector3.zero, PoseVelocity.Linear(Vector3.zero, Vector3.one, 0f));
            Assert.AreEqual(Vector3.zero, PoseVelocity.Angular(Quaternion.identity, Quaternion.Euler(0f, 90f, 0f), 0f));
        }

        [Test]
        public void APoppedBoneCannotFlingItsBody()
        {
            Vector3 velocity = PoseVelocity.Linear(Vector3.zero, new Vector3(3f, 0f, 0f), 0.016f);

            Assert.AreEqual(PoseVelocity.MaximumLinearSpeed, velocity.magnitude, 0.001f);
        }

        [Test]
        public void AQuarterTurnInASecondSpinsAtHalfPiAboutItsAxis()
        {
            Vector3 spin = PoseVelocity.Angular(Quaternion.identity, Quaternion.Euler(0f, 90f, 0f), 1f);

            Assert.AreEqual(Mathf.PI * 0.5f, spin.y, 0.001f);
            Assert.AreEqual(0f, spin.x, 0.001f);
        }

        [Test]
        public void ASmallTurnBackIsTakenTheShortWayRound()
        {
            Vector3 spin = PoseVelocity.Angular(Quaternion.Euler(0f, 10f, 0f), Quaternion.identity, 1f);

            Assert.AreEqual(-10f * Mathf.Deg2Rad, spin.y, 0.001f);
        }
    }
}
