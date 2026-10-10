using AdaptiveBossArena.AI.States;
using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Combat.Movement;
using AdaptiveBossArena.Combat.Feel;
using AdaptiveBossArena.Combat.Vitals;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Constants;
using AdaptiveBossArena.Core.Events;
using AdaptiveBossArena.Core.Perception;
using AdaptiveBossArena.Core.Services;
using AdaptiveBossArena.Core.StateMachine;
using AdaptiveBossArena.Learning;
using UnityEngine;

namespace AdaptiveBossArena.AI
{
    /// <summary>
    /// 组装 Boss 并驱动决策。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是整个项目的重点，值得明确说明：此类没有玩家引用，也无法获取。
    /// 其程序集不引用玩家程序集或输入包。它解析仅暴露屏幕可见信息、
    /// 声明于 Core 的 <see cref="IObservablePlayer"/>接口，
    /// 再包装为 <see cref="DelayedPerceptionSource"/>，因此读取的仍是零点几秒前的快照。
    /// </para>
    /// <para>
    /// 采样由这里按固定节奏推送，而非状态主动拉取，因此 AI 深层无论多急于行动，
    /// 都无法取得比允许范围更新的信息。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class BossController : MonoBehaviour, IDamageable, IPushBody
    {
        private const int DeathTransitionPriority = 1000;
        private const int StaggerTransitionPriority = 500;

        /// <summary>高于普通攻击，使阶段演出能够接管；低于硬直和死亡，使二者可打断它。</summary>
        private const int SetPieceTransitionPriority = 300;

        private const int ParryTransitionPriority = 200;

        /// <summary>Boss 架势崩溃时施加的硬直。</summary>
        /// <remarks>
        /// 足够长以便走近并惩罚：反击现在要求玩家在此窗口内按攻击，而非防线崩溃后自动触发。
        /// </remarks>
        private const float PoiseBreakStaggerSeconds = 1.6f;

        /// <summary>
        /// 两次观察到的挥击开始时间仍可视为同次挥击的最大差值。
        /// </summary>
        /// <remarks>
        /// 开始时间通过延迟观察做减法得出，因此跨帧有少量浮点误差。
        /// 此容差远小于两次真实挥击的最短间隔。
        /// </remarks>
        private const float SameSwingToleranceSeconds = 0.01f;

        /// <summary>适应时伴随的相机震动强度，作为发生变化的身体感提示。</summary>
        private const float AdaptationTellTrauma = 0.25f;

        [Header("Configuration")]
        [SerializeField]
        [Tooltip("Boss tuning values, phases and counter-strategy catalogue. Required.")]
        private BossConfig _config;

        [Header("Event Channels")]
        [SerializeField]
        [Tooltip("Broadcasts normalised boss health for the health bar.")]
        private FloatEventChannel _healthChannel;

        [SerializeField]
        [Tooltip("Broadcasts the zero-based phase index when the boss escalates.")]
        private IntEventChannel _phaseChannel;

        [SerializeField]
        [Tooltip("Raised once when the boss is defeated.")]
        private VoidEventChannel _defeatChannel;

        [SerializeField]
        [Tooltip("Carries the tell for a newly adopted counter-strategy, so the player can perceive " +
                 "that the boss has changed rather than suspecting it of cheating.")]
        private StringEventChannel _adaptationChannel;

        [SerializeField]
        [Tooltip("Broadcasts normalised boss posture. Emptying it opens the riposte.")]
        private FloatEventChannel _postureChannel;

        [SerializeField]
        [Tooltip("Raised when a committed boss swing whiffs hard enough to overbalance it, so " +
                 "presentation can show the stumble and the opening it leaves.")]
        private VoidEventChannel _overbalanceChannel;

        [SerializeField]
        [Tooltip("Raised when the boss parries a swing. The encounter director relays it into " +
                 "posture damage on the player.")]
        private VoidEventChannel _parrySucceededChannel;

        private CharacterController _characterController;
        private HitFlash _hitFlash;
        private CharacterAnimator _animator;

        /// <summary>驱动骨骼角色，并切换为狂暴徒手形态。</summary>
        private CharacterAnimationBridge _bridge;

        /// <summary>角色的物理身体，死亡时唤醒。基础几何体身体没有此项。</summary>
        private RagdollActivator _ragdoll;
        private PhaseAura _phaseAura;

        /// <summary>
        /// Boss 转入新阶段时忽略伤害的时长。
        /// </summary>
        /// <remarks>
        /// 通过咆哮动画和光环爆发明显预警，使其表现为强化而非漏判命中。
        /// 防止玩家在升级刚开始时直接爆发跳过该过程。使用1.3 秒而非 0.6 秒，让咆哮足够完整；
        /// 0.6秒看起来只像抽动。期间 Boss 不执行其他攻击。
        /// </remarks>
        private const float PhaseTransitionInvulnSeconds = 1.3f;

        /// <summary>巨兽丢弃剑并改为徒手战斗的阶段。</summary>
        /// <remarks>
        /// 始终使用配置列表的最后阶段，因此三阶段配置会在第三阶段狂暴，而不是永不狂暴。
        /// </remarks>
        private int FrenzyPhaseIndex =>
            _config != null && _config.Phases != null && _config.Phases.Length > 0
                ? _config.Phases.Length - 1
                : int.MaxValue;

        /// <summary>物理接管身体前，死亡动画播放的时长。</summary>
        /// <remarks>
        /// 足够长以看到巨兽完整倒下，又足够短，使结算界面淡入时尸体仍在下落。
        /// </remarks>
        private const float DeathClipSeconds = 0.9f;

        private float _phaseTransitionInvulnRemaining;

        /// <summary>布娃娃接管前剩余的死亡动画秒数，未死亡时为零。</summary>
        private float _deathClipRemaining;

        /// <summary>致命一击的信息，保留到死亡动画播完。</summary>
        private Vector3 _pendingDeathThrow;

        /// <summary>阶段升级应在可行动后引发演出冲击波时设置。</summary>
        private bool _phaseAttackQueued;

        /// <summary>结局确定后设置，使 Boss 停止战斗。</summary>
        private bool _fightConcluded;

        /// <summary>挑战修正对适应速度的倍率，一表示普通战斗。</summary>
        private float _adaptationRateScale = 1f;

        private ITimeService _time;
        private ICombatEventBus _events;
        private DelayedPerceptionSource _perception;

        private HealthPool _health;
        private PoisePool _poise;
        private BossContext _context;
        private BossAttackSelector _selector;
        private AdaptationManager _adaptation;
        private BossResolve _resolve;
        private IRandomProvider _random;
        private OverbalanceEvaluator _overbalance;
        private IScreenShake _screenShake;
        private StateMachine<BossContext> _machine;

        private BossIdleState _idleState;
        private BossObserveState _observeState;
        private BossApproachState _approachState;
        private BossAttackState _attackState;
        private BossRecoverState _recoverState;
        private BossParryState _parryState;

        /// <summary>Boss 最近一次作出招架决策的挥击开始时间。</summary>
        private float _lastConsideredSwingStart = float.NegativeInfinity;

        /// <summary>针对该挥击作出的决策，保留它以免每帧重新抽取。</summary>
        private bool _parryThisSwing;
        private BossStaggerState _staggerState;
        private BossDeadState _deadState;

        private bool _isInitialised;

        /// <inheritdoc />
        public CombatantTeam Team => CombatantTeam.Boss;

        /// <inheritdoc />
        public bool IsAlive => _health != null && _health.IsAlive;

        /// <summary>生命值池，以只读方式暴露给 HUD。</summary>
        public IHealth Health => _health;

        /// <summary>架势池，以只读方式暴露给调试工具。</summary>
        public IPoise Poise => _poise;

        /// <summary>学习系统调整的行为数值，供调试面板读取。</summary>
        public BossTuning Tuning => _context?.Tuning;

        /// <summary>当前阶段从零开始的索引。</summary>
        public int PhaseIndex => _context?.PhaseIndex ?? 0;

        /// <summary>Boss 当前状态名称，供调试工具使用。</summary>
        public string CurrentStateName => _machine?.CurrentState?.Name ?? "Uninitialised";

        /// <summary>
        /// 构建是否完成。暴露此项，因为否则失败会被静默掩盖。
        /// </summary>
        /// <remarks>
        /// <c>ValidateSetup</c> 最后三项检查返回false 而不记录日志，因此缺失服务会静默禁用 Boss：
        /// 停止攻击并忽略命中，外部看来像被动 Boss 而非损坏对象。
        /// </remarks>
        public bool IsInitialised => _isInitialised;

        /// <summary>阶段变化无敌剩余秒数，供调试工具使用。</summary>
        public float PhaseTransitionInvulnRemaining => _phaseTransitionInvulnRemaining;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _hitFlash = GetComponentInChildren<HitFlash>();
            _animator = GetComponentInChildren<CharacterAnimator>();
            _bridge = GetComponentInChildren<CharacterAnimationBridge>();
            _ragdoll = GetComponentInChildren<RagdollActivator>();
            _phaseAura = GetComponentInChildren<PhaseAura>();
        }

        private void Start()
        {
            if (!ValidateSetup())
            {
                enabled = false;
                return;
            }

            _time = ServiceRegistry.Current.Get<ITimeService>();
            _animator?.SetTime(_time);
            _events = ServiceRegistry.Current.Get<ICombatEventBus>();
            var random = ServiceRegistry.Current.Get<IRandomProvider>();
            ServiceRegistry.Current.TryGet(out _screenShake);

            var observablePlayer = ServiceRegistry.Current.Get<IObservablePlayer>();
            _perception = new DelayedPerceptionSource(
                observablePlayer, _config.ReactionProfile.PerceptionLatencySeconds);

            var motor = new BossMotor(_characterController, _config);
            _health = new HealthPool(_config.MaxHealth, CombatantTeam.Boss);
            _poise = new PoisePool(_config.MaxPoise, _config.PoiseRegenPerSecond, PoiseBreakStaggerSeconds);

            // 危险区域场可选且属于场景，因此无此对象的测试场景中 Boss 不会留下危险痕迹。
            // 只将其传给 Boss，使地面危险区域始终属于 Boss 一方。
            var hazardField = FindAnyObjectByType<Combat.HazardField>();

            var attacks = new AttackExecutor(
                transform, CombatantTeam.Boss, Layers.BossAttackMask, _time, _events, _screenShake,
                GetComponentInChildren<Combat.Feel.AttackVisualizer>(), hazardField);

            // 造成命中的刀刃、拳头、脚和身体。也包含未激活对象：砍刀狂暴时隐藏，重试后恢复。
            attacks.SetStrikers(GetComponentsInChildren<StrikeVolume>(includeInactive: true));

            var tuning = new BossTuning(
                _config.BaselinePreferredRange, _config.BaselineAggression, _config.BaselineParryChance);

            _context = new BossContext(
                _config, transform, motor, _health, _poise, attacks,
                _perception, tuning, _time, random, _events);

            attacks.Parried += OnOwnAttackParried;

            _selector = new BossAttackSelector(random);

            BuildLearning(random);
            BuildStateMachine();
            SubscribeToVitals();
            ApplyPhase(0);

            _isInitialised = true;
            _healthChannel?.Raise(_health.Normalized);
        }

        /// <summary>
        /// 组装学习循环，并订阅战斗事件。
        /// </summary>
        /// <remarks>
        /// 订阅的是战斗事件总线而非玩家。Boss像观众一样从已发生的事件获知战斗进展。
        /// </remarks>
        private void BuildLearning(IRandomProvider random)
        {
            var memory = new CombatMemory();
            var tracker = new BehaviorTracker(memory, _config.MemoryHalfLifeSeconds);

            _adaptation = new AdaptationManager(
                memory,
                tracker,
                new BehaviorProfile(),
                _context.Tuning,
                _config.CounterStrategies,
                random,
                _config.AnalysisIntervalSeconds,
                _config.MinSecondsBetweenAdaptations,
                _config.MemoryHalfLifeSeconds);

            _resolve = new BossResolve(
                _config.ResolveThreshold, _config.ResolveBuildPerSecond, _config.ResolveBuildPerWhiff);

            _random = random;
            _overbalance = new OverbalanceEvaluator(
                _config.OverbalanceMaxChance,
                _config.OverbalanceIntensityThreshold,
                _config.OverbalanceRecoverySeconds,
                _config.OverbalancePoiseMultiplier);

            _adaptation.StrategyAdopted += OnStrategyAdopted;
            _events.EventRecorded += OnCombatEventRecorded;
        }

        /// <summary>
        /// 把可观察事件送入学习循环；若是 Boss自己的攻击被避开，也送入决心系统。
        /// </summary>
        private void OnCombatEventRecorded(CombatEvent combatEvent)
        {
            _adaptation.OnCombatEvent(combatEvent);

            // Boss 挥空意味着玩家避开攻击，这份挫败会积累孤注一掷。只有 Boss 自己挥空计入，
            // 且仅用于统计，绝不直接驱动反应。
            if (combatEvent.Kind == CombatEventKind.AttackWhiffed && combatEvent.Actor == CombatantTeam.Boss)
            {
                _resolve?.RegisterBossWhiff();
                TryOverbalance();
            }
        }

        /// <summary>
        /// 判断刚挥空的高投入攻击是否使Boss 失衡并暴露踉跄空当。
        /// </summary>
        /// <remarks>
        /// 这是对 Boss <em>自己</em>动作及其<em>自己</em>投入的反应，从不读取玩家，
        /// 因此无需感知延迟也公平：玩家不再符合预测时，Boss 为过度信任该判断付出代价。
        /// 概率和强度随投入提升，尚未适应时为零，因此完全没有学习的战斗不受影响。
        /// </remarks>
        private void TryOverbalance()
        {
            if (_context.IsOverbalanced)
            {
                return;
            }

            float commitment = _context.Tuning.CommitmentIntensity;

            if (!_overbalance.ShouldOverbalance(
                    _context.LastAttackWasCommitted, commitment, _random.NextFloat01()))
            {
                return;
            }

            _context.OverbalanceSeconds = _overbalance.ExtraRecoverySeconds(commitment);
            _context.OverbalancePoiseMultiplier = _overbalance.PoiseVulnerabilityMultiplier(commitment);

            // 沿过度投入方向前倾，使挥空在身体上表现为过度伸展，而非单纯暂停。
            // 与适应时光环脉冲使用相同表现通道，放在所伴随机制旁边。
            _animator?.Recoil(_context.Motor.Facing);

            // 发出通知供导演添加尘土、音效和空当标记；机制已在上下文中生效。这里不读取玩家，
            // 也不对其作出反应。
            _overbalanceChannel?.Raise();
        }

        /// <summary>
        /// 提示新形成的应对，让玩家察觉 Boss 已改变。
        /// </summary>
        /// <remarks>
        /// 必须提供，不能省略。玩家看不到的适应会被理解为 Boss 作弊，使整个战斗设计失去意义。
        /// </remarks>
        private void OnStrategyAdopted(CounterStrategy strategy)
        {
            if (strategy == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(strategy.TellMessage))
            {
                _adaptationChannel?.Raise(strategy.TellMessage);
            }

            _screenShake?.AddTrauma(AdaptationTellTrauma);

            // 适应瞬间在 Boss 身上闪光，使变化既能从角色身体感受到，也能从屏幕顶端横幅读到。
            _phaseAura?.Pulse();
        }

        /// <summary>
        /// 战斗结局确定后使 Boss 停止战斗。
        /// </summary>
        /// <remarks>
        /// 否则 Boss 会继续挥砍尸体。自身死亡经过死亡状态，但玩家死亡对其不可见：
        /// 隔离屏障禁止直接告知玩家信息，因此导演必须叫停 Boss。动画继续运行，
        /// 保留结局节奏和败者倒下过程。
        /// </remarks>
        /// <param name="concluded">结局已经确定时为 true。</param>
        public void SetFightConcluded(bool concluded) => _fightConcluded = concluded;

        private void Update()
        {
            if (!_isInitialised || _time.IsPaused)
            {
                return;
            }

            float deltaTime = _time.DeltaTime;

            // 死亡中：先播放动画，再将携带致命一击的身体交给物理。使用游戏时间，
            // 使处决慢动作同时拉长倒下过程。
            if (_deathClipRemaining > 0f)
            {
                _deathClipRemaining -= deltaTime;

                if (_deathClipRemaining <= 0f)
                {
                    _ragdoll?.Activate(_pendingDeathThrow);
                }
            }

            // 战斗结束：停止攻击和决策，逐渐停下。下方动画仍运行，使 Boss 可见地归于稳定，
            // 而非冻结在半途姿势。
            if (_fightConcluded)
            {
                _context.Attacks.Cancel();
                _context.Motor.Decelerate(deltaTime);
                _context.Motor.Tick(deltaTime);
                DriveAnimation();
                return;
            }

            _phaseTransitionInvulnRemaining = Mathf.Max(0f, _phaseTransitionInvulnRemaining - deltaTime);
            _context.OverbalanceSeconds = Mathf.Max(0f, _context.OverbalanceSeconds - deltaTime);

            // Boss 观察玩家的唯一窗口，每帧刷新并延迟提供。下游无法请求更新数据。
            _perception.Sample(_time.CombatTime);

            _context.TickCooldowns(deltaTime);
            _poise.Tick(deltaTime);
            _machine.Tick(deltaTime);
            ResolveWallImpact();

            // 学习在状态机之后推进，使得出的习惯描述已经完成的一帧，而非正在进行的一帧。
            _adaptation.Tick(
                deltaTime, _time.CombatTime, _context.DistanceToPlayer, _context.PhaseIndex + 1);

            UpdatePhase();
            UpdateResolve(deltaTime);

            // 每帧驱动，而非依赖招架状态进入和退出，避免站姿中途被销毁后灯光残留。
            _phaseAura?.SetParryWindow(_context.ParryWindowOpen);

            DriveAnimation();
        }

        /// <summary>
        /// 积累决心，显示蓄力，充满后排入孤注一掷。
        /// </summary>
        /// <remarks>
        /// 孤注一掷使用与阶段变化相同的排队演出冲击波路径，使两个危险演出共享一项有预警、
        /// 可闪避的攻击。排队瞬间消耗决心，避免等待攻击空当时一次充满触发两次。
        /// </remarks>
        private void UpdateResolve(float deltaTime)
        {
            if (_resolve == null)
            {
                return;
            }

            _resolve.Tick(deltaTime);
            _phaseAura?.SetCharge(_resolve.Normalized);

            if (_resolve.IsReady && _config.PhaseShockwave != null)
            {
                _phaseAttackQueued = true;
                _resolve.Spend();
            }
        }

        /// <summary>将本帧可见状态传递给程序化动画器。</summary>
        /// <remarks>
        /// Boss 不维护自身可观察状态字段，隔离屏障要求发布该字段的是玩家。
        /// 因此这里从 Boss 本已使用的信号在表现层推导动画状态，这些信息不会回流到 AI。
        /// </remarks>
        private void DriveAnimation()
        {
            if (_animator == null)
            {
                return;
            }

            ObservableActionState state = ResolveAnimationState();

            float topSpeed = _context.Motor.CurrentTopSpeed;
            float speed01 = topSpeed > 0f ? _context.Motor.Speed / topSpeed : 0f;

            _animator.SetMotionState(
                state, _context.Attacks.Phase, speed01,
                _context.Attacks.CurrentAttack, _context.Attacks.ElapsedSeconds);
        }

        /// <summary>将 Boss 当前情况映射为动画器的表现状态。</summary>
        private ObservableActionState ResolveAnimationState()
        {
            if (!_health.IsAlive)
            {
                return ObservableActionState.Dead;
            }

            if (ReferenceEquals(_machine.CurrentState, _staggerState))
            {
                return ObservableActionState.Staggered;
            }

            // 仅使用窗口，而非整个站姿。尾段保持防御姿势会在防御能力已耗尽的关键瞬间仍向玩家显示举起
            // 防御，而此帧的姿势必须诚实。
            if (_context.ParryWindowOpen)
            {
                return ObservableActionState.Guarding;
            }

            // Boss 挥击有分量，因此始终采用重击姿势，比玩家轻击连招有更大蓄势和更长突进。
            if (_context.Attacks.IsRunning)
            {
                return ObservableActionState.HeavyAttacking;
            }

            return _context.Motor.IsStationary
                ? ObservableActionState.Idle
                : ObservableActionState.Moving;
        }

        /// <inheritdoc />
        public DamageResult TakeDamage(in DamageInfo damage)
        {
            if (!_isInitialised || !_health.IsAlive || damage.SourceTeam == Team)
            {
                return DamageResult.NoDamage(DamageOutcome.Ignored);
            }

            // 成功招架拒绝命中，并给 Boss 惩罚机会。仅在站姿开头窗口可触发；
            // 稍后同一站姿只是防御能力已耗尽的静止身体，会受到完整伤害。CanBlock 为 false，
            // 因为 Boss 没有备用格挡：没有擦伤伤害，没有消耗，也没有折中结果。要么时机准确，要么失败。
            if (DescribeDefence(damage) == DefenceOutcome.Deflected)
            {
                return ResolveParry();
            }

            // 阶段变形期间 Boss 短暂无敌。窗口短且预警明确，因此表现为升级，而非命中被吞掉。
            if (_phaseTransitionInvulnRemaining > 0f && !damage.IgnoresInvulnerability)
            {
                return DamageResult.NoDamage(DamageOutcome.Invulnerable);
            }

            float applied = _health.Reduce(damage.Amount, damage);
            _hitFlash?.Play();
            _animator?.Recoil(damage.HitDirection);

            // 失衡时 Boss 防线敞开，因此踉跄期间命中会加速架势击破并打开处决机会，
            // 这是诱骗预测成功后的实际奖励。
            if (_poise.ApplyPoiseDamage(damage.PoiseDamage * _context.IncomingPoiseMultiplier))
            {
                _context.RequestStagger(PoiseBreakStaggerSeconds, StaggerReason.PoiseBreak);
            }

            // 在架势伤害之后处理，使击破站姿的那一击也能推动 Boss。Boss 以前完全忽略击退。
            ApplyKnockback(damage);

            return DamageResult.Applied(applied, !_health.IsAlive);
        }

        /// <summary>沿攻击方向推动 Boss，幅度由其站姿允许的冲量决定。</summary>
        private void ApplyKnockback(in DamageInfo damage)
        {
            Vector3 direction = damage.HitDirection;
            direction.y = 0f;

            if (damage.KnockbackSpeed <= 0f || direction.sqrMagnitude < Mathf.Epsilon)
            {
                return;
            }

            float shove = KnockbackResistance.ShoveMultiplier(
                _poise.Normalized,
                _poise.IsBroken,
                _config.KnockbackResistanceAtFullPoise,
                _config.KnockbackResistanceWhenBroken);

            _context.Motor.AddImpulse(direction.normalized * (damage.KnockbackSpeed * shove));
        }

        /// <summary>
        /// 从被玩家弹反的挥击中反震。
        /// </summary>
        /// <remarks>
        /// 这是同一交锋的 Boss 侧处理，与玩家一样延迟执行：这里位于攻击执行器阶段推进循环内，
        /// 因此由请求的硬直状态清理挥击，而非当场取消。此前弹反只改变 Boss 架势，
        /// 挥击仍像无事发生般穿过防御继续，这正是弹反缺乏赢下交锋感觉的重要原因。
        /// </remarks>
        private void OnOwnAttackParried() =>
            _context.RequestStagger(
                _context.Attacks.LastParryWasStrike ? ParriedByStrikeStaggerSeconds : StaggerDurations.InterruptSeconds,
                StaggerReason.Parried);

        /// <summary>
        /// Boss 被主动招架后踉跄的时长，比普通弹反反震更长。
        /// </summary>
        /// <remarks>
        /// 足够长以便攻击，才值得承担招架投入；又足够短，使招架本身并非免费反击机会。
        /// </remarks>
        private const float ParriedByStrikeStaggerSeconds = 0.6f;

        /// <summary>
        /// 向共享裁决器询问 Boss 站姿如何应对传入攻击。
        /// </summary>
        /// <param name="damage">传入的攻击。</param>
        /// <returns>攻击对该站姿的裁决结果。</returns>
        private DefenceOutcome DescribeDefence(in DamageInfo damage) =>
            DefenceResolver.ResolveDefence(new DefenceQuery
            {
                IsDefending = _context.IsParrying,
                CanDeflect = true,
                CanBlock = false,
                TimeInDefenceSeconds = _parryState.TimeInState,
                DeflectWindowSeconds = _config.ParryWindowSeconds,
                Unblockable = damage.Unblockable,
                Unparryable = damage.Unparryable
            });

        /// <summary>
        /// 拒绝 Boss 精准迎击的攻击。
        /// </summary>
        /// <remarks>
        /// 奖励是清除攻击冷却：判断正确后 Boss 可以立即回应。此处刻意不发布事件；
        /// 返回 Deflected 会使玩家攻击执行器发布 <see cref="CombatEventKind.Parried"/>，
        /// 并将 Boss 标为防御者。若同一瞬间重复发布事件，同次交锋会出现两次特效和音效。
        /// </remarks>
        /// <returns>不含伤害的结果。</returns>
        private DamageResult ResolveParry()
        {
            _context.AttackCooldownRemaining = 0f;
            _parrySucceededChannel?.Raise();

            return DamageResult.NoDamage(DamageOutcome.Deflected);
        }

        /// <summary>重试时恢复 Boss 初始状态。</summary>
        /// <param name="spawnPosition">Boss 的放置位置。</param>
        public void ResetForNewAttempt(Vector3 spawnPosition)
        {
            if (!_isInitialised)
            {
                return;
            }

            _health.ResetToFull();
            _poise.ResetToFull();
            _perception.Reset();

            _context.Tuning.ResetToBaseline();
            _context.AttackCooldownRemaining = 0f;
            _phaseTransitionInvulnRemaining = 0f;
            _phaseAttackQueued = false;
            _fightConcluded = false;
            _context.OverbalanceSeconds = 0f;
            _context.OverbalancePoiseMultiplier = 1f;
            _context.LastAttackWasCommitted = false;
            _resolve?.Reset();
            _context.StaggerRequested = false;
            _context.IsParrying = false;
            _context.ParryWindowOpen = false;
            _context.ParryCooldownRemaining = 0f;
            _lastConsideredSwingStart = float.NegativeInfinity;
            _parryThisSwing = false;
            _context.PendingAttack = null;
            _context.Attacks.Cancel();
            _context.Motor.Teleport(spawnPosition);
            _adaptation.Reset();

            ApplyPhase(0);
            _machine.ForceState(_idleState);
            // 先把身体交回 Animator，再重置姿势，否则会修改仍归物理管理的骨骼。
            _ragdoll?.Restore();
            _animator?.ResetPose();
            _phaseAura?.ResetAura();
            _healthChannel?.Raise(_health.Normalized);
        }

        /// <summary>连接各状态和状态间转移条件。</summary>
        private void BuildStateMachine()
        {
            _idleState = new BossIdleState();
            _observeState = new BossObserveState();
            _approachState = new BossApproachState();
            _attackState = new BossAttackState();
            _recoverState = new BossRecoverState();
            _parryState = new BossParryState();
            _staggerState = new BossStaggerState();
            _deadState = new BossDeadState();

            _machine = new StateMachine<BossContext>(_context, _idleState);

            _machine.AddTransition(_idleState, _observeState, HasPerceivedPlayer);

            // 决定攻击投入前先选择攻击，使接近状态知道应靠近到何种距离。
            _machine.AddTransition(_observeState, _attackState, WantsToAttackNow, priority: 100);
            _machine.AddTransition(_observeState, _approachState, WantsToApproach, priority: 90);

            _machine.AddTransition(_approachState, _attackState, HasArrived);
            _machine.AddTransition(_approachState, _observeState, LostSightOfPlayer);

            _machine.AddTransition(_attackState, _recoverState, AttackFinished);
            _machine.AddTransition(_recoverState, _observeState, RecoveryFinished);
            _machine.AddTransition(_parryState, _observeState, ParryFinished);

            _machine.AddGlobalTransition(_deadState, IsDefeated, DeathTransitionPriority);
            _machine.AddGlobalTransition(_staggerState, IsStaggerRequested, StaggerTransitionPriority);
            _machine.AddGlobalTransition(_attackState, WantsPhaseAttack, SetPieceTransitionPriority);
            _machine.AddGlobalTransition(_parryState, WantsToParry, ParryTransitionPriority);

            _machine.AddTransition(_staggerState, _observeState, StaggerFinished);
        }

        private static bool HasPerceivedPlayer(BossContext context) => context.HasPerceivedPlayer;

        private static bool LostSightOfPlayer(BossContext context) => !context.HasPerceivedPlayer;

        private static bool IsDefeated(BossContext context) => !context.Health.IsAlive;

        private static bool IsStaggerRequested(BossContext context) => context.StaggerRequested;

        /// <summary>
        /// 决定是否攻击，并在判断过程中选择攻击。
        /// </summary>
        /// <remarks>
        /// 首先检查反应门控。无论空当多明显，Boss都不能快于反应配置允许的速度利用它；
        /// 它有时也会像人类一样完全错过空当。
        /// </remarks>
        private bool WantsToAttackNow(BossContext context)
        {
            // 变形期间不攻击：咆哮是此时节奏，仍在明显咆哮的身体不应同时挥击。选择前检查，
            // 避免无谓消耗随机抽取。
            if (_phaseTransitionInvulnRemaining > 0f)
            {
                return false;
            }

            // 只在这里选择一次。紧接着评估的接近转移读取结果而不重新选择，使单次决策只消耗一次随机值，
            // 并保持种子战斗可复现。
            if (!TryCommitToAttack(context))
            {
                return false;
            }

            AttackDefinition attack = context.PendingAttack;
            float reach = attack.EffectiveReach;

            return context.DistanceToPlayer <= reach;
        }

        /// <summary>
        /// Boss 可以出招后，执行排队中的阶段转换演出。
        /// </summary>
        /// <remarks>
        /// 等待挺身咆哮的无敌结束，使冲击波在变形节奏<em>之后</em>出现；普通攻击仍处于投入期时也等待，
        /// 避免演出打断半途挥击。与普通攻击路径一样，在投入时选择攻击，使种子战斗可复现。
        /// </remarks>
        private bool WantsPhaseAttack(BossContext context)
        {
            if (!_phaseAttackQueued ||
                _phaseTransitionInvulnRemaining > 0f ||
                context.IsOverbalanced ||
                context.PendingAttack != null ||
                _config.PhaseShockwave == null)
            {
                return false;
            }

            _phaseAttackQueued = false;
            context.PendingAttack = _config.PhaseShockwave;
            return true;
        }

        /// <summary>向已经确定使用的攻击范围靠近。</summary>
        private static bool WantsToApproach(BossContext context) => context.PendingAttack != null;

        /// <summary>
        /// 决定是否投入攻击，是则选择具体攻击。
        /// </summary>
        /// <remarks>
        /// 错过机会的随机结果会启动新的完整反应延迟，而非仅返回 false。否则 Boss 每帧重抽，
        /// 即使高失误概率也几乎立即通过，设计中的失误性便被无声抹去。
        /// 错过时付出完整反应窗口也更符合人类：漏掉空当的人不会在六十分之一秒后重新判断。
        /// </remarks>
        private bool TryCommitToAttack(BossContext context)
        {
            if (context.PendingAttack != null)
            {
                return true;
            }

            if (context.AttackCooldownRemaining > 0f ||
                context.IsOverbalanced ||
                !context.IsReactionReady ||
                !context.Perceived.IsValid)
            {
                return false;
            }

            if (context.Reaction.RollMissedOpportunity(context.Random))
            {
                context.BeginReactionDelay();
                return false;
            }

            AttackDefinition attack = _selector.Select(context);

            if (attack == null)
            {
                return false;
            }

            context.PendingAttack = attack;
            return true;
        }

        /// <summary>
        /// 决定是否招架已经观察到前摇的重击。
        /// </summary>
        /// <remarks>
        /// 根据已感知、因此滞后的动作状态门控。Boss 响应的是看见已开始的前摇，而非玩家刚作出的决定。
        /// 尝试意愿来自学习后的招架概率，玩家提供理由前为零。
        /// </remarks>
        private bool WantsToParry(BossContext context)
        {
            if (context.AttackCooldownRemaining > 0f || context.ParryCooldownRemaining > 0f)
            {
                return false;
            }

            float parryChance = context.Tuning.Get(BossTuningParameter.ParryChance);

            if (parryChance <= 0f)
            {
                return false;
            }

            PlayerObservation observation = context.Perceived;

            if (!observation.IsValid || observation.ActionState != ObservableActionState.HeavyAttacking)
            {
                return false;
            }

            // 只有玩家近到确实可能挥到 Boss 时才值得尝试。
            if (context.DistanceToPlayer > context.PreferredRange + 2f)
            {
                return false;
            }

            // 每次挥击只决定一次，而非每帧一次。Boss具备资格时状态机每帧评估此谓词，若在这里反复抽取，
            // 概率会叠加：重击可见约五十帧，即使单帧概率很小也几乎必然，Boss 会招架所有重击。
            // 用观察到的动作开始时间而非每帧增加的观察时间戳识别挥击，确保一次挥击只抽一次。
            if (Mathf.Abs(observation.ActionStartedAt - _lastConsideredSwingStart) >
                SameSwingToleranceSeconds)
            {
                _lastConsideredSwingStart = observation.ActionStartedAt;
                _parryThisSwing = context.Random.NextBool(parryChance);
            }

            return _parryThisSwing;
        }

        private bool HasArrived(BossContext context) => _approachState.HasArrived(context);

        private bool AttackFinished(BossContext context) => _attackState.IsComplete(context);

        private bool RecoveryFinished(BossContext context) => _recoverState.IsComplete(context);

        private bool ParryFinished(BossContext context) => _parryState.IsComplete(context);

        private bool StaggerFinished(BossContext context) => _staggerState.IsComplete(context);

        /// <summary>生命值越过阶段阈值时升级 Boss。</summary>
        private void UpdatePhase()
        {
            int resolved = _config.ResolvePhaseIndex(_health.Normalized);

            if (resolved != _context.PhaseIndex)
            {
                ApplyPhase(resolved);
            }
        }

        /// <summary>应用阶段移动倍率与适应速度，然后发出提示。</summary>
        private void ApplyPhase(int phaseIndex)
        {
            // 赋值前捕获旧值，以区分真正升级、初始阶段以及重置为零阶段，后两者不应触发爆发。
            bool isEscalation = phaseIndex > _context.PhaseIndex;

            _context.PhaseIndex = phaseIndex;
            _context.Motor.SetSpeedMultiplier(_context.CurrentPhase.MoveSpeedMultiplier);

            // 后期阶段学习更快，使战斗升级而不赋予 Boss额外信息。快速学习挑战倍率在此合入，
            // 确保每次阶段变化后仍保留，而不被覆盖。
            _adaptation?.SetAdaptationRateMultiplier(
                _context.CurrentPhase.AdaptationRateMultiplier * _adaptationRateScale);

            _phaseAura?.SetPhase(phaseIndex);

            // 最后阶段徒手战斗：改变身体表现，而非仅提升数值。
            _bridge?.SetFrenzy(phaseIndex >= FrenzyPhaseIndex);

            if (isEscalation)
            {
                // 变形节奏：挺身、光环爆发，并短暂忽略攻击。咆哮和清场冲击波由阶段通道监听者触发；
                // 这里只管理属于 Boss 自身的内容。
                _animator?.Flourish(PhaseTransitionInvulnSeconds);
                _phaseAura?.Pulse();
                _phaseTransitionInvulnRemaining = PhaseTransitionInvulnSeconds;

                // 变形现在是威胁，而非单纯灯光演出：挺身结束后 Boss爆发演出冲击波，
                // 使阶段进入成为需要生存的考验。
                _phaseAttackQueued = _config.PhaseShockwave != null;
            }

            _phaseChannel?.Raise(phaseIndex);
        }

        /// <summary>将资源池变化转发到界面监听的事件通道。</summary>
        private void SubscribeToVitals()
        {
            _health.Changed += args => _healthChannel?.Raise(args.Normalized);
            _poise.Changed += args => _postureChannel?.Raise(args.Normalized);
            _health.Died += _ => _defeatChannel?.Raise();

            // 先动画，后物理身体。死亡当帧交给物理会让巨兽死亡动画完全不可见，只是直接折倒。
            // 保留击飞信息，动画播放后再应用，使倒下仍表现为致命一击造成。
            _health.Died += args =>
            {
                _pendingDeathThrow = args.FinalBlow.HitDirection.normalized * args.FinalBlow.KnockbackSpeed;
                _deathClipRemaining = DeathClipSeconds;
            };
        }

        /// <summary>清晰报告依赖缺失，而非通过空引用失败。</summary>
        private bool ValidateSetup()
        {
            if (_config == null)
            {
                Debug.LogError(
                    "[Adaptive Boss Arena] BossController has no BossConfig assigned and has been disabled.",
                    this);
                return false;
            }

            if (_config.ReactionProfile == null)
            {
                // 没有反应配置时，Boss 会在感知到事件的同帧行动，这正是项目要避免的行为，
                // 因此拒绝运行是正确处理。
                Debug.LogError(
                    "[Adaptive Boss Arena] BossConfig has no ReactionProfile. The boss has been " +
                    "disabled rather than allowed to react instantly.", this);
                return false;
            }

            if (!ServiceRegistry.Current.TryGet(out IObservablePlayer _))
            {
                Debug.LogError(
                    "[Adaptive Boss Arena] No IObservablePlayer is registered. The boss has nothing to " +
                    "perceive and has been disabled.", this);
                return false;
            }

            return ServiceRegistry.Current.TryGet(out ITimeService _) &&
                   ServiceRegistry.Current.TryGet(out ICombatEventBus _) &&
                   ServiceRegistry.Current.TryGet(out IRandomProvider _);
        }

        /// <summary>设置配置，供预制体生成器使用。</summary>
        /// <param name="config">Boss 调参值。</param>
        public void SetConfig(BossConfig config) => _config = config;

        /// <summary>
        /// 在每阶段适应速度基础上进一步缩放，用于快速学习挑战修正。
        /// </summary>
        /// <remarks>
        /// 只缩放现有速度，不增加能力：Boss 从相同证据得到相同结论，只是更早。在下一次阶段变化重新应用，
        /// 因此贯穿整场战斗。
        /// </remarks>
        /// <param name="scale">适应速度倍率，一保持战斗不变。</param>
        public void SetAdaptationRateScale(float scale)
        {
            _adaptationRateScale = Mathf.Max(0.1f, scale);

            if (_adaptation != null && _context != null)
            {
                _adaptation.SetAdaptationRateMultiplier(
                    _context.CurrentPhase.AdaptationRateMultiplier * _adaptationRateScale);
            }
        }

        /// <summary>设置事件通道，供预制体生成器使用。</summary>
        /// <param name="health">归一化 Boss 生命通道。</param>
        /// <param name="phase">阶段索引通道。</param>
        /// <param name="defeat">Boss 被击败通道。</param>
        /// <param name="adaptation">适应提示通道。</param>
        /// <param name="posture">归一化架势通道。</param>
        public void SetChannels(
            FloatEventChannel health,
            IntEventChannel phase,
            VoidEventChannel defeat,
            StringEventChannel adaptation,
            FloatEventChannel posture,
            VoidEventChannel overbalance,
            VoidEventChannel parried)
        {
            _healthChannel = health;
            _phaseChannel = phase;
            _defeatChannel = defeat;
            _adaptationChannel = adaptation;
            _postureChannel = posture;
            _overbalanceChannel = overbalance;
            _parrySucceededChannel = parried;
        }

        /// <summary>
        /// 施加被弹反攻击造成的架势伤害。
        /// </summary>
        /// <remarks>
        /// 玩家精准弹反时由战斗导演调用。此方法集中表达整个弹反收益：
        /// 每次及时举防御都会让 Boss 更接近架势崩溃，使坚守位置成为进展而非只是存活。
        /// </remarks>
        /// <param name="postureDamage">要扣除的架势。</param>
        public void ApplyDeflectPosture(float postureDamage)
        {
            if (!_isInitialised || _poise == null)
            {
                return;
            }

            if (_poise.ApplyPoiseDamage(postureDamage))
            {
                _context.RequestStagger(PoiseBreakStaggerSeconds, StaggerReason.PoiseBreak);
            }
        }

        /// <summary>Boss 成功招架对玩家造成的架势伤害。</summary>
        /// <remarks>
        /// 与 <c>PlayerController.DeflectPostureDamage</c> 完全镜像，战斗导演读取它的原因相同：
        /// 双方不能持有彼此引用，因此行动者报告所做事情，导演决定对另一方的意义。
        /// </remarks>
        public float ParryPostureDamage => _config != null ? _config.ParryPostureDamage : 0f;

        /// <summary>当前判定窗口打开的挥击范围，无则为零。</summary>
        /// <remarks>
        /// 刻意比暴露攻击执行器更窄。战斗导演检测刀刃相撞仅需知道刀刃是否有效以及范围；
        /// 传入执行器还会暴露 Cancel，而外部取消攻击正是 Stage D 约束要控制的操作。
        /// </remarks>
        public float LiveSwingReach =>
            _isInitialised &&
            _context.Attacks.IsRunning &&
            _context.Attacks.Phase == AttackPhase.Active &&
            _context.Attacks.CurrentAttack != null
                ? _context.Attacks.CurrentAttack.Range
                : 0f;

        /// <summary>
        /// 正在进行的挥击与另一刀刃相撞时拒绝它。
        /// </summary>
        /// <remarks>
        /// 沿招架使用的相同硬直请求路径，理由也相同：延迟到下次更新，由攻击状态退出清理挥击。
        /// 导演LateUpdate 位于所有执行器阶段循环之外，实际上同步取消也安全，
        /// 但现有路径已对双方表现正确，无需保留两种停止挥击的方法。
        /// </remarks>
        public void RecoilFromClash()
        {
            if (!_isInitialised)
            {
                return;
            }

            _context.RequestStagger(StaggerDurations.InterruptSeconds, StaggerReason.Parried);
        }

        /// <summary>
        /// 惩罚撞墙结束的突进。
        /// </summary>
        /// <remarks>
        /// 只处理突进。Boss 走进柱子不算玩家可利用的失误，但玩家侧移诱使冲锋撞墙正是战斗奖励的判断。
        /// 与其他中断一样，通过延迟硬直请求处理。
        /// </remarks>
        private void ResolveWallImpact()
        {
            AttackDefinition attack = _context.Attacks.CurrentAttack;

            if (_context.Motor.WallImpactSpeed < _config.WallImpactSpeed ||
                _context.StaggerRequested ||
                !_context.Attacks.IsRunning ||
                attack == null ||
                attack.LungeSpeed <= 0f)
            {
                return;
            }

            _context.PublishCombatEvent(CombatEventKind.WallImpact);

            bool broken = _poise.ApplyPoiseDamage(_config.WallImpactPoiseDamage);

            _context.RequestStagger(
                broken ? PoiseBreakStaggerSeconds : StaggerDurations.InterruptSeconds,
                broken ? StaggerReason.PoiseBreak : StaggerReason.Hit);
        }

        /// <inheritdoc />
        public Vector3 BodyPosition => transform.position;

        /// <inheritdoc />
        public float BodyRadius => _characterController != null
            ? _characterController.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z)
            : 0f;

        /// <inheritdoc />
        public float BodyMass => _config != null ? _config.BodyMass : 1f;

        /// <inheritdoc />
        public bool IsSolid => _isInitialised && _characterController != null && _characterController.enabled;

        /// <inheritdoc />
        public void Displace(Vector3 offset)
        {
            if (_isInitialised)
            {
                _context.Motor.Displace(offset);
            }
        }

        /// <summary>暴露学习循环，使调试面板能显示 Boss 的判断。</summary>
        /// <returns>适应管理器，初始化前为 null。</returns>
        public AdaptationManager GetAdaptation() => _adaptation;

        private void OnDestroy()
        {
            if (_events != null)
            {
                _events.EventRecorded -= OnCombatEventRecorded;
            }
        }

        /// <summary>暴露感知源供学习系统共享。</summary>
        /// <returns>Boss 的延迟玩家视图。</returns>
        public IPerceptionSource GetPerceptionSource() => _perception;

        /// <summary>暴露上下文供学习系统调整数值。</summary>
        /// <returns>Boss 上下文，初始化前为 null。</returns>
        public BossContext GetContext() => _context;
    }
}
