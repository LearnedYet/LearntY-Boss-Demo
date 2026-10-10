using System.Linq;
using AdaptiveBossArena.Combat.Feel;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 断言巨兽徒手的最终形态能播放持武器形态所能播放的全部动作。
    /// </summary>
    /// <remarks>
    /// 狂暴形态是在战斗中途为同一骨架替换的第二个控制器，驱动它的各部分并不知道当前是哪一个：
    /// 桥接器始终按名称请求状态。因此狂暴控制器缺失状态不会导致编译或生成失败，
    /// 只会让首领在战斗最后四分之一段原地站立，而这恰是它最不能停下的时刻。
    /// </remarks>
    [TestFixture]
    public sealed class FrenzyFormTests
    {
        private const string BossAnimationConfig =
            "Assets/_Project/ScriptableObjects/Config/DefaultBossAnimation.asset";

        [Test]
        public void TheFrenzyCanPlayEveryStateTheArmedFormCan()
        {
            var config = AssetDatabase.LoadAssetAtPath<CharacterAnimationConfig>(BossAnimationConfig);
            Assert.IsNotNull(config, "The boss animation config is missing.");

            var armed = config.AnimatorController as AnimatorController;
            var frenzied = config.FrenzyController as AnimatorController;

            // 未包含授权美术的检出中不存在该资源，此时巨兽也没有第二形态。
            Assume.That(armed != null && frenzied != null, "The brute is not rigged in this checkout.");

            string[] armedStates = StateNames(armed);
            string[] frenziedStates = StateNames(frenzied);

            Assert.IsNotEmpty(armedStates, "The armed controller has no states.");

            CollectionAssert.IsSubsetOf(
                armedStates, frenziedStates,
                "The frenzy cannot play every state the armed brute can, so it would stand still on those moves.");
        }

        [Test]
        public void TheFrenzyIsADifferentBodyRatherThanTheSameOneRenamed()
        {
            var config = AssetDatabase.LoadAssetAtPath<CharacterAnimationConfig>(BossAnimationConfig);
            var armed = config != null ? config.AnimatorController as AnimatorController : null;
            var frenzied = config != null ? config.FrenzyController as AnimatorController : null;

            Assume.That(armed != null && frenzied != null, "The brute is not rigged in this checkout.");
            Assert.AreNotSame(armed, frenzied, "The frenzy is the armed controller, so nothing changes at the swap.");

            // 最后阶段的意义就在于：它不再挥动剑。
            Assert.AreNotEqual(
                MotionName(armed, "Heavy"), MotionName(frenzied, "Heavy"),
                "The frenzy's heavy swing is the armed one, so it fights the same way bare-handed.");
        }

        private static string[] StateNames(AnimatorController controller) =>
            controller.layers[0].stateMachine.states.Select(child => child.state.name).ToArray();

        private static string MotionName(AnimatorController controller, string state) =>
            controller.layers[0].stateMachine.states
                .Where(child => child.state.name == state)
                .Select(child => child.state.motion != null ? child.state.motion.name : null)
                .FirstOrDefault();
    }
}
