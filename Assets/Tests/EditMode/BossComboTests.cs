using AdaptiveBossArena.AI.States;
using AdaptiveBossArena.Core.Services;
using NUnit.Framework;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试首领如何决定连击长度。
    /// </summary>
    /// <remarks>
    /// 连击长度来自带种子的随机源，依靠此类决定具有确定性，才能兑现种子复现战斗。同时保证公平：
    /// 开场不能连击，任何阶段连击不能超过阶段索引，否则不符合设计的递进。
    /// </remarks>
    [TestFixture]
    public sealed class BossComboTests
    {
        /// <summary>未学习时的继续概率，让边界测试以名称表达基线。</summary>
        private const float Baseline = BossAttackState.BaseComboContinueChance;

        /// <summary>固定硬币结果的桩对象，使边界测试不依赖种子。</summary>
        private sealed class FixedBoolRandom : IRandomProvider
        {
            private readonly bool _value;

            public FixedBoolRandom(bool value) => _value = value;

            public uint Seed => 0u;

            public float NextFloat01() => 0f;

            public float NextFloat(float min, float max) => min;

            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;

            public bool NextBool(float probability = 0.5f) => _value;

            public Vector2 NextDirectionOnPlane() => Vector2.right;

            public void Reseed(uint seed) { }
        }

        [Test]
        public void OpeningPhaseNeverChains()
        {
            // 即使随机源总要求继续，阶段零也只打一击。
            Assert.AreEqual(0, BossAttackState.ComboLength(0, new FixedBoolRandom(true), Baseline));
        }

        [Test]
        public void AlwaysContinuingChainsUpToThePhaseIndex()
        {
            Assert.AreEqual(1, BossAttackState.ComboLength(1, new FixedBoolRandom(true), Baseline));
            Assert.AreEqual(2, BossAttackState.ComboLength(2, new FixedBoolRandom(true), Baseline));
            Assert.AreEqual(3, BossAttackState.ComboLength(3, new FixedBoolRandom(true), Baseline));
        }

        [Test]
        public void NeverContinuingProducesNoExtraHits()
        {
            Assert.AreEqual(0, BossAttackState.ComboLength(3, new FixedBoolRandom(false), Baseline));
        }

        [Test]
        public void ResultStaysWithinBoundsAcrossManySeeds()
        {
            for (uint seed = 1; seed <= 200; seed++)
            {
                var random = new XorShiftRandomProvider(seed);
                int length = BossAttackState.ComboLength(2, random, Baseline);

                Assert.GreaterOrEqual(length, 0, $"Seed {seed} produced a negative combo length.");
                Assert.LessOrEqual(length, 2, $"Seed {seed} chained more than the phase allows.");
            }
        }

        [Test]
        public void SameSeedProducesTheSameLength()
        {
            int first = BossAttackState.ComboLength(2, new XorShiftRandomProvider(12345u), Baseline);
            int second = BossAttackState.ComboLength(2, new XorShiftRandomProvider(12345u), Baseline);

            Assert.AreEqual(first, second);
        }

        [Test]
        public void TheLastStandPhaseStaysWithinItsBounds()
        {
            // 绝望阶段索引 3 最危险，但公平契约仍有效：可猛烈连击，却不超过索引且不会为负。
            for (uint seed = 1; seed <= 200; seed++)
            {
                int length = BossAttackState.ComboLength(3, new XorShiftRandomProvider(seed), Baseline);

                Assert.GreaterOrEqual(length, 0, $"Seed {seed} produced a negative combo length.");
                Assert.LessOrEqual(length, 3, $"Seed {seed} chained more than the Last Stand allows.");
            }
        }

        [Test]
        public void ABossThatHasLearnedToPress_ChainsMoreOften()
        {
            // ComboExtensionChance 应产生的行为。此前三策略设置却无人读取，使“开始串联攻击”只是空话；
            // 这里固定概率提高确实会延长连击。
            int baselineTotal = 0;
            int pressingTotal = 0;

            for (uint seed = 1; seed <= 400; seed++)
            {
                baselineTotal += BossAttackState.ComboLength(
                    3, new XorShiftRandomProvider(seed), Baseline);

                pressingTotal += BossAttackState.ComboLength(
                    3, new XorShiftRandomProvider(seed), Baseline + 0.35f);
            }

            Assert.Greater(
                pressingTotal, baselineTotal,
                "A raised combo-extension chance should produce longer chains overall.");
        }

        [Test]
        public void TheContinueChanceIsClamped()
        {
            // 调参平滑靠近目标并叠加基线，可能超过一。越界概率不得改变连击长度公平契约。
            Assert.AreEqual(2, BossAttackState.ComboLength(2, new XorShiftRandomProvider(7u), 5f));
            Assert.AreEqual(0, BossAttackState.ComboLength(2, new XorShiftRandomProvider(7u), -3f));
        }

        [Test]
        public void ALearnedDelay_MakesTheBossWaitLonger()
        {
            // “它现在等待你露出破绽。”背后的行为。最常采用的策略写入 AttackDelay，
            // 却没有任何逻辑读取它，因此提示宣称首领在耐心等待，而实际并没有。
            float untrained = BossAttackState.AttackCooldownFor(1f, 0f, isFeint: false);
            float patient = BossAttackState.AttackCooldownFor(1f, 0.35f, isFeint: false);

            Assert.AreEqual(1f, untrained, 0.0001f);
            Assert.AreEqual(1.35f, patient, 0.0001f);
            Assert.Greater(patient, untrained);
        }

        [Test]
        public void AFeintCostsLessThanACommittedSwing_AndStillRespectsTheDelay()
        {
            float feint = BossAttackState.AttackCooldownFor(1f, 0f, isFeint: true);
            float committed = BossAttackState.AttackCooldownFor(1f, 0f, isFeint: false);

            Assert.Less(feint, committed, "A feint must recover faster than a committed swing.");

            // 学习出的耐心同样适用于佯攻，避免延迟首领通过佯攻绕过限制。
            Assert.Greater(
                BossAttackState.AttackCooldownFor(1f, 0.4f, isFeint: true), feint);
        }

        [Test]
        public void ANegativeDelay_CannotSpeedTheBossUp()
        {
            // 适应在这里只能增加耐心。负值会让衰减目标缩短阶段自身节奏，这是首领不应获得的能力。
            Assert.AreEqual(
                1f, BossAttackState.AttackCooldownFor(1f, -2f, isFeint: false), 0.0001f);
        }
    }
}
