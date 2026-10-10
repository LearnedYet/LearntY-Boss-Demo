using AdaptiveBossArena.Combat.Vitals;
using NUnit.Framework;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试以强化特殊攻击奖励良好防御的专注条。
    /// </summary>
    /// <remarks>
    /// 重要契约：专注靠赢得而非赠送，真正满条才强化，硬直会清空。三条规则使它值得风险管理，
    /// 而非自动增长的条。
    /// </remarks>
    [TestFixture]
    public sealed class FocusMeterTests
    {
        private static FocusMeter Meter() => new FocusMeter(maximum: 100f, gainPerDeflect: 40f, gainPerPerfectDodge: 30f);

        [Test]
        public void AMeterStartsEmpty()
        {
            Assert.AreEqual(0f, Meter().Current);
            Assert.IsFalse(Meter().IsFull);
        }

        [Test]
        public void DefendingWellFillsIt()
        {
            FocusMeter meter = Meter();
            meter.AddFromDeflect();
            meter.AddFromPerfectDodge();

            Assert.AreEqual(70f, meter.Current, 0.001f);
            Assert.IsFalse(meter.IsFull);
        }

        [Test]
        public void FocusNeverExceedsTheMaximum()
        {
            FocusMeter meter = Meter();
            for (int i = 0; i < 10; i++)
            {
                meter.AddFromDeflect();
            }

            Assert.AreEqual(100f, meter.Current, 0.001f);
            Assert.IsTrue(meter.IsFull);
        }

        [Test]
        public void ASpecialCanOnlyBeEmpoweredWhenTheMeterIsFull()
        {
            FocusMeter meter = Meter();
            meter.AddFromDeflect();

            Assert.IsFalse(meter.TryConsumeFull(), "A partial meter must not empower.");

            meter.AddFromDeflect();
            meter.AddFromDeflect(); // 现在已满

            Assert.IsTrue(meter.TryConsumeFull(), "A full meter should empower and empty.");
            Assert.AreEqual(0f, meter.Current);
        }

        [Test]
        public void AStaggerTakesAllOfIt()
        {
            FocusMeter meter = Meter();
            meter.AddFromDeflect();
            meter.AddFromDeflect();

            meter.Reset();

            Assert.AreEqual(0f, meter.Current);
        }
    }
}
