using System;
using System.Collections.Generic;
using AdaptiveBossArena.Core.Services;
using UnityEngine;

namespace AdaptiveBossArena.Learning
{
    /// <summary>
    /// 管理完整学习循环，决定首领何时可以改变行为。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 掌控适应节奏：分析按间隔执行，适应之间有冷却，每次变化都用数秒平滑生效。这三项限制出于同一原因：
    /// 变化过快的自适应首领与作弊首领难以区分，玩家必须有时间察觉并应对变化，下一次变化才能到来。
    /// </para>
    /// <para>
    /// 每项采纳的策略都会通过 <see cref="StrategyAdopted"/> 宣告。这不是表现上的点缀，而是让机制可读。
    /// 玩家无法察觉的适应，会被归因于首领读取了玩家输入。
    /// </para>
    /// <para>
    /// 除 <c>Mathf</c> 外，此类刻意不依赖引擎类型，因此整场战斗的适应可以通过输入模拟事件、推进时钟，
    /// 在编辑模式测试中模拟。
    /// </para>
    /// </remarks>
    public sealed class AdaptationManager
    {
        private readonly CombatMemory _memory;
        private readonly BehaviorTracker _tracker;
        private readonly PatternRecognizer _recognizer;
        private readonly CounterSelector _selector;
        private readonly BehaviorProfile _profile;
        private readonly BossTuning _tuning;

        private readonly IReadOnlyList<CounterStrategy> _catalogue;
        private readonly HashSet<CounterStrategy> _adopted = new HashSet<CounterStrategy>();

        private readonly float _analysisIntervalSeconds;
        private readonly float _minSecondsBetweenAdaptations;
        private readonly float _forgetHalfLifeSeconds;

        private float _nextAnalysisAt;
        private float _nextAdaptationAllowedAt;
        private float _adaptationRateMultiplier = 1f;
        private float _currentEaseHalfLife = DefaultEaseHalfLifeSeconds;

        /// <summary>尚未采纳任何策略时采用的平滑半衰期。</summary>
        private const float DefaultEaseHalfLifeSeconds = 3f;

        /// <summary>创建已接入首领学习组件的管理器。</summary>
        /// <param name="memory">记录观察事件的位置。</param>
        /// <param name="tracker">累计统计跟踪器。</param>
        /// <param name="profile">从统计中推导出的习惯。</param>
        /// <param name="tuning">管理器调整的行为数值。</param>
        /// <param name="catalogue">首领能够形成的全部策略。</param>
        /// <param name="random">供策略选择使用、可设置种子的随机源。</param>
        /// <param name="analysisIntervalSeconds">重新计算习惯的时间间隔。</param>
        /// <param name="minSecondsBetweenAdaptations">两次适应生效之间的最短间隔。</param>
        /// <param name="forgetHalfLifeSeconds">未被强化的调整衰减至一半所需的时间。</param>
        /// <exception cref="ArgumentNullException">缺少必要依赖时抛出。</exception>
        public AdaptationManager(
            CombatMemory memory,
            BehaviorTracker tracker,
            BehaviorProfile profile,
            BossTuning tuning,
            IReadOnlyList<CounterStrategy> catalogue,
            IRandomProvider random,
            float analysisIntervalSeconds,
            float minSecondsBetweenAdaptations,
            float forgetHalfLifeSeconds)
        {
            _memory = memory ?? throw new ArgumentNullException(nameof(memory));
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
            _catalogue = catalogue;

            _recognizer = new PatternRecognizer();
            _selector = new CounterSelector(random ?? throw new ArgumentNullException(nameof(random)));

            _analysisIntervalSeconds = Mathf.Max(0.5f, analysisIntervalSeconds);
            _minSecondsBetweenAdaptations = Mathf.Max(0f, minSecondsBetweenAdaptations);
            _forgetHalfLifeSeconds = Mathf.Max(1f, forgetHalfLifeSeconds);

            // 首领不能在开场最初几秒就适应，因为此时尚未观察到值得针对的习惯。
            _nextAnalysisAt = _analysisIntervalSeconds;
            _nextAdaptationAllowedAt = _minSecondsBetweenAdaptations;
        }

        /// <summary>首领当前的玩家画像。</summary>
        public BehaviorProfile Profile => _profile;

        /// <summary>本次战斗中首领已经形成的策略。</summary>
        public IReadOnlyCollection<CounterStrategy> AdoptedStrategies => _adopted;

        /// <summary>上次重新计算习惯时的战斗时钟时间。</summary>
        public float LastAnalysisAt => _profile.LastUpdatedAt;

        /// <summary>首领形成新应对时触发，并携带面向玩家的提示。</summary>
        public event Action<CounterStrategy> StrategyAdopted;

        /// <summary>缩放首领的适应速度，后续阶段会提高该值。</summary>
        /// <param name="multiplier">适应速率倍率。</param>
        public void SetAdaptationRateMultiplier(float multiplier) =>
            _adaptationRateMultiplier = Mathf.Max(0.1f, multiplier);

        /// <summary>记录一个观察到的事件。</summary>
        /// <param name="combatEvent">发生的事件。</param>
        public void OnCombatEvent(in Core.Combat.CombatEvent combatEvent) =>
            _tracker.OnCombatEvent(combatEvent);

        /// <summary>
        /// 推进学习循环。
        /// </summary>
        /// <param name="deltaTime">经过缩放的时间增量。</param>
        /// <param name="timeNow">当前战斗时钟时间。</param>
        /// <param name="separationDistance">交战双方当前的距离。</param>
        /// <param name="currentPhase">当前阶段，从一开始编号。</param>
        public void Tick(float deltaTime, float timeNow, float separationDistance, int currentPhase)
        {
            _tracker.Tick(deltaTime, timeNow, separationDistance);

            // 无论是否发生新适应，都逐帧平滑调整，确保变化渐进到来，未被强化的变化也同样渐进消退。
            _tuning.Tick(deltaTime, _currentEaseHalfLife, _forgetHalfLifeSeconds);

            if (timeNow < _nextAnalysisAt)
            {
                return;
            }

            _nextAnalysisAt = timeNow + _analysisIntervalSeconds / _adaptationRateMultiplier;
            _recognizer.Analyse(_tracker, _memory, _profile, timeNow);

            TryAdapt(timeNow, currentPhase);
        }

        /// <summary>冷却结束且存在符合条件的策略时，采纳新策略。</summary>
        private void TryAdapt(float timeNow, int currentPhase)
        {
            if (timeNow < _nextAdaptationAllowedAt)
            {
                return;
            }

            CounterStrategy chosen = _selector.Select(_catalogue, _profile, currentPhase, _adopted);

            if (chosen == null)
            {
                return;
            }

            Apply(chosen);

            _adopted.Add(chosen);
            _nextAdaptationAllowedAt =
                timeNow + _minSecondsBetweenAdaptations / _adaptationRateMultiplier;

            StrategyAdopted?.Invoke(chosen);
        }

        /// <summary>将受影响的行为参数指向新目标值。</summary>
        /// <remarks>
        /// 设置目标而非当前值。首领不会在这一瞬间突变；它会在策略自己的平滑时间内逐步改变。
        /// </remarks>
        private void Apply(CounterStrategy strategy)
        {
            _currentEaseHalfLife = Mathf.Max(0.5f, strategy.EaseInSeconds * 0.5f);

            TuningAdjustment[] adjustments = strategy.Adjustments;

            if (adjustments == null)
            {
                return;
            }

            foreach (TuningAdjustment adjustment in adjustments)
            {
                _tuning.SetTarget(adjustment.Parameter, adjustment.TargetValue);
            }
        }

        /// <summary>清除全部学习结果，供战斗重试使用。</summary>
        public void Reset()
        {
            _memory.Clear();
            _tracker.Reset();
            _profile.Clear();
            _tuning.ResetToBaseline();
            _adopted.Clear();

            _nextAnalysisAt = _analysisIntervalSeconds;
            _nextAdaptationAllowedAt = _minSecondsBetweenAdaptations;
            _currentEaseHalfLife = DefaultEaseHalfLifeSeconds;
        }
    }
}
