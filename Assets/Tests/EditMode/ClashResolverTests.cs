using AdaptiveBossArena.Combat;
using NUnit.Framework;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试决定两次挥击相碰而非分别命中目标的规则。
    /// </summary>
    /// <remarks>
    /// 拼刀拒绝原本会命中的两击，每项测试都是公平契约：过宽会剥夺已赢得的命中，
    /// 过窄则同时攻击仍成为伤害竞赛。
    /// </remarks>
    [TestFixture]
    public sealed class ClashResolverTests
    {
        [Test]
        public void TwoSwingsMeetingHeadOnClash()
        {
            Assert.IsTrue(ClashResolver.ShouldClash(
                firstIsSwinging: true, secondIsSwinging: true,
                separation: 2f, combinedReach: 5f, facingDot: 1f));
        }

        [Test]
        public void OneSwingAloneIsAHitNotAClash()
        {
            Assert.IsFalse(ClashResolver.ShouldClash(
                firstIsSwinging: true, secondIsSwinging: false,
                separation: 2f, combinedReach: 5f, facingDot: 1f));

            Assert.IsFalse(ClashResolver.ShouldClash(
                firstIsSwinging: false, secondIsSwinging: true,
                separation: 2f, combinedReach: 5f, facingDot: 1f));
        }

        [Test]
        public void SwingsWhoseRangesMerelyOverlapDoNotClash()
        {
            // 两者范围在某处交叉，但角色并不同位。未真正相遇的刀刃不应互相拒绝命中。
            Assert.IsFalse(ClashResolver.ShouldClash(
                firstIsSwinging: true, secondIsSwinging: true,
                separation: 4f, combinedReach: 6f, facingDot: 1f));
        }

        [Test]
        public void AFighterStruckFromTheSideIsNotClashing()
        {
            // 被绕侧抓到破绽不等于正面对拼；判拼刀会夺走攻击者通过侧绕赢得的命中。
            Assert.IsFalse(ClashResolver.ShouldClash(
                firstIsSwinging: true, secondIsSwinging: true,
                separation: 1f, combinedReach: 5f, facingDot: 0.2f));
        }

        [Test]
        public void FacingIsTheWorseOfTheTwoFighters()
        {
            // 一方面向另一方，另一方背离。取双方较小朝向值，确保一方未注意就足以判为命中。
            float dot = ClashResolver.FacingDot(
                Vector3.zero, Vector3.forward,
                Vector3.forward * 2f, Vector3.forward);

            Assert.Less(dot, 0f, "A fighter facing away should drag the pair's alignment negative.");
        }

        [Test]
        public void TwoFightersLookingAtEachOtherAreFullyAligned()
        {
            float dot = ClashResolver.FacingDot(
                Vector3.zero, Vector3.forward,
                Vector3.forward * 2f, Vector3.back);

            Assert.AreEqual(1f, dot, 0.001f);
        }

        [Test]
        public void HeightDoesNotChangeWhoIsFacingWhom()
        {
            // 在水平面测量，与攻击弧一致。角色站得更高不意味着更不正对。
            float level = ClashResolver.FacingDot(
                Vector3.zero, Vector3.forward,
                Vector3.forward * 2f, Vector3.back);

            float raised = ClashResolver.FacingDot(
                Vector3.zero, Vector3.forward,
                new Vector3(0f, 1.5f, 2f), Vector3.back);

            Assert.AreEqual(level, raised, 0.001f);
        }
    }
}
