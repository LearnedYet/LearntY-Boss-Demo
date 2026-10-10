using AdaptiveBossArena.Learning;
using NUnit.Framework;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试已投入的首领挥击落空后，何时会暴露破绽。
    /// </summary>
    /// <remarks>
    /// 评估器为纯逻辑，因此整个“反向适应”的回报在此验证，无需凭感觉判断。最关键的断言是休眠：
    /// 投入为零时首领绝不能失衡，因为完全未学到内容的战斗必须与该机制出现前完全一致。
    /// </remarks>
    [TestFixture]
    public sealed class OverbalanceTests
    {
        /// <summary>代表性评估器；各测试改变投入程度与随机结果。</summary>
        private static OverbalanceEvaluator Evaluator() =>
            new OverbalanceEvaluator(
                maxChance: 0.6f, intensityThreshold: 0.15f,
                recoverySecondsAtFull: 1.2f, poiseMultiplierAtFull: 2.5f);

        [Test]
        public void AtZeroCommitment_NeverOverbalances()
        {
            OverbalanceEvaluator evaluator = Evaluator();

            // 随机结果为 0 是最有利的可能抽取结果；即使如此，未经适应的首领也不能踉跄。
            Assert.IsFalse(evaluator.ShouldOverbalance(wasCommittedAttack: true, commitmentIntensity: 0f, roll: 0f));
            Assert.AreEqual(0f, evaluator.Chance(0f), 0.0001f);
        }

        [Test]
        public void BelowTheThreshold_NeverOverbalances()
        {
            OverbalanceEvaluator evaluator = Evaluator();

            // 轻度适应位于死区，因此低投入不会暴露首领。
            Assert.AreEqual(0f, evaluator.Chance(0.1f), 0.0001f);
            Assert.IsFalse(evaluator.ShouldOverbalance(true, 0.1f, 0f));
        }

        [Test]
        public void AnUncommittedSwing_NeverOverbalances()
        {
            OverbalanceEvaluator evaluator = Evaluator();

            // 无论首领投入程度多高，轻刺都不会留下值得惩罚的破绽。
            Assert.IsFalse(evaluator.ShouldOverbalance(wasCommittedAttack: false, commitmentIntensity: 1f, roll: 0f));
        }

        [Test]
        public void AFullyCommittedWhiff_CanOverbalance()
        {
            OverbalanceEvaluator evaluator = Evaluator();

            // 满投入时概率达到最大值，因此低于该概率的随机结果会使首领踉跄。
            Assert.AreEqual(0.6f, evaluator.Chance(1f), 0.0001f);
            Assert.IsTrue(evaluator.ShouldOverbalance(true, 1f, roll: 0.5f));
            Assert.IsFalse(evaluator.ShouldOverbalance(true, 1f, roll: 0.7f));
        }

        [Test]
        public void ChanceRisesWithCommitment()
        {
            OverbalanceEvaluator evaluator = Evaluator();

            Assert.Greater(evaluator.Chance(1f), evaluator.Chance(0.5f));
            Assert.Greater(evaluator.Chance(0.5f), evaluator.Chance(0.2f));
        }

        [Test]
        public void TheStumbleAndVulnerabilityGrowWithCommitment()
        {
            OverbalanceEvaluator evaluator = Evaluator();

            Assert.Greater(evaluator.ExtraRecoverySeconds(1f), evaluator.ExtraRecoverySeconds(0.3f));
            Assert.Greater(evaluator.PoiseVulnerabilityMultiplier(1f), evaluator.PoiseVulnerabilityMultiplier(0.3f));

            // 倍率只会增加首领脆弱程度，绝不会降低。
            Assert.GreaterOrEqual(evaluator.PoiseVulnerabilityMultiplier(0f), 1f);
            Assert.AreEqual(2.5f, evaluator.PoiseVulnerabilityMultiplier(1f), 0.0001f);
        }

        [Test]
        public void ATriggeredStumble_IsLongEnoughToPunish()
        {
            OverbalanceEvaluator evaluator = Evaluator();

            // 即使刚好达到阈值边缘，窗口也应占完整踉跄时间的可用比例，而非短到玩家根本无法反应的一闪。
            Assert.Greater(evaluator.ExtraRecoverySeconds(0.16f), 0f);
            Assert.GreaterOrEqual(evaluator.ExtraRecoverySeconds(1f), evaluator.ExtraRecoverySeconds(0.16f));
        }
    }
}
