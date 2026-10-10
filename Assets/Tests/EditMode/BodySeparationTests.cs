using AdaptiveBossArena.Combat.Movement;
using NUnit.Framework;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试两名重叠角色如何被推开。
    /// </summary>
    [TestFixture]
    public sealed class BodySeparationTests
    {
        private const float KnightRadius = 0.4f;
        private const float BruteRadius = 0.9f;
        private const float KnightMass = 1f;
        private const float BruteMass = 4f;

        [Test]
        public void BodiesThatDoNotTouchAreLeftAlone()
        {
            BodySeparation.Result result = BodySeparation.Resolve(
                Vector3.zero, KnightRadius, KnightMass,
                new Vector3(3f, 0f, 0f), BruteRadius, BruteMass,
                Vector3.zero);

            Assert.IsFalse(result.Overlapped);
        }

        [Test]
        public void OverlappingBodiesEndExactlyInContact()
        {
            Vector3 knight = Vector3.zero;
            Vector3 brute = new Vector3(1f, 0f, 0f);

            BodySeparation.Result result = BodySeparation.Resolve(
                knight, KnightRadius, KnightMass, brute, BruteRadius, BruteMass, Vector3.zero);

            float after = Vector3.Distance(knight + result.PushFirst, brute + result.PushSecond);

            Assert.IsTrue(result.Overlapped);
            Assert.AreEqual(KnightRadius + BruteRadius, after, 0.001f);
        }

        [Test]
        public void TheHeavierBodyGivesLessGround()
        {
            // 四倍质量应只移动四分之一距离：顶巨兽只轻推它，却大幅推回骑士。
            BodySeparation.Result result = BodySeparation.Resolve(
                Vector3.zero, KnightRadius, KnightMass,
                new Vector3(1f, 0f, 0f), BruteRadius, BruteMass,
                Vector3.zero);

            Assert.AreEqual(4f, result.PushFirst.magnitude / result.PushSecond.magnitude, 0.01f);
        }

        [Test]
        public void BodiesThatPassedThroughEachOtherArePushedBackToTheSideTheyCameFrom()
        {
            // 上一帧巨兽在骑士右侧，长帧使骑士中心越过巨兽中心。按新方向分离会从远侧推出骑士，
            // 让它穿过巨兽。
            Vector3 knight = new Vector3(0.2f, 0f, 0f);
            Vector3 brute = Vector3.zero;

            BodySeparation.Result result = BodySeparation.Resolve(
                knight, KnightRadius, KnightMass, brute, BruteRadius, BruteMass,
                previousDirection: Vector3.right);

            Vector3 knightAfter = knight + result.PushFirst;
            Vector3 bruteAfter = brute + result.PushSecond;

            Assert.Less(knightAfter.x, bruteAfter.x, "The knight was pushed out of the far side of the brute.");
            Assert.AreEqual(KnightRadius + BruteRadius, bruteAfter.x - knightAfter.x, 0.001f);
        }

        [Test]
        public void BodiesMovedFarApartDeliberatelyAreNotYankedBack()
        {
            // 重试会传送双方，此时与上一帧相对位置颠倒属于正常，不应视为互相穿过。
            BodySeparation.Result result = BodySeparation.Resolve(
                new Vector3(10f, 0f, 0f), KnightRadius, KnightMass,
                Vector3.zero, BruteRadius, BruteMass,
                previousDirection: Vector3.right);

            Assert.IsFalse(result.Overlapped);
        }

        [Test]
        public void ExactlyCoincidentBodiesStillSeparate()
        {
            BodySeparation.Result result = BodySeparation.Resolve(
                Vector3.zero, KnightRadius, KnightMass, Vector3.zero, BruteRadius, BruteMass, Vector3.zero);

            Assert.IsTrue(result.Overlapped);
            Assert.Greater((result.PushSecond - result.PushFirst).magnitude, KnightRadius);
        }

        [Test]
        public void HeightDifferencesDoNotCreateOrHideOverlap()
        {
            BodySeparation.Result result = BodySeparation.Resolve(
                Vector3.zero, KnightRadius, KnightMass,
                new Vector3(1f, 5f, 0f), BruteRadius, BruteMass,
                Vector3.zero);

            Assert.IsTrue(result.Overlapped);
            Assert.AreEqual(0f, result.PushFirst.y, 0.0001f);
            Assert.AreEqual(0f, result.PushSecond.y, 0.0001f);
        }
    }
}
