using System;
using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Combat.Vitals;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Perception;
using AdaptiveBossArena.Core.Services;
using AdaptiveBossArena.Learning;
using AdaptiveBossArena.Utilities;
using UnityEngine;

namespace AdaptiveBossArena.AI
{
    /// <summary>
    /// 传递给每个 Boss 状态的共享状态与服务。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 注意此上下文持有的是 <see cref="IPerceptionSource"/>而非玩家引用。状态关于玩家的全部问题，
    /// 如位置、距离和可见动作，均由刻意滞后的快照回答。Boss 程序集无法引用玩家程序集，
    /// 因此没有其他答案来源。
    /// </para>
    /// <para>
    /// 这也解释了下面便捷属性为何均采用合理降级。战斗刚开始、历史尚不足以满足感知延迟时，
    /// Boss 确实一无所知，但仍需合理行动。
    /// </para>
    /// </remarks>
    public sealed class BossContext
    {
        /// <summary>Boss 尚未感知玩家时返回的距离。</summary>
        private const float UnknownDistance = float.MaxValue;

        /// <summary>创建上下文。</summary>
        /// <param name="config">Boss 调参值和阶段定义。</param>
        /// <param name="transform">Boss 的 Transform。</param>
        /// <param name="motor">移动驱动器。</param>
        /// <param name="health">生命值池。</param>
        /// <param name="poise">架势池。</param>
        /// <param name="attacks">攻击执行器。</param>
        /// <param name="perception">玩家的延迟视图。</param>
        /// <param name="tuning">学习系统调整的行为数值。</param>
        /// <param name="time">提供战斗时钟的时间服务。</param>
        /// <param name="random">可设置种子的随机源，使概率行为可复现。</param>
        /// <param name="events">用于发布可观察事件的总线。</param>
        /// <exception cref="ArgumentNullException">任意依赖缺失时抛出。</exception>
        public BossContext(
            BossConfig config,
            Transform transform,
            BossMotor motor,
            HealthPool health,
            PoisePool poise,
            AttackExecutor attacks,
            IPerceptionSource perception,
            BossTuning tuning,
            ITimeService time,
            IRandomProvider random,
            ICombatEventBus events)
        {
            Config = config != null ? config : throw new ArgumentNullException(nameof(config));
            Transform = transform != null ? transform : throw new ArgumentNullException(nameof(transform));
            Motor = motor ?? throw new ArgumentNullException(nameof(motor));
            Health = health ?? throw new ArgumentNullException(nameof(health));
            Poise = poise ?? throw new ArgumentNullException(nameof(poise));
            Attacks = attacks ?? throw new ArgumentNullException(nameof(attacks));
            Perception = perception ?? throw new ArgumentNullException(nameof(perception));
            Tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
            Time = time ?? throw new ArgumentNullException(nameof(time));
            Random = random ?? throw new ArgumentNullException(nameof(random));
            Events = events ?? throw new ArgumentNullException(nameof(events));
        }

        /// <summary>Boss 调参值和阶段定义。</summary>
        public BossConfig Config { get; }

        /// <summary>Boss 的 Transform。</summary>
        public Transform Transform { get; }

        /// <summary>移动驱动器。</summary>
        public BossMotor Motor { get; }

        /// <summary>生命值池。</summary>
        public HealthPool Health { get; }

        /// <summary>架势池。</summary>
        public PoisePool Poise { get; }

        /// <summary>攻击执行器。</summary>
        public AttackExecutor Attacks { get; }

        /// <summary>刻意延迟的玩家视图。</summary>
        public IPerceptionSource Perception { get; }

        /// <summary>学习系统调整的行为数值。</summary>
        public BossTuning Tuning { get; }

        /// <summary>提供战斗时钟的时间服务。</summary>
        public ITimeService Time { get; }

        /// <summary>可设置种子的随机源。</summary>
        public IRandomProvider Random { get; }

        /// <summary>用于发布可观察事件的总线。</summary>
        public ICombatEventBus Events { get; }

        /// <summary>当前阶段从零开始的索引。</summary>
        public int PhaseIndex { get; set; }

        /// <summary>下次进入攻击状态时应执行的攻击。</summary>
        public AttackDefinition PendingAttack { get; set; }

        /// <summary>
        /// 正在进行的挥击是否为有投入的行动，如突进、不可格挡攻击或连招后续段，而非轻微试探。
        /// </summary>
        /// <remarks>
        /// 每次挥击开始时设置，挥空时读取，使失衡只在Boss 确实过度投入后打开破绽。试探挥空没有代价，
        /// 让空当成为诱出真正攻击的奖励。
        /// </remarks>
        public bool LastAttackWasCommitted { get; set; }

        /// <summary>高投入挥空导致失衡后剩余的踉跄秒数。</summary>
        public float OverbalanceSeconds { get; set; }

        /// <summary>
        /// Boss 失衡时捕获的承受架势伤害倍率，踉跄期间应用。
        /// </summary>
        public float OverbalancePoiseMultiplier { get; set; } = 1f;

        /// <summary>Boss 因失衡挥击而踉跄、可被惩罚时为 true。</summary>
        public bool IsOverbalanced => OverbalanceSeconds > 0f;

        /// <summary>
        /// 当前承受架势伤害的倍率，失衡踉跄时增大，其他时候为一。
        /// </summary>
        public float IncomingPoiseMultiplier =>
            OverbalanceSeconds > 0f ? Mathf.Max(1f, OverbalancePoiseMultiplier) : 1f;

        /// <summary>Boss 再次攻击前剩余的秒数。</summary>
        public float AttackCooldownRemaining { get; set; }

        /// <summary>命中应中断 Boss 时设置。</summary>
        public bool StaggerRequested { get; set; }

        /// <summary>请求中断的时长。</summary>
        public float RequestedStaggerSeconds { get; set; }

        /// <summary>请求中断的原因。</summary>
        public StaggerReason RequestedStaggerReason { get; set; }

        /// <summary>Boss 已投入一次招架尝试期间为 true。</summary>
        public bool IsParrying { get; set; }

        /// <summary>
        /// 只有此刻到达的攻击会被完全拒绝时才为 true。
        /// </summary>
        /// <remarks>
        /// 范围小于在可惩罚尾段仍为 true 的 <see cref="IsParrying"/>。
        /// 区分二者使站姿不再代表半秒无敌，而成为需要判断的行动：窗口是玩家应避开的部分，
        /// 尾段是判断成功后的奖励。
        /// </remarks>
        public bool ParryWindowOpen { get; set; }

        /// <summary>Boss 再次尝试招架前剩余的时间。</summary>
        /// <remarks>
        /// 与 <see cref="AttackCooldownRemaining"/>分开，
        /// 因为成功招架会清除攻击冷却并给 Boss 惩罚机会。
        /// 共用计时器会使成功招架同时重新准备下一次招架，连续招架的 Boss 将完全无法攻击。
        /// </remarks>
        public float ParryCooldownRemaining { get; set; }

        /// <summary>当前反应延迟结束时的战斗时钟时间。</summary>
        public float ReactionReadyAt { get; set; }

        /// <summary>当前生效的阶段定义。</summary>
        public BossPhaseDefinition CurrentPhase =>
            Config.Phases != null && Config.Phases.Length > 0
                ? Config.Phases[Mathf.Clamp(PhaseIndex, 0, Config.Phases.Length - 1)]
                : default;

        /// <summary>Boss 每次决策的人类合理性限制。</summary>
        public ReactionProfile Reaction => Config.ReactionProfile;

        /// <summary>Boss 已看到足够信息可据以行动时为 true。</summary>
        public bool HasPerceivedPlayer => Perception.TryGetPerceived(out _);

        /// <summary>
        /// Boss 允许用于行动的最新玩家快照。
        /// </summary>
        /// <remarks>
        /// 始终落后一个配置的感知延迟。此属性让佯攻有效：Boss 在延迟窗口内作出的投入无法撤回。
        /// </remarks>
        public PlayerObservation Perceived
        {
            get
            {
                Perception.TryGetPerceived(out PlayerObservation observation);
                return observation;
            }
        }

        /// <summary>与上次观察到的玩家位置的水平距离。</summary>
        public float DistanceToPlayer
        {
            get
            {
                PlayerObservation observation = Perceived;
                return observation.IsValid
                    ? MathUtil.PlanarDistance(Transform.position, observation.Position)
                    : UnknownDistance;
            }
        }

        /// <summary>指向上次观察到的玩家位置的单位方向。</summary>
        public Vector3 DirectionToPlayer
        {
            get
            {
                PlayerObservation observation = Perceived;

                if (!observation.IsValid)
                {
                    return Transform.forward;
                }

                Vector3 delta = observation.Position - Transform.position;
                delta.y = 0f;

                return delta.sqrMagnitude > Mathf.Epsilon ? delta.normalized : Transform.forward;
            }
        }

        /// <summary>适应后 Boss 当前希望保持的距离。</summary>
        public float PreferredRange => Tuning.Get(BossTuningParameter.PreferredRange);

        /// <summary>Boss 反应延迟已结束，可以行动时为 true。</summary>
        public bool IsReactionReady => Time.CombatTime >= ReactionReadyAt;

        /// <summary>启动反应延迟，避免 Boss 响应快于人类。</summary>
        public void BeginReactionDelay() =>
            ReactionReadyAt = Time.CombatTime + Reaction.RollReactionDelay(Random);

        /// <summary>记录一次命中应中断 Boss。</summary>
        /// <param name="durationSeconds">中断应持续的时长。</param>
        public void RequestStagger(float durationSeconds) =>
            RequestStagger(durationSeconds, StaggerReason.Hit);

        /// <summary>
        /// 请求中断，并说明原因。
        /// </summary>
        /// <remarks>
        /// 原因随请求传递，因为硬直状态会发出提示；若把招架反震报告成架势击破，
        /// 会错误告知玩家防线已崩溃。已有中断期间收到新请求时保留更严重的原因，
        /// 避免同帧较轻事件掩盖更重要的事实。
        /// </remarks>
        /// <param name="durationSeconds">中断持续的时长。</param>
        /// <param name="reason">引发中断的原因。</param>
        public void RequestStagger(float durationSeconds, StaggerReason reason)
        {
            StaggerRequested = true;
            RequestedStaggerSeconds = Mathf.Max(0f, durationSeconds);
            RequestedStaggerReason = reason;
        }

        /// <summary>推进不受当前状态限制的计时器。</summary>
        /// <param name="deltaTime">经过的缩放时间。</param>
        public void TickCooldowns(float deltaTime)
        {
            if (AttackCooldownRemaining > 0f)
            {
                AttackCooldownRemaining = Mathf.Max(0f, AttackCooldownRemaining - deltaTime);
            }

            if (ParryCooldownRemaining > 0f)
            {
                ParryCooldownRemaining = Mathf.Max(0f, ParryCooldownRemaining - deltaTime);
            }
        }

        /// <summary>发布位于 Boss 位置的简单事件。</summary>
        /// <param name="kind">发生的事件。</param>
        public void PublishCombatEvent(CombatEventKind kind) =>
            Events.Publish(new CombatEvent
            {
                Kind = kind,
                Actor = CombatantTeam.Boss,
                Timestamp = Time.CombatTime,
                Position = Transform.position,
                Direction = Transform.forward,
                SeparationDistance = DistanceToPlayer
            });
    }
}
