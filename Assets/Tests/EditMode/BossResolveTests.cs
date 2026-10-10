using AdaptiveBossArena.Learning;
using NUnit.Framework;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试首领 Resolve：随时间和被闪避累积，使用后清空。
    /// </summary>
    /// <remarks>
    /// 公平关键是 Resolve 仅由首领确实观察到的事件累积；善于闪避的玩家使其更快增长。豪赌由战况赢得，
    /// 不是无偿给首领。
    /// </remarks>
    [TestFixture]
    public sealed class BossResolveTests
    {
        private static BossResolve Resolve() =>
            new BossResolve(threshold: 100f, buildPerSecond: 5f, buildPerWhiff: 20f);

        [Test]
        public void AMeterStartsEmptyAndNotReady()
        {
            BossResolve resolve = Resolve();
            Assert.AreEqual(0f, resolve.Normalized);
            Assert.IsFalse(resolve.IsReady);
        }

        [Test]
        public void TimeFillsIt()
        {
            BossResolve resolve = Resolve();
            for (int i = 0; i < 200; i++) // 200 * 0.1 秒 = 20 秒，以每秒 5 点计为 100
            {
                resolve.Tick(0.1f);
            }

            Assert.IsTrue(resolve.IsReady);
        }

        [Test]
        public void BeingDodgedFillsItFaster()
        {
            BossResolve resolve = Resolve();
            for (int i = 0; i < 5; i++)
            {
                resolve.RegisterBossWhiff(); // 5 * 20 = 100
            }

            Assert.IsTrue(resolve.IsReady, "Five whiffed swings should be enough to wind up a gambit.");
        }

        [Test]
        public void ItNeverExceedsFull()
        {
            BossResolve resolve = Resolve();
            for (int i = 0; i < 20; i++)
            {
                resolve.RegisterBossWhiff();
            }

            Assert.AreEqual(1f, resolve.Normalized, 0.001f);
        }

        [Test]
        public void SpendingEmptiesIt()
        {
            BossResolve resolve = Resolve();
            for (int i = 0; i < 5; i++)
            {
                resolve.RegisterBossWhiff();
            }

            resolve.Spend();

            Assert.IsFalse(resolve.IsReady);
            Assert.AreEqual(0f, resolve.Normalized);
        }
    }
}
