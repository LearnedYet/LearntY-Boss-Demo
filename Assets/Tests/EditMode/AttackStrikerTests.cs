using AdaptiveBossArena.Combat;
using NUnit.Framework;
using UnityEditor;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 断言生成攻击使用动画实际挥动的身体部位打击。
    /// </summary>
    /// <remarks>
    /// 未指定部位会悄然回退为攻击者前方隐形扇区，正是玩家反馈的“没碰到却伤害了我”，其他检查发现不了。
    /// 首版表中的组合标志和非首标志被按枚举位置而非数值写入的工具丢失，只保留普通武器攻击。
    /// </remarks>
    [TestFixture]
    public sealed class AttackStrikerTests
    {
        private const string AttackFolder = "Assets/_Project/ScriptableObjects/Attacks";

        [TestCase("PlayerLight1", StrikerParts.Weapon)]
        [TestCase("PlayerHeavy", StrikerParts.Weapon)]
        [TestCase("PlayerExecution", StrikerParts.Weapon)]
        [TestCase("BossSweep", StrikerParts.Weapon)]
        [TestCase("BossSpinCleave", StrikerParts.Weapon)]
        [TestCase("BossKick", StrikerParts.RightFoot)]
        [TestCase("BossGrab", StrikerParts.RightHand | StrikerParts.LeftHand)]
        [TestCase("BossLeapSmash", StrikerParts.Body)]
        [TestCase("BossCharge", StrikerParts.Weapon | StrikerParts.Body)]
        public void TheAttackStrikesWithThePartItsClipSwings(string attackName, StrikerParts expected)
        {
            var attack = AssetDatabase.LoadAssetAtPath<AttackDefinition>($"{AttackFolder}/{attackName}.asset");

            Assert.IsNotNull(attack, attackName + " was not generated.");
            Assert.AreEqual(expected, attack.Strikers, attackName + " strikes with the wrong part of the body.");
        }

        [TestCase("BossSweep")]
        [TestCase("BossJab")]
        [TestCase("BossPerilousOverhead")]
        [TestCase("BossDelayedOverhead")]
        [TestCase("BossSpinCleave")]
        [TestCase("BossCharge")]
        [TestCase("BossKick")]
        [TestCase("BossGrab")]
        [TestCase("BossLeapSmash")]
        public void TheBossPlansEachBlowFromAReachItsBodyHas(string attackName)
        {
            // 首领按此范围规划每次挥击。直接采样片段保留游戏丢弃的地面位移，曾把滑步攻击规划在六米外，
            // 导致够不到就挥空。此身体巨剑含骑士厚度在内仍不到四米半。
            var attack = AssetDatabase.LoadAssetAtPath<AttackDefinition>($"{AttackFolder}/{attackName}.asset");

            Assert.IsNotNull(attack, attackName + " was not generated.");
            Assert.That(attack.Range, Is.InRange(1f, 4.5f),
                attackName + " is planned from a reach its body does not have.");
        }

        [TestCase("BossSlam")]
        [TestCase("BossShockwave")]
        [TestCase("BossPhaseShockwave")]
        [TestCase("PlayerSpecial")]
        public void AnAreaAttackStaysOnTheAreaDrawnForIt(string attackName)
        {
            var attack = AssetDatabase.LoadAssetAtPath<AttackDefinition>($"{AttackFolder}/{attackName}.asset");

            Assert.IsNotNull(attack, attackName + " was not generated.");
            Assert.AreEqual(StrikerParts.None, attack.Strikers,
                attackName + " is an area attack and should be decided by the area on the floor, not a limb.");
        }
    }
}
