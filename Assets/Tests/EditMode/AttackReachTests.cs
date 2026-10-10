using System.Reflection;
using AdaptiveBossArena.Combat;
using NUnit.Framework;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 固定攻击可达距离的唯一定义。
    /// </summary>
    /// <remarks>
    /// 首领四处逻辑判断可击中距离：选招、停止接近、连击能否继续、控制器自身检查。以前各自复制同一计算。
    /// 突进位移即将变化，任一旧副本都会让首领选中无法命中的攻击。
    /// </remarks>
    [TestFixture]
    public sealed class AttackReachTests
    {
        private static AttackDefinition Attack(float range, float lungeSpeed, float startupSeconds)
        {
            var attack = ScriptableObject.CreateInstance<AttackDefinition>();
            Set(attack, "_range", range);
            Set(attack, "_lungeSpeed", lungeSpeed);
            Set(attack, "_startupSeconds", startupSeconds);

            return attack;
        }

        private static void Set(AttackDefinition attack, string field, float value) =>
            typeof(AttackDefinition)
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(attack, value);

        [Test]
        public void AStandingAttackReachesExactlyItsRange()
        {
            AttackDefinition jab = Attack(range: 3f, lungeSpeed: 0f, startupSeconds: 0.28f);

            Assert.AreEqual(3f, jab.EffectiveReach, 0.001f);
            Object.DestroyImmediate(jab);
        }

        [Test]
        public void ALungeAddsTheGroundItCoversDuringItsWindUp()
        {
            // Charge：2.6 米命中范围，0.55 秒前摇期间速度 16 米每秒。
            AttackDefinition charge = Attack(range: 2.6f, lungeSpeed: 16f, startupSeconds: 0.55f);

            Assert.AreEqual(2.6f + 16f * 0.55f, charge.EffectiveReach, 0.001f);
            Object.DestroyImmediate(charge);
        }
    }
}
