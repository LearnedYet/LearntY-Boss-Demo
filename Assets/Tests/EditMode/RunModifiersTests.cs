using AdaptiveBossArena.Core.Services;
using NUnit.Framework;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试每项挑战修正映射至战斗实际应用的数值。
    /// </summary>
    /// <remarks>
    /// 修正的意义就在于它们使用现有系统已遵守的数值，而非新规则。断言固定映射，
    /// 避免重命名标志或改变倍率悄然使挑战失效。
    /// </remarks>
    [TestFixture]
    public sealed class RunModifiersTests
    {
        [Test]
        public void AnUnmodifiedRunLeavesEveryNumberNeutral()
        {
            var modifiers = new RunModifiers();

            Assert.AreEqual(1f, modifiers.AdaptationRateScale);
            Assert.AreEqual(1f, modifiers.IncomingDamageMultiplier);
            Assert.IsTrue(modifiers.HealingEnabled);
            Assert.IsFalse(modifiers.AnyActive);
            Assert.IsEmpty(modifiers.ActiveLabels());
        }

        [Test]
        public void FastLearnerSpeedsAdaptationAndNothingElse()
        {
            var modifiers = new RunModifiers { FastLearner = true };

            Assert.Greater(modifiers.AdaptationRateScale, 1f);
            Assert.AreEqual(1f, modifiers.IncomingDamageMultiplier);
            Assert.IsTrue(modifiers.HealingEnabled);
        }

        [Test]
        public void NoHealingDisablesHealing()
        {
            Assert.IsFalse(new RunModifiers { NoHealing = true }.HealingEnabled);
        }

        [Test]
        public void FragileRaisesIncomingDamage()
        {
            Assert.Greater(new RunModifiers { FragilePlayer = true }.IncomingDamageMultiplier, 1f);
        }

        [Test]
        public void ActiveLabelsListEveryChosenModifier()
        {
            var modifiers = new RunModifiers
            {
                FastLearner = true,
                NoHealing = true,
                FragilePlayer = true,
                TrainingMode = true
            };

            Assert.IsTrue(modifiers.AnyActive);
            Assert.AreEqual(4, modifiers.ActiveLabels().Count);
            CollectionAssert.Contains(modifiers.ActiveLabels(), "训练模式");
        }

        [Test]
        public void AnOrdinaryRunCountsTowardRecords()
        {
            Assert.IsTrue(new RunModifiers().CountsTowardRecords);
        }

        [Test]
        public void TrainingRunsNeverCountTowardRecords()
        {
            // 训练模式使玩家无法被杀死，因此其中的“胜利”不是真正胜利。
            // 记录它会永久设定最快胜利和最低允许适应次数，且无法撤回。
            Assert.IsFalse(new RunModifiers { TrainingMode = true }.CountsTowardRecords);
        }

        [Test]
        public void HarderModifiersStillCountTowardRecords()
        {
            // 这三项只会增加难度，因此启用时的胜利至少与普通胜利同样有价值。
            Assert.IsTrue(new RunModifiers { FastLearner = true }.CountsTowardRecords);
            Assert.IsTrue(new RunModifiers { NoHealing = true }.CountsTowardRecords);
            Assert.IsTrue(new RunModifiers { FragilePlayer = true }.CountsTowardRecords);
        }
    }
}
