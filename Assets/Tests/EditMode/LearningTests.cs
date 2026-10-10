using System.Collections.Generic;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Services;
using AdaptiveBossArena.Learning;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试首领是否从已观察的战斗中得出预期结论。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 学习系统使用纯 C# 正是为了支持这种测试。人工战斗事件流模拟带有特定习惯的玩家，
    /// 断言检查首领对玩家的认识是否吻合。否则只能实际与其战斗并凭印象判断学习是否正确。
    /// </para>
    /// <para>
    /// 置信度断言与数值断言同样重要。只观察两次就得出强结论的首领是在反制巧合，
    /// 会让玩家觉得它看到了不该知道的信息。
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class PatternRecognizerTests
    {
        private const float MemoryHalfLife = 45f;

        private CombatMemory _memory;
        private BehaviorTracker _tracker;
        private PatternRecognizer _recognizer;
        private BehaviorProfile _profile;

        [SetUp]
        public void CreateSystem()
        {
            _memory = new CombatMemory();
            _tracker = new BehaviorTracker(_memory, MemoryHalfLife);
            _recognizer = new PatternRecognizer();
            _profile = new BehaviorProfile();
        }

        private void Feed(CombatEventKind kind, float timestamp, CombatantTeam actor = CombatantTeam.Player,
            DamageType damageType = DamageType.Light, Vector3 direction = default) =>
            _tracker.OnCombatEvent(new CombatEvent
            {
                Kind = kind,
                Actor = actor,
                Timestamp = timestamp,
                DamageType = damageType,
                Direction = direction
            });

        private void Analyse(float timeNow) => _recognizer.Analyse(_tracker, _memory, _profile, timeNow);

        [Test]
        public void AParryIsCreditedToTheDefenderNotTheAttacker()
        {
            // 整个招架链路依赖的约定：Parried 事件以防御者为执行主体。若反向发布，
            // 挥击被招架的玩家会被计为成功弹反，而首领以佯攻和不可格挡攻击应对的弹反熟练度读数，
            // 就会基于从未发生过的事件推理。故障本身不会显现，首领只会适应错误的玩家。
            _memory.Record(new CombatEvent
            {
                Kind = CombatEventKind.Parried,
                Actor = CombatantTeam.Boss,
                Timestamp = 1f
            });

            Assert.AreEqual(1, _memory.BossParries, "A boss parry was not counted for the boss.");
            Assert.AreEqual(0, _memory.PlayerDeflects, "A boss parry was counted as a player deflect.");
        }

        [Test]
        public void APlayerDeflectStaysOnThePlayersTally()
        {
            _memory.Record(new CombatEvent
            {
                Kind = CombatEventKind.Deflected,
                Actor = CombatantTeam.Player,
                Timestamp = 1f
            });

            Assert.AreEqual(1, _memory.PlayerDeflects);
            Assert.AreEqual(0, _memory.BossParries);
        }

        [Test]
        public void WithNoObservations_EveryHabitHasZeroConfidence()
        {
            Analyse(0f);

            foreach (BehaviorFeature feature in System.Enum.GetValues(typeof(BehaviorFeature)))
            {
                Assert.AreEqual(0f, _profile.Get(feature).Confidence, 0.001f,
                    $"{feature} claimed confidence without evidence.");
            }
        }

        [Test]
        public void APlayerWhoAlwaysAttacksAfterDodging_IsRecognised()
        {
            // 二十次闪避，每次之后立即攻击，正是需求描述中指出的习惯。
            float time = 0f;
            for (int i = 0; i < 20; i++)
            {
                Feed(CombatEventKind.DodgePerformed, time, direction: Vector3.left);
                Feed(CombatEventKind.AttackStarted, time + 0.2f);
                time += 2f;
            }

            Analyse(time);

            FeatureReading reading = _profile.Get(BehaviorFeature.PostDodgeAggression);

            Assert.Greater(reading.Value, 0.9f, "The follow-up habit should be read as near-total.");
            Assert.Greater(reading.Confidence, 0.7f, "Twenty dodges is ample evidence.");
        }

        [Test]
        public void APlayerWhoNeverAttacksAfterDodging_IsNotAccused()
        {
            float time = 0f;
            for (int i = 0; i < 20; i++)
            {
                Feed(CombatEventKind.DodgePerformed, time, direction: Vector3.left);

                // 攻击发生时，后续动作窗口早已关闭。
                Feed(CombatEventKind.AttackStarted, time + 1.5f);
                time += 3f;
            }

            Analyse(time);

            Assert.Less(_profile.Get(BehaviorFeature.PostDodgeAggression).Value, 0.1f);
        }

        [Test]
        public void ASingleDodgeAndAttack_ProducesLowConfidence()
        {
            Feed(CombatEventKind.DodgePerformed, 0f, direction: Vector3.left);
            Feed(CombatEventKind.AttackStarted, 0.2f);

            Analyse(1f);

            FeatureReading reading = _profile.Get(BehaviorFeature.PostDodgeAggression);

            // 数值是完美的一一对应，但不能信任单次观察。这是防止首领反制巧合的保护。
            Assert.AreEqual(1f, reading.Value, 0.01f);
            Assert.Less(reading.Confidence, 0.15f);
        }

        [Test]
        public void APlayerWhoAlwaysRollsTheSameWay_IsRecognisedAsPredictable()
        {
            float time = 0f;
            for (int i = 0; i < 25; i++)
            {
                Feed(CombatEventKind.DodgePerformed, time, direction: Vector3.left);
                time += 1f;
            }

            Analyse(time);

            FeatureReading reading = _profile.Get(BehaviorFeature.DashDirectionBias);

            Assert.Greater(reading.Value, 0.9f);
            Assert.Greater(reading.Confidence, 0.7f);
        }

        [Test]
        public void APlayerWhoRollsEveryWay_IsNotRecognisedAsPredictable()
        {
            Vector3[] directions =
            {
                Vector3.left, Vector3.right, Vector3.forward, Vector3.back,
                new Vector3(1f, 0f, 1f), new Vector3(-1f, 0f, 1f),
                new Vector3(1f, 0f, -1f), new Vector3(-1f, 0f, -1f)
            };

            float time = 0f;
            for (int i = 0; i < 32; i++)
            {
                Feed(CombatEventKind.DodgePerformed, time, direction: directions[i % directions.Length]);
                time += 0.5f;
            }

            Analyse(time);

            // 声称能预测均匀分布移动的首领会明显像是在作弊。
            Assert.Less(_profile.Get(BehaviorFeature.DashDirectionBias).Value, 0.1f);
        }

        [Test]
        public void APlayerWhoLeansOnHeavyAttacks_IsRecognised()
        {
            float time = 0f;
            for (int i = 0; i < 20; i++)
            {
                Feed(CombatEventKind.AttackStarted, time, damageType: DamageType.Heavy);
                time += 1.5f;
            }

            Analyse(time);

            FeatureReading reading = _profile.Get(BehaviorFeature.HeavyAttackRatio);

            Assert.Greater(reading.Value, 0.9f);
            Assert.Greater(reading.Confidence, 0.7f);
        }

        [Test]
        public void APlayerWhoAvoidsEverything_IsRecognisedAsEvasive()
        {
            float time = 0f;
            for (int i = 0; i < 15; i++)
            {
                Feed(CombatEventKind.AttackEvaded, time, CombatantTeam.Boss);
                time += 2f;
            }

            Analyse(time);

            FeatureReading reading = _profile.Get(BehaviorFeature.EvasionSuccess);

            Assert.Greater(reading.Value, 0.9f);
            Assert.Greater(reading.Confidence, 0.7f);
        }

        [Test]
        public void APlayerWhoLeansOnHealing_IsRecognised()
        {
            // 设计将一场战斗中四次治疗视为明确习惯；首领应读出它，并愿意惩罚治疗创造的破绽。
            float time = 0f;
            for (int i = 0; i < 4; i++)
            {
                Feed(CombatEventKind.HealStarted, time);
                time += 4f;
            }

            Analyse(time);

            FeatureReading reading = _profile.Get(BehaviorFeature.HealFrequency);

            Assert.Greater(reading.Value, 0.9f, "A player healing repeatedly should read as reliant on it.");
            Assert.Greater(reading.Confidence, 0.6f, "Four heals is enough to believe the habit.");
        }

        [Test]
        public void APlayerWhoPerfectDodgesOnRhythm_IsRecognised()
        {
            // 每次闪避都落在完美闪避帧上：毫无失误，因此也可利用的节奏。
            float time = 0f;
            for (int i = 0; i < 20; i++)
            {
                Feed(CombatEventKind.DodgePerformed, time, direction: Vector3.left);
                Feed(CombatEventKind.PerfectDodge, time + 0.01f, direction: Vector3.left);
                time += 1.5f;
            }

            Analyse(time);

            FeatureReading reading = _profile.Get(BehaviorFeature.DodgeTimingConsistency);

            Assert.Greater(reading.Value, 0.9f, "Flawless dodge timing should read as near-total consistency.");
            Assert.Greater(reading.Confidence, 0.7f, "Twenty dodges is ample evidence.");
        }

        [Test]
        public void APlayerWhoDodgesButRarelyTimesItPerfectly_IsNotAccusedOfRhythm()
        {
            // 同样的二十次闪避，没有任何一次帧级完美：佯攻所反制的习惯不存在，若仍读出它，
            // 就会诱导玩家从未使用过的闪避节奏。
            float time = 0f;
            for (int i = 0; i < 20; i++)
            {
                Feed(CombatEventKind.DodgePerformed, time, direction: Vector3.left);
                time += 1.5f;
            }

            Analyse(time);

            Assert.Less(_profile.Get(BehaviorFeature.DodgeTimingConsistency).Value, 0.1f);
        }

        [Test]
        public void ATurtle_IsRecognisedAsRelyingOnTheGuard()
        {
            // 二十次攻击全部由防御接下。这些事件从最初便已发布，却无人统计，使该习惯对首领不可见。
            float time = 0f;
            for (int i = 0; i < 20; i++)
            {
                Feed(CombatEventKind.Blocked, time);
                time += 1f;
            }

            Analyse(time);

            FeatureReading reading = _profile.Get(BehaviorFeature.GuardReliance);

            Assert.Greater(reading.Value, 0.8f, "A player who blocks everything relies on the guard.");
            Assert.Greater(reading.Confidence, 0.5f);
        }

        [Test]
        public void APlayerWhoMovesInsteadOfBlocking_DoesNotReadAsATurtle()
        {
            float time = 0f;
            for (int i = 0; i < 20; i++)
            {
                Feed(CombatEventKind.AttackEvaded, time, CombatantTeam.Boss);
                time += 1f;
            }

            Analyse(time);

            Assert.Less(_profile.Get(BehaviorFeature.GuardReliance).Value, 0.1f);
        }

        [Test]
        public void AParryMaster_IsDistinguishedFromSomeoneMerelyHoldingBlock()
        {
            // 两位玩家防御一切攻击，因此依赖程度相同；但只有一位时机精准，两种习惯需要不同回应：
            // 不可格挡攻击对付龟缩，佯攻对付固定节奏。
            float time = 0f;
            for (int i = 0; i < 16; i++)
            {
                Feed(CombatEventKind.Deflected, time);
                time += 1f;
            }

            Analyse(time);

            FeatureReading skill = _profile.Get(BehaviorFeature.DeflectSkill);

            Assert.Greater(skill.Value, 0.9f, "Every guarded answer was a clean deflect.");
            Assert.Greater(skill.Confidence, 0.5f);
            Assert.Greater(_profile.Get(BehaviorFeature.GuardReliance).Value, 0.8f);
        }

        [Test]
        public void ALatePlayerWhoBlocksRatherThanDeflects_ReadsAsUnskilled()
        {
            float time = 0f;
            for (int i = 0; i < 16; i++)
            {
                Feed(CombatEventKind.Blocked, time);
                time += 1f;
            }

            Analyse(time);

            // 依赖防御却不擅长，因此用佯攻应对它，就是反制玩家并不存在的节奏。
            Assert.Greater(_profile.Get(BehaviorFeature.GuardReliance).Value, 0.8f);
            Assert.Less(_profile.Get(BehaviorFeature.DeflectSkill).Value, 0.1f);
        }

        [Test]
        public void DeflectSkill_RestsOnlyOnGuardedExchanges()
        {
            // 两次防御回应均为弹反，在几乎没有证据时得到完美比例。数值可以高，置信度却不能高，
            // 否则首领会向尚未经考验的玩家开始佯攻。
            Feed(CombatEventKind.Deflected, 0f);
            Feed(CombatEventKind.Deflected, 1f);

            Analyse(2f);

            Assert.Less(_profile.Get(BehaviorFeature.DeflectSkill).Confidence, 0.5f);
        }

        [Test]
        public void PreferredDistance_ReflectsWhereThePlayerActuallyStands()
        {
            float time = 0f;
            for (int i = 0; i < 200; i++)
            {
                // 玩家长期保持远距离，持续多秒采样。
                _tracker.Tick(0.05f, time, separationDistance: 11f);
                time += 0.05f;
            }

            Analyse(time);

            FeatureReading reading = _profile.Get(BehaviorFeature.PreferredDistance);

            Assert.Greater(reading.Value, 0.6f, "A kiting player should read as preferring distance.");
            Assert.Greater(reading.Confidence, 0.5f);
        }

        [Test]
        public void AnAbandonedHabit_Fades()
        {
            // 使用短记忆首领，使衰减可在测试期间观察到。
            var memory = new CombatMemory();
            var tracker = new BehaviorTracker(memory, memoryHalfLifeSeconds: 2f);
            var profile = new BehaviorProfile();

            float time = 0f;
            for (int i = 0; i < 30; i++)
            {
                tracker.OnCombatEvent(new CombatEvent
                {
                    Kind = CombatEventKind.DodgePerformed,
                    Actor = CombatantTeam.Player,
                    Timestamp = time,
                    Direction = Vector3.left
                });

                time += 0.2f;
            }

            _recognizer.Analyse(tracker, memory, profile, time);
            float whileHabitual = profile.Get(BehaviorFeature.DashDirectionBias).Confidence;

            // 玩家完全停止闪避并保持较长时间。
            for (int i = 0; i < 400; i++)
            {
                tracker.Tick(0.05f, time, separationDistance: 5f);
                time += 0.05f;
            }

            _recognizer.Analyse(tracker, memory, profile, time);
            float afterAbandoning = profile.Get(BehaviorFeature.DashDirectionBias).Confidence;

            // 置信度必须下降，否则玩家即使更换战术，首领也永远不会停止反制玩家已不再具有的习惯。
            Assert.Less(afterAbandoning, whileHabitual);
        }
    }

    /// <summary>
    /// 测试反制策略选择遵循门控且保持可复现。
    /// </summary>
    [TestFixture]
    public sealed class CounterSelectorTests
    {
        private readonly List<CounterStrategy> _created = new List<CounterStrategy>();

        [TearDown]
        public void DestroyStrategies()
        {
            foreach (CounterStrategy strategy in _created)
            {
                Object.DestroyImmediate(strategy);
            }

            _created.Clear();
        }

        private CounterStrategy MakeStrategy(
            BehaviorFeature feature,
            float threshold,
            float minimumConfidence,
            int minimumPhase,
            float weight = 1f)
        {
            var strategy = ScriptableObject.CreateInstance<CounterStrategy>();
            _created.Add(strategy);

            var serialized = new SerializedObject(strategy);
            serialized.FindProperty("_triggerFeature").enumValueIndex = (int)feature;
            serialized.FindProperty("_comparison").enumValueIndex = (int)FeatureComparison.AtOrAbove;
            serialized.FindProperty("_threshold").floatValue = threshold;
            serialized.FindProperty("_minimumConfidence").floatValue = minimumConfidence;
            serialized.FindProperty("_minimumPhase").intValue = minimumPhase;
            serialized.FindProperty("_selectionWeight").floatValue = weight;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return strategy;
        }

        private static BehaviorProfile ProfileWith(BehaviorFeature feature, float value, int sampleCount)
        {
            var profile = new BehaviorProfile();
            profile.Set(feature, value, sampleCount, halfConfidenceSampleCount: 10);
            return profile;
        }

        [Test]
        public void AStrategyBelowItsThreshold_IsNotSelected()
        {
            CounterStrategy strategy = MakeStrategy(
                BehaviorFeature.HeavyAttackRatio, threshold: 0.8f, minimumConfidence: 0f, minimumPhase: 1);

            var selector = new CounterSelector(new XorShiftRandomProvider(1u));
            BehaviorProfile profile = ProfileWith(BehaviorFeature.HeavyAttackRatio, 0.3f, 100);

            Assert.IsNull(selector.Select(new[] { strategy }, profile, 1, null));
        }

        [Test]
        public void AStrategyWithoutEnoughConfidence_IsNotSelected()
        {
            CounterStrategy strategy = MakeStrategy(
                BehaviorFeature.HeavyAttackRatio, threshold: 0.5f, minimumConfidence: 0.8f, minimumPhase: 1);

            var selector = new CounterSelector(new XorShiftRandomProvider(1u));

            // 习惯很强，但只依据一次观察。
            BehaviorProfile profile = ProfileWith(BehaviorFeature.HeavyAttackRatio, 1f, 1);

            Assert.IsNull(selector.Select(new[] { strategy }, profile, 1, null));
        }

        [Test]
        public void AStrategyGatedToALaterPhase_IsNotSelectedEarly()
        {
            CounterStrategy strategy = MakeStrategy(
                BehaviorFeature.HeavyAttackRatio, threshold: 0.5f, minimumConfidence: 0f, minimumPhase: 3);

            var selector = new CounterSelector(new XorShiftRandomProvider(1u));
            BehaviorProfile profile = ProfileWith(BehaviorFeature.HeavyAttackRatio, 1f, 100);

            Assert.IsNull(selector.Select(new[] { strategy }, profile, 1, null));
            Assert.IsNotNull(selector.Select(new[] { strategy }, profile, 3, null));
        }

        [Test]
        public void AnAlreadyAdoptedStrategy_IsNotSelectedAgain()
        {
            CounterStrategy strategy = MakeStrategy(
                BehaviorFeature.HeavyAttackRatio, threshold: 0.5f, minimumConfidence: 0f, minimumPhase: 1);

            var selector = new CounterSelector(new XorShiftRandomProvider(1u));
            BehaviorProfile profile = ProfileWith(BehaviorFeature.HeavyAttackRatio, 1f, 100);

            var adopted = new HashSet<CounterStrategy> { strategy };

            Assert.IsNull(selector.Select(new[] { strategy }, profile, 1, adopted));
        }

        [Test]
        public void SelectionIsReproducibleFromASeed()
        {
            CounterStrategy first = MakeStrategy(
                BehaviorFeature.HeavyAttackRatio, 0.4f, 0f, 1, weight: 1f);

            CounterStrategy second = MakeStrategy(
                BehaviorFeature.HeavyAttackRatio, 0.4f, 0f, 1, weight: 3f);

            var catalogue = new[] { first, second };
            BehaviorProfile profile = ProfileWith(BehaviorFeature.HeavyAttackRatio, 1f, 100);

            var runA = new List<CounterStrategy>();
            var runB = new List<CounterStrategy>();

            var selectorA = new CounterSelector(new XorShiftRandomProvider(4242u));
            var selectorB = new CounterSelector(new XorShiftRandomProvider(4242u));

            for (int i = 0; i < 50; i++)
            {
                runA.Add(selectorA.Select(catalogue, profile, 1, null));
                runB.Add(selectorB.Select(catalogue, profile, 1, null));
            }

            // 若概率行为无法由种子复现，首领就无法测试，任何故障报告也无法有效处理。
            CollectionAssert.AreEqual(runA, runB);
        }

        [Test]
        public void SelectionIsNotDeterministicAcrossDifferentSeeds()
        {
            CounterStrategy first = MakeStrategy(BehaviorFeature.HeavyAttackRatio, 0.4f, 0f, 1, 1f);
            CounterStrategy second = MakeStrategy(BehaviorFeature.HeavyAttackRatio, 0.4f, 0f, 1, 1f);

            var catalogue = new[] { first, second };
            BehaviorProfile profile = ProfileWith(BehaviorFeature.HeavyAttackRatio, 1f, 100);

            var selector = new CounterSelector(new XorShiftRandomProvider(99u));
            var seen = new HashSet<CounterStrategy>();

            for (int i = 0; i < 100; i++)
            {
                seen.Add(selector.Select(catalogue, profile, 1, null));
            }

            // 若最强习惯总产生相同回应，玩家就会学会查询表，战斗变成背诵。
            Assert.AreEqual(2, seen.Count, "Selection among equal candidates should vary.");
        }

        [Test]
        public void APanickedWhiffer_DrawsAWhiffRatioCounter()
        {
            // WhiffRatio 曾是完整计算却没有策略回应的特征。CounterWhiffPunish补齐了缺口；
            // 此处保证习惯足够强且置信度足够高时，以 WhiffRatio为依据的策略确实可以选中。
            CounterStrategy punishWhiffing = MakeStrategy(
                BehaviorFeature.WhiffRatio, threshold: 0.5f, minimumConfidence: 0.4f, minimumPhase: 1);

            var selector = new CounterSelector(new XorShiftRandomProvider(7u));
            BehaviorProfile profile = ProfileWith(BehaviorFeature.WhiffRatio, 0.8f, 100);

            Assert.AreSame(punishWhiffing, selector.Select(new[] { punishWhiffing }, profile, 1, null));
        }
    }

    /// <summary>测试使自适应可感知且不突兀的渐进变化。</summary>
    [TestFixture]
    public sealed class BossTuningTests
    {
        private const float Frame = 1f / 60f;

        [Test]
        public void NewTuning_SitsAtItsBaseline()
        {
            var tuning = new BossTuning(baselinePreferredRange: 3f, baselineAggression: 0.5f);

            Assert.AreEqual(3f, tuning.Get(BossTuningParameter.PreferredRange), 0.001f);
            Assert.AreEqual(0.5f, tuning.Get(BossTuningParameter.Aggression), 0.001f);
            Assert.AreEqual(0f, tuning.Get(BossTuningParameter.ParryChance), 0.001f);
        }

        [Test]
        public void SettingATarget_DoesNotChangeTheValueImmediately()
        {
            var tuning = new BossTuning(3f, 0.5f);
            tuning.SetTarget(BossTuningParameter.ParryChance, 0.9f);

            // 瞬间变化像是首领被直接告知信息，而非自行推断出来。
            Assert.AreEqual(0f, tuning.Get(BossTuningParameter.ParryChance), 0.001f);
        }

        [Test]
        public void AValueEasesTowardItsTargetOverTime()
        {
            var tuning = new BossTuning(3f, 0.5f);
            tuning.SetTarget(BossTuningParameter.ParryChance, 1f);

            for (int i = 0; i < 120; i++)
            {
                tuning.Tick(Frame, easeHalfLifeSeconds: 1f, forgetHalfLifeSeconds: 1000f);
            }

            Assert.Greater(tuning.Get(BossTuningParameter.ParryChance), 0.7f);
        }

        [Test]
        public void AnUnreinforcedTarget_DecaysBackToBaseline()
        {
            var tuning = new BossTuning(3f, 0.5f);
            tuning.SetTarget(BossTuningParameter.ParryChance, 1f);

            for (int i = 0; i < 60; i++)
            {
                tuning.Tick(Frame, easeHalfLifeSeconds: 0.5f, forgetHalfLifeSeconds: 1000f);
            }

            float whileReinforced = tuning.Get(BossTuningParameter.ParryChance);

            for (int i = 0; i < 600; i++)
            {
                tuning.Tick(Frame, easeHalfLifeSeconds: 0.5f, forgetHalfLifeSeconds: 2f);
            }

            // 否则玩家一旦被反制，无论后续如何改变玩法，整场战斗都会持续被反制。
            Assert.Less(tuning.Get(BossTuningParameter.ParryChance), whileReinforced * 0.5f);
        }

        [Test]
        public void ResetToBaseline_UndoesEverythingLearned()
        {
            var tuning = new BossTuning(3f, 0.5f);
            tuning.SetTarget(BossTuningParameter.ParryChance, 1f);

            for (int i = 0; i < 120; i++)
            {
                tuning.Tick(Frame, 0.5f, 1000f);
            }

            tuning.ResetToBaseline();

            Assert.AreEqual(0f, tuning.Get(BossTuningParameter.ParryChance), 0.001f);
            Assert.AreEqual(3f, tuning.Get(BossTuningParameter.PreferredRange), 0.001f);
        }

        [Test]
        public void FreshTuning_HasNoCommitment()
        {
            var tuning = new BossTuning(3f, 0.5f);

            // 整个失衡机制依赖未学到任何内容时该值为零，从而保持基础战斗不变。
            Assert.AreEqual(0f, tuning.CommitmentIntensity, 0.0001f);
        }

        [Test]
        public void EasingTheCommittingParameters_RaisesCommitmentIntensity()
        {
            var tuning = new BossTuning(3f, baselineAggression: 0.5f);
            tuning.SetTarget(BossTuningParameter.Aggression, 0.85f);
            tuning.SetTarget(BossTuningParameter.GapCloserWeight, 0.7f);
            tuning.SetTarget(BossTuningParameter.ComboExtensionChance, 0.5f);

            for (int i = 0; i < 300; i++)
            {
                tuning.Tick(Frame, easeHalfLifeSeconds: 1f, forgetHalfLifeSeconds: 1000f);
            }

            // 完全投入压制策略的首领应表现为高度投入。
            Assert.Greater(tuning.CommitmentIntensity, 0.5f);
        }

        [Test]
        public void NonCommittingParameters_DoNotContributeToCommitment()
        {
            var tuning = new BossTuning(3f, 0.5f);

            // 读出重击会提高 ParryChance，但招架不是投入突进，因此它本身不能让首领更容易失衡。
            tuning.SetTarget(BossTuningParameter.ParryChance, 1f);

            for (int i = 0; i < 300; i++)
            {
                tuning.Tick(Frame, 1f, 1000f);
            }

            Assert.AreEqual(0f, tuning.CommitmentIntensity, 0.0001f);
        }
    }
}
