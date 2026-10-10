using AdaptiveBossArena.Combat;
using NUnit.Framework;
using UnityEditor;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 固定哪些攻击仍在地面绘制预警。
    /// </summary>
    /// <remarks>
    /// 已有身体动画时，普通挥击可从首领自身动作读出，每次攻击下方都放扇形会教玩家只看地面。
    /// 只有打击地面或无法防御的攻击保留地面预警，因为其区域或规则无法仅由身体动作表达。
    /// </remarks>
    [TestFixture]
    public sealed class TelegraphAssetTests
    {
        private static AttackDefinition Load(string name) =>
            AssetDatabase.LoadAssetAtPath<AttackDefinition>("Assets/_Project/ScriptableObjects/Attacks/" + name + ".asset");

        [TestCase("BossSlam")]
        [TestCase("BossShockwave")]
        [TestCase("BossPhaseShockwave")]
        [TestCase("BossPerilousOverhead")]
        public void GroundStrikesAndUnblockablesKeepTheirWarning(string attack)
        {
            Assert.IsTrue(Load(attack).ShowTelegraph, attack + " lost its ground warning.");
        }

        [TestCase("BossSweep")]
        [TestCase("BossCharge")]
        [TestCase("BossJab")]
        [TestCase("PlayerLight1")]
        [TestCase("PlayerHeavy")]
        public void OrdinarySwingsAreReadFromTheBody(string attack)
        {
            Assert.IsFalse(Load(attack).ShowTelegraph, attack + " still draws a ground warning.");
        }
    }
}
