using AdaptiveBossArena.Combat.Vitals;
using NUnit.Framework;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试使首领可被推动程度随站姿损耗变化的规则。
    /// </summary>
    [TestFixture]
    public sealed class KnockbackResistanceTests
    {
        private const float AtFull = 0.85f;
        private const float WhenBroken = 0f;

        [Test]
        public void AFreshStanceShrugsOffMostOfABlow()
        {
            float shove = KnockbackResistance.ShoveMultiplier(1f, false, AtFull, WhenBroken);

            Assert.AreEqual(0.15f, shove, 0.001f);
        }

        [Test]
        public void ABrokenStanceTakesTheWholeBlow()
        {
            // 按报告满架势的情况检查，因为破防结束时资源池补满，破防仍生效期间不能将其视为全新的站姿。
            float shove = KnockbackResistance.ShoveMultiplier(1f, true, AtFull, WhenBroken);

            Assert.AreEqual(1f, shove, 0.001f);
        }

        [Test]
        public void WearingTheStanceDownMakesItGiveGroundSteadily()
        {
            float fresh = KnockbackResistance.ShoveMultiplier(1f, false, AtFull, WhenBroken);
            float worn = KnockbackResistance.ShoveMultiplier(0.5f, false, AtFull, WhenBroken);
            float nearlyGone = KnockbackResistance.ShoveMultiplier(0.1f, false, AtFull, WhenBroken);

            Assert.Less(fresh, worn);
            Assert.Less(worn, nearlyGone);
        }

        [Test]
        public void OutOfRangeValuesNeverProduceANegativeOrAmplifiedShove()
        {
            Assert.AreEqual(0f, KnockbackResistance.ShoveMultiplier(5f, false, 2f, 0f), 0.001f);
            Assert.AreEqual(1f, KnockbackResistance.ShoveMultiplier(-3f, false, AtFull, -1f), 0.001f);
        }
    }
}
