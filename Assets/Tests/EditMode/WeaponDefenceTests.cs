using AdaptiveBossArena.Combat;
using UnityEditor;
using NUnit.Framework;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 断言发布数据中的三把武器确实具有不同防御效果。
    /// </summary>
    /// <remarks>
    /// 区分武器的规则由以下测试覆盖： <see cref="DefenceResolverTests"/>，
    /// 但同样数值下的规则只会产生同样武器。这些数值曾通过读取角色配置而非手持武器的属性进入运行时，
    /// 因此所有弹反都造成相同的 28 点架势伤害，编写的差异成为无效数据。代码看不出该故障，
    /// 因为两条路径都可编译且返回看似合理的数字。
    /// </remarks>
    [TestFixture]
    public sealed class WeaponDefenceTests
    {
        private const string WeaponFolder = "Assets/_Project/ScriptableObjects/Weapons";

        private static WeaponDefinition Load(string assetName)
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(
                $"{WeaponFolder}/{assetName}.asset");

            Assert.IsNotNull(weapon, $"Generated weapon '{assetName}' is missing.");

            return weapon;
        }

        [Test]
        public void EachWeaponDeflectsForItsOwnPosture()
        {
            Assert.AreEqual(28f, Load("WeaponBlade").DeflectPostureDamage, 0.001f);
            Assert.AreEqual(16f, Load("WeaponEnergyBlade").DeflectPostureDamage, 0.001f);
        }

        [Test]
        public void TheGreatswordCannotDeflectAtAll()
        {
            WeaponDefinition greatsword = Load("WeaponGreatsword");

            // 零架势收益符合设计，并非遗漏：霸体没有可进入的弹反分支，因此永远不读取该数值。
            // 同时断言两者，能防止有人“修复”零值，造出无需代价就能招架的武器。
            Assert.IsFalse(greatsword.CanDeflect);
            Assert.AreEqual(0f, greatsword.DeflectPostureDamage, 0.001f);
        }

        [Test]
        public void TheTwoParryingWeaponsTradeWindowAgainstReward()
        {
            WeaponDefinition blade = Load("WeaponBlade");
            WeaponDefinition energy = Load("WeaponEnergyBlade");

            // 让武器选择有意义的取舍：能量刃允许更差的时机，却给予更少回报。任一方面反转，
            // 都会使一种武器在防御上完全优于另一种。
            Assert.Greater(energy.DeflectWindowSeconds, blade.DeflectWindowSeconds);
            Assert.Less(energy.DeflectPostureDamage, blade.DeflectPostureDamage);
        }
    }
}
