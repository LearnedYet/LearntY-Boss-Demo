using AdaptiveBossArena.Combat;
using NUnit.Framework;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试使三种武器防御不同的规则。
    /// </summary>
    /// <remarks>
    /// 这些既是手感也是公平契约：霸体应保护投入阶段而不保护后摇，否则巨剑只会更安全而非真正取舍；
    /// 只有持续格挡应更慢损失姿态，否则武器差异会瓦解。
    /// </remarks>
    [TestFixture]
    public sealed class DefenceResolverTests
    {
        [Test]
        public void HyperArmourResistsStaggerDuringTheCommittedSwing()
        {
            Assert.IsTrue(DefenceResolver.ResistsStagger(DefenceStyle.HyperArmour, AttackPhase.Startup));
            Assert.IsTrue(DefenceResolver.ResistsStagger(DefenceStyle.HyperArmour, AttackPhase.Active));
        }

        [Test]
        public void HyperArmourDoesNotProtectRecoveryOrIdle()
        {
            Assert.IsFalse(DefenceResolver.ResistsStagger(DefenceStyle.HyperArmour, AttackPhase.Recovery));
            Assert.IsFalse(DefenceResolver.ResistsStagger(DefenceStyle.HyperArmour, AttackPhase.Inactive));
        }

        [Test]
        public void OtherStylesNeverResistStagger()
        {
            Assert.IsFalse(DefenceResolver.ResistsStagger(DefenceStyle.Deflect, AttackPhase.Active));
            Assert.IsFalse(DefenceResolver.ResistsStagger(DefenceStyle.SustainedGuard, AttackPhase.Active));
        }

        [Test]
        public void OnlySustainedGuardBlocksCheaply()
        {
            Assert.Less(DefenceResolver.BlockPostureMultiplier(DefenceStyle.SustainedGuard), 1f);
            Assert.AreEqual(1f, DefenceResolver.BlockPostureMultiplier(DefenceStyle.Deflect));
            Assert.AreEqual(1f, DefenceResolver.BlockPostureMultiplier(DefenceStyle.HyperArmour));
        }

        /// <summary>短暂举起的防御，类似玩家持盾。</summary>
        private static DefenceQuery Guard(float heldFor) => new DefenceQuery
        {
            IsDefending = true,
            CanDeflect = true,
            CanBlock = true,
            TimeInDefenceSeconds = heldFor,
            DeflectWindowSeconds = 0.2f
        };

        [Test]
        public void AHitMetOnTheBeatIsDeflected()
        {
            Assert.AreEqual(DefenceOutcome.Deflected, DefenceResolver.ResolveDefence(Guard(0f)));
            Assert.AreEqual(
                DefenceOutcome.Deflected, DefenceResolver.ResolveDefence(Guard(0.2f)),
                "The last instant of the window must still deflect. A window that is exclusive at " +
                "its own boundary is a frame shorter than the number it advertises.");
        }

        [Test]
        public void AHitMetLateIsBlockedRatherThanDeflected()
        {
            Assert.AreEqual(DefenceOutcome.Blocked, DefenceResolver.ResolveDefence(Guard(0.21f)));
        }

        [Test]
        public void NotDefendingResolvesNothing()
        {
            var dropped = new DefenceQuery
            {
                IsDefending = false,
                CanDeflect = true,
                CanBlock = true,
                TimeInDefenceSeconds = 0f,
                DeflectWindowSeconds = 0.2f
            };

            Assert.AreEqual(DefenceOutcome.None, DefenceResolver.ResolveDefence(dropped));
        }

        [Test]
        public void AnUnblockableHitBeatsEvenAPerfectlyTimedGuard()
        {
            var perilous = new DefenceQuery
            {
                IsDefending = true,
                CanDeflect = true,
                CanBlock = true,
                TimeInDefenceSeconds = 0f,
                DeflectWindowSeconds = 0.2f,
                Unblockable = true
            };

            // 标记不可格挡的意义：将安全默认防御变为错误选择，否则玩家无需辨识格挡与闪避。
            Assert.AreEqual(DefenceOutcome.None, DefenceResolver.ResolveDefence(perilous));
        }

        [Test]
        public void AWeaponThatCannotParryStillBlocks()
        {
            // 巨剑没有弹反能力，因此窗口内格挡也应落到普通格挡，而非免费拒绝命中。
            var greatsword = new DefenceQuery
            {
                IsDefending = true,
                CanDeflect = false,
                CanBlock = true,
                TimeInDefenceSeconds = 0f,
                DeflectWindowSeconds = 0.2f
            };

            Assert.AreEqual(DefenceOutcome.Blocked, DefenceResolver.ResolveDefence(greatsword));
        }

        [Test]
        public void ADefenceThatCannotBlockRefusesOnlyWhatItTimes()
        {
            // 首领姿态窗口内拒绝命中，稍后承受完整伤害；可惩罚尾段让诱出姿态有价值。
            var stance = new DefenceQuery
            {
                IsDefending = true,
                CanDeflect = true,
                CanBlock = false,
                TimeInDefenceSeconds = 0.1f,
                DeflectWindowSeconds = 0.18f
            };

            Assert.AreEqual(DefenceOutcome.Deflected, DefenceResolver.ResolveDefence(stance));

            var tail = new DefenceQuery
            {
                IsDefending = true,
                CanDeflect = true,
                CanBlock = false,
                TimeInDefenceSeconds = 0.3f,
                DeflectWindowSeconds = 0.18f
            };

            Assert.AreEqual(DefenceOutcome.None, DefenceResolver.ResolveDefence(tail));
        }
    }
}
