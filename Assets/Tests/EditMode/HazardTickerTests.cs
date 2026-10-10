using AdaptiveBossArena.Combat;
using NUnit.Framework;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试地面危险区域的两条公平规则：伤害前先预警，并按固定节奏而非逐帧造成伤害。
    /// </summary>
    [TestFixture]
    public sealed class HazardTickerTests
    {
        private const float Step = 0.02f;

        private static HazardTicker Ticker() =>
            new HazardTicker(warningSeconds: 0.5f, activeSeconds: 2f, fadeSeconds: 0.5f, damageIntervalSeconds: 0.4f);

        [Test]
        public void AHazardWarnsBeforeItHurts()
        {
            HazardTicker ticker = Ticker();
            Assert.AreEqual(HazardTicker.Stage.Warning, ticker.CurrentStage);

            // 24 步等于 0.48 秒，仍在 0.5 秒预警期内，不能触发伤害。
            bool damaged = false;
            for (int i = 0; i < 24; i++)
            {
                damaged |= ticker.Advance(Step);
            }

            Assert.IsFalse(damaged, "The warning window must never deal damage.");
        }

        [Test]
        public void ItBitesPromptlyOnceActive()
        {
            HazardTicker ticker = Ticker();

            float firstDamageAt = -1f;
            float t = 0f;
            for (int i = 0; i < 200 && firstDamageAt < 0f; i++)
            {
                t += Step;
                if (ticker.Advance(Step))
                {
                    firstDamageAt = t;
                }
            }

            // 第一次伤害应紧随 0.5 秒预警结束发生，而非进入生效期后再等完整间隔：
            // 站立玩家脚下形成的区域会立即伤害他。
            Assert.GreaterOrEqual(firstDamageAt, 0.5f);
            Assert.Less(firstDamageAt, 0.58f);
        }

        [Test]
        public void DamageTicksArePacedByTheInterval()
        {
            HazardTicker ticker = Ticker();

            int ticks = 0;
            for (int i = 0; i < 200; i++) // 4 秒，已远超 3 秒总存活时间。
            {
                if (ticker.Advance(Step))
                {
                    ticks++;
                }
            }

            // 生效 2 秒、间隔 0.4 秒约产生五次周期伤害，加上进入时立即发生的一次；只检查边界范围，
            // 不检查精确次数，避免相差一帧的边界影响规则成立。
            Assert.GreaterOrEqual(ticks, 5);
            Assert.LessOrEqual(ticks, 7);
        }

        [Test]
        public void AHazardEventuallyExpires()
        {
            HazardTicker ticker = Ticker();

            for (int i = 0; i < 200; i++)
            {
                ticker.Advance(Step);
            }

            Assert.IsTrue(ticker.IsExpired);
            Assert.IsFalse(ticker.Advance(Step), "An expired hazard deals no further damage.");
        }
    }
}
