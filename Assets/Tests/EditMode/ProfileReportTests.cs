using System.Collections.Generic;
using AdaptiveBossArena.Learning;
using NUnit.Framework;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试战后档案向玩家报告哪些习惯的规则。
    /// </summary>
    /// <remarks>
    /// 档案是整个“它研究过你”机制的回报，因此只能报告首领确实依赖过的习惯：强且证据充足，
    /// 并按强度降序排列。对某习惯 <em>缺失</em> 的高置信度判断（“你几乎从未挥空”）不能伪装成首领从玩家
    /// 身上读出的习惯。
    /// </remarks>
    [TestFixture]
    public sealed class ProfileReportTests
    {
        // 大量样本应表现为确信，少量样本则不确定，两者均明确远离最低阈值。
        private const int Confident = 100;
        private const int Unsure = 1;
        private const int HalfConfidence = 10;

        private static BehaviorProfile ProfileWith(params (BehaviorFeature feature, float value, int samples)[] set)
        {
            var profile = new BehaviorProfile();
            foreach ((BehaviorFeature feature, float value, int samples) in set)
            {
                profile.Set(feature, value, samples, HalfConfidence);
            }

            return profile;
        }

        [Test]
        public void AnUnreadPlayerProducesNoLines()
        {
            Assert.IsEmpty(ProfileReport.HabitLines(new BehaviorProfile()));
        }

        [Test]
        public void OnlyStrongWellEvidencedHabitsAreReported()
        {
            BehaviorProfile profile = ProfileWith(
                (BehaviorFeature.HeavyAttackRatio, 0.85f, Confident),   // 强且确信 -> 纳入
                (BehaviorFeature.WhiffRatio, 0.05f, Confident),         // 确信但不存在 -> 排除
                (BehaviorFeature.PreferredDistance, 0.9f, Unsure));     // 强但只是猜测 -> 排除

            IReadOnlyList<string> lines = ProfileReport.HabitLines(profile);

            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains("重攻击", lines[0]);
        }

        [Test]
        public void HabitsAreOrderedByConfidenceWeightedStrength()
        {
            // 两者均符合条件；重击的置信度加权值更高，必须排在前面。
            BehaviorProfile profile = ProfileWith(
                (BehaviorFeature.HeavyAttackRatio, 0.95f, Confident),
                (BehaviorFeature.PreferredDistance, 0.5f, Confident));

            IReadOnlyList<string> lines = ProfileReport.HabitLines(profile);

            Assert.AreEqual(2, lines.Count);
            StringAssert.Contains("重攻击", lines[0]);
            StringAssert.Contains("保持距离", lines[1]);
        }

        [Test]
        public void EachReportedHabitCarriesAConfidencePercentage()
        {
            BehaviorProfile profile = ProfileWith((BehaviorFeature.EvasionSuccess, 0.9f, Confident));

            IReadOnlyList<string> lines = ProfileReport.HabitLines(profile);

            Assert.AreEqual(1, lines.Count);
            StringAssert.IsMatch(@"可信度 \d+%", lines[0]);
        }
    }
}
