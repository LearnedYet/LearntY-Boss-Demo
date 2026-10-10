using AdaptiveBossArena.Combat;
using NUnit.Framework;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试决定挥击能否触及身体的几何规则。
    /// </summary>
    /// <remarks>
    /// 源于试玩者早已感觉到的故障：视觉上与首领重叠的挥击不命中。原算法测目标中心角并直接对比扇形，
    /// 要求目标轴位于扇区内，忽略目标中心轴到身体近缘之间的部分。
    /// </remarks>
    [TestFixture]
    public sealed class ArcHitTests
    {
        /// <summary>首领受击框半径，是受此故障影响最大的情况。</summary>
        private const float BossRadius = 0.85f;

        /// <summary>玩家受击框半径。</summary>
        private const float PlayerRadius = 0.38f;

        private static Vector3 AtAngle(float degrees, float distance) =>
            Quaternion.Euler(0f, degrees, 0f) * Vector3.forward * distance;

        [Test]
        public void ABodyOverlappingTheWedgeIsHit_EvenWhenItsCentreIsOutside()
        {
            // 准确复现故障：90 度挥击覆盖轴两侧 45 度；中心位于55 度、
            // 2.5 米外的首领仍有多数身体在扇区内，因为此距离上 0.85 米半径向中心两侧各展开约 19 度。
            bool hit = AttackHitDetector.IsWithinArc(
                Vector3.forward, AtAngle(55f, 2.5f), BossRadius, arcDegrees: 90f);

            Assert.IsTrue(hit, "A swing overlapping the boss should connect even if its centre does not.");
        }

        [Test]
        public void ABodyFullyOutsideTheWedgeIsMissed()
        {
            // 偏离足够远，使近缘也不在扇区内。修复不能让每个弧形都变成圆。
            bool hit = AttackHitDetector.IsWithinArc(
                Vector3.forward, AtAngle(85f, 2.5f), BossRadius, arcDegrees: 90f);

            Assert.IsFalse(hit, "A body clear of the wedge should still be missed.");
        }

        [Test]
        public void ATargetBehindTheAttackerIsMissed()
        {
            bool hit = AttackHitDetector.IsWithinArc(
                Vector3.forward, AtAngle(180f, 2.5f), BossRadius, arcDegrees: 120f);

            Assert.IsFalse(hit, "Nothing behind the attacker should ever be hit by a forward swing.");
        }

        [Test]
        public void AWiderBodyIsReachedFromFurtherOffAxis()
        {
            // 修正必须随目标尺寸变化，因此玩家攻击首领时影响更大。在两阈值之间：
            // 该距离下首领 0.85米身体覆盖偏轴约 65 度，玩家 0.38米身体只覆盖约 54 度。
            const float offAxis = 58f;
            const float distance = 2.5f;

            Assert.IsTrue(
                AttackHitDetector.IsWithinArc(
                    Vector3.forward, AtAngle(offAxis, distance), BossRadius, 90f),
                "The wide body should be reachable at this angle.");

            Assert.IsFalse(
                AttackHitDetector.IsWithinArc(
                    Vector3.forward, AtAngle(offAxis, distance), PlayerRadius, 90f),
                "The narrow body should not be, at the same angle and range.");
        }

        [Test]
        public void TheSameBodyIsHarderToReachFromFurtherAway()
        {
            // 身体越远，占据角度越小。近处能擦到边缘的挥击，远处应错过同一边缘。
            Assert.IsTrue(
                AttackHitDetector.IsWithinArc(Vector3.forward, AtAngle(60f, 1.2f), BossRadius, 90f));

            Assert.IsFalse(
                AttackHitDetector.IsWithinArc(Vector3.forward, AtAngle(60f, 6f), BossRadius, 90f));
        }

        [Test]
        public void APointBlankTargetIsAlwaysHit()
        {
            // 没有有意义的方向可拒绝，零距离未命中会像错误。
            Assert.IsTrue(
                AttackHitDetector.IsWithinArc(Vector3.forward, Vector3.zero, BossRadius, 40f));

            // 身体半径大于自身距离时包围攻击者，窄挥击也应命中贴身对象，与朝向无关。
            Assert.IsTrue(
                AttackHitDetector.IsWithinArc(
                    Vector3.forward, AtAngle(170f, 0.4f), BossRadius, 40f));
        }

        [Test]
        public void AZeroWidthTargetFallsBackToTheCentreTest()
        {
            // 宽度为零时必须完全保持旧行为，证明改动是扩展而非放松判定。
            Assert.IsTrue(
                AttackHitDetector.IsWithinArc(Vector3.forward, AtAngle(44f, 2.5f), 0f, 90f));

            Assert.IsFalse(
                AttackHitDetector.IsWithinArc(Vector3.forward, AtAngle(46f, 2.5f), 0f, 90f));
        }

        [Test]
        public void ANegativeRadiusIsTreatedAsNoWidth()
        {
            Assert.IsFalse(
                AttackHitDetector.IsWithinArc(Vector3.forward, AtAngle(46f, 2.5f), -1f, 90f));
        }
    }
}
