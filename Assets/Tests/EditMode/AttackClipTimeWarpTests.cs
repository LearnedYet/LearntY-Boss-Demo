using AdaptiveBossArena.Combat.Feel;
using NUnit.Framework;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试无论片段与攻击时长如何，动画打击都发生在命中框有效期间。
    /// </summary>
    [TestFixture]
    public sealed class AttackClipTimeWarpTests
    {
        // 轻斩：0.09 秒前摇、0.07 秒有效、0.2 秒后摇；片段长 1.1 秒，在 0.45 秒接触。
        private static AttackClipTimeWarp Light() => new AttackClipTimeWarp(0.09f, 0.07f, 0.2f, 1.1f, 0.45f, 0.1f);

        // 同一片段的重攻击，前摇和后摇长得多。
        private static AttackClipTimeWarp Heavy() => new AttackClipTimeWarp(0.34f, 0.10f, 0.52f, 1.1f, 0.45f, 0.1f);

        [Test]
        public void TheBladeLandsInsideTheActiveWindow()
        {
            AttackClipTimeWarp warp = Light();
            float midActive = warp.ClipTimeAt(0.09f + 0.035f);

            Assert.GreaterOrEqual(midActive, warp.ContactStart);
            Assert.LessOrEqual(midActive, warp.ContactEnd);
            Assert.AreEqual(0.45f, midActive, 0.01f, "The middle of the live window is not the contact frame.");
        }

        [Test]
        public void TheWindUpNeverShowsTheContact()
        {
            AttackClipTimeWarp warp = Light();

            Assert.Less(warp.ClipTimeAt(0.089f), warp.ContactStart + 0.001f);
        }

        [Test]
        public void ClipTimeNeverRunsBackwards()
        {
            // 挥击途中姿势向后跳会像卡顿。
            AttackClipTimeWarp warp = Heavy();
            float previous = -1f;

            for (float t = 0f; t <= 1f; t += 0.005f)
            {
                float clip = warp.ClipTimeAt(t);
                Assert.GreaterOrEqual(clip, previous, "Clip time ran backwards at " + t);
                previous = clip;
            }
        }

        [Test]
        public void OneClipServesAQuickSwingAndASlowOneAlike()
        {
            Assert.AreEqual(0.45f, Light().ClipTimeAt(0.09f + 0.035f), 0.01f);
            Assert.AreEqual(0.45f, Heavy().ClipTimeAt(0.34f + 0.05f), 0.01f);
        }

        [Test]
        public void TheSwingEndsOnTheClipsLastFrame()
        {
            AttackClipTimeWarp warp = Light();

            Assert.AreEqual(1.1f, warp.ClipTimeAt(10f), 0.001f);
            Assert.AreEqual(1f, warp.NormalizedTimeAt(10f), 0.001f);
        }
    }
}
