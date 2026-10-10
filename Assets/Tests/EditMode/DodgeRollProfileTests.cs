using AdaptiveBossArena.Combat.Movement;
using NUnit.Framework;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试闪避翻滚速度曲线。
    /// </summary>
    [TestFixture]
    public sealed class DodgeRollProfileTests
    {
        private const float Duration = 0.4f;
        private const float Distance = 4.5f;

        [Test]
        public void TheRollCoversItsConfiguredDistance()
        {
            // 首领距离逻辑按闪避位移调节，改曲线形状不得改总距离。
            const float frame = 1f / 240f;
            float travelled = 0f;

            for (float t = 0f; t < Duration; t += frame)
            {
                travelled += DodgeRollProfile.SpeedAt(t + frame * 0.5f, Duration, Distance) * frame;
            }

            Assert.AreEqual(Distance, travelled, 0.05f);
        }

        [Test]
        public void TheRollBurstsOutAndSettles()
        {
            float start = DodgeRollProfile.SpeedAt(0f, Duration, Distance);
            float middle = DodgeRollProfile.SpeedAt(Duration * 0.5f, Duration, Distance);
            float end = DodgeRollProfile.SpeedAt(Duration * 0.95f, Duration, Distance);

            Assert.Greater(start, middle);
            Assert.Greater(middle, end);
        }

        [Test]
        public void MostOfTheDistanceIsCoveredWhileInvincible()
        {
            // 无敌帧在前 0.135 秒。若多数位移发生在其后，就会把玩家送进攻击而非离开。
            const float frame = 1f / 240f;
            float early = 0f;

            for (float t = 0f; t < 0.135f; t += frame)
            {
                early += DodgeRollProfile.SpeedAt(t + frame * 0.5f, Duration, Distance) * frame;
            }

            Assert.Greater(early, Distance * 0.6f);
        }

        [Test]
        public void TheRollHasNoSpeedOutsideItsDuration()
        {
            Assert.AreEqual(0f, DodgeRollProfile.SpeedAt(Duration, Duration, Distance));
            Assert.AreEqual(0f, DodgeRollProfile.SpeedAt(-0.1f, Duration, Distance));
        }
    }
}
