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
using AdaptiveBossArena.Player.Controls;
using AdaptiveBossArena.Player.Movement;
using AdaptiveBossArena.Player.States;
using UnityEngine;

namespace AdaptiveBossArena.Player
{
    /// <summary>
    /// 组装玩家角色并驱动状态机。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 角色由一个组件持有多个普通 C# 协作者，而非一组在运行时互相查找的组件。移动、生命、
    /// 体力和输入缓存均可脱离场景测试，预制体也足够简单，可由代码生成。
    /// </para>
    /// <para>
    /// 此类型实现角色与游戏其余部分的两个契约：<see cref="IDamageable"/>决定传入攻击是否命中，
    /// <see cref="IObservablePlayer"/>则是 Boss 获取玩家信息的唯一通道。
    /// Boss能知道的一切均经过 <see cref="CaptureObservation"/>，因此评估 Boss 是否公平时，
    /// 应重点审查该方法。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour, IDamageable, IObservablePlayer, IPushBody
    {
        /// <summary>死亡转移优先级，必须高于全部其他条件。</summary>
        private const int DeathTransitionPriority = 1000;

        /// <summary>受硬直转移优先级。</summary>
        private const int StaggerTransitionPriority = 500;

        /// <summary>
        /// 击倒和击飞优先于同次攻击较轻的硬直反应，但低于死亡。
        /// </summary>
        private const int ReactionTransitionPriority = 600;

        /// <summary>开始闪避的转移优先级。</summary>
        private const int DashTransitionPriority = 100;

        /// <summary>开始攻击的转移优先级，刻意低于闪避。</summary>
        private const int AttackTransitionPriority = 90;

        /// <summary>开始治疗的转移优先级，属于最不紧急的动作。</summary>
        private const int HealTransitionPriority = 80;

        /// <summary>完美闪避奖励的时间倍率。</summary>
        /// <remarks>
        /// 减慢而非冻结。玩家需要看清空当并行动，因此窗口必须可操作，而非只是戏剧效果。
        /// </remarks>
        private const float PerfectDodgeTimeScale = 0.35f;

        /// <summary>精准弹反的冻结时间。长于普通命中，因为此刻值得强调。</summary>
        private const float DeflectHitStopSeconds = 0.12f;

        /// <summary>精准弹反的相机震动强度。</summary>
        private const float DeflectTrauma = 0.42f;

        /// <summary>举起防御的转移优先级，高于攻击但低于闪避。</summary>
        private const int GuardTransitionPriority = 95;

        /// <summary>
        /// 主动招架的转移优先级，略高于防御。
        /// </summary>
        /// <remarks>
        /// 按住格挡后又按招架，表示玩家想主动招架：这是更冒险、更明确的选择，因此优先。
        /// 仍低于作为紧急逃生按钮的闪避。
        /// </remarks>
        private const int ParryTransitionPriority = 96;

        /// <summary>跪倒 Boss 进入反击范围所需的玩家距离。</summary>
        private const float RiposteReachMetres = 2.8f;

        /// <summary>玩家需多正对 Boss 才能惩罚，约在 60 度以内。</summary>
        private const float RiposteFacingDot = 0.5f;

        /// <summary>主动招架的冻结时间，因难度更高而长于弹反。</summary>
        private const float ParryHitStopSeconds = 0.14f;

        /// <summary>招架赢得的节奏窗口中世界运行速度。</summary>
        private const float ParrySlowMotionScale = 0.5f;

        /// <summary>该窗口持续的真实秒数。</summary>
        private const float ParrySlowMotionSeconds = 0.25f;

        /// <summary>主动招架的相机震动强度。</summary>
        private const float ParryTrauma = 0.5f;

        /// <summary>
        /// 主动招架削减的 Boss 架势，普通弹反削减 28。
        /// </summary>
        /// <remarks>
        /// 两次招架加一次弹反可击破一百点架势，这是选择有投入应对而非安全选择的奖励。
        /// </remarks>
        private const float ParryPostureDamage = 45f;

        /// <summary>反击的转移优先级，高于全部普通动作。</summary>
        private const int RiposteTransitionPriority = 300;

        [Header("Configuration")]
        [SerializeField]
        [Tooltip("Character tuning values. Required.")]
        private PlayerConfig _config;

        [Header("Scene References")]
        [SerializeField]
        [Tooltip("Camera used to interpret movement input. Falls back to the main camera when empty.")]
        private Transform _cameraTransform;

        [Header("Event Channels")]
        [SerializeField]
        [Tooltip("Broadcasts normalised health for the heads-up display.")]
        private FloatEventChannel _healthChannel;

        [SerializeField]
        [Tooltip("Broadcasts normalised stamina for the heads-up display.")]
        private FloatEventChannel _staminaChannel;

        [SerializeField]
        [Tooltip("Raised once when the player is defeated.")]
        private VoidEventChannel _deathChannel;

        [SerializeField]
        [Tooltip("Raised on a perfect dodge, for the flourish that tells the player they nailed it.")]
        private VoidEventChannel _perfectDodgeChannel;

        [SerializeField]
        [Tooltip("Raised on a clean deflect, driving the metallic ring and spark.")]
        private VoidEventChannel _deflectChannel;

        [SerializeField]
        [Tooltip("Broadcasts normalised player posture for the guard bar.")]
        private FloatEventChannel _postureChannel;

        [SerializeField]
        [Tooltip("Broadcasts normalised focus for the focus bar.")]
        private FloatEventChannel _focusChannel;

        [SerializeField]
        [Tooltip("Carries the name of a newly drawn weapon, shown briefly on screen.")]
        private StringEventChannel _weaponChannel;

        [SerializeField]
        [Tooltip("Carries the drawn weapon's swing cue, so each weapon sounds like itself.")]
        private StringEventChannel _weaponSwingCueChannel;

        /// <summary>训练模式玩家生命永远不会低于的下限。</summary>
        private const float TrainingHealthFloor = 1f;

        private CharacterController _characterController;
        private HitFlash _hitFlash;
        private CharacterAnimator _animator;

        /// <summary>角色的物理身体，死亡时唤醒。基础几何体身体没有此项。</summary>
        private RagdollActivator _ragdoll;
        private WeaponSocket _weaponSocket;

        // 挑战修正状态，由战斗导演在挑战开始时应用。默认值不改变普通战斗。
        private float _incomingDamageMultiplier = 1f;
        private bool _immortal;
        private IPlayerInput _input;
        private ITimeService _time;
        private ICombatEventBus _events;

        private HealthPool _health;
        private StaminaPool _stamina;
        private PlayerContext _context;
        private StateMachine<PlayerContext> _machine;

        private PlayerIdleState _idleState;
        private PlayerMoveState _moveState;
        private PlayerDashState _dashState;
        private PlayerAttackState _attackState;
        private PlayerHealState _healState;
        private PlayerParryState _parryState;
        private PlayerParryStrikeState _parryStrikeState;
        private PlayerRiposteState _riposteState;
        private PlayerStaggerState _staggerState;
        private PlayerDeadState _deadState;
        private PlayerAirborneState _airborneState;
        private PlayerKnockedDownState _knockedDownState;
        private PlayerGetUpState _getUpState;

        private PoisePool _posture;
        private FocusMeter _focus;
        private IScreenShake _screenShake;

        /// <summary>
        /// 对手，仅用于让防御和反击面向目标。
        /// </summary>
        /// <remarks>
        /// 只有 Transform，没有更多信息。玩家可以看向Boss；反向关系才是整个架构禁止的内容，且仍然禁止。
        /// </remarks>
        private Transform _threat;

        /// <summary>配置武器列表的索引。</summary>
        private int _weaponIndex;

        private bool _isInitialised;

        /// <inheritdoc />
        public CombatantTeam Team => CombatantTeam.Player;

        /// <inheritdoc />
        public bool IsAlive => _health != null && _health.IsAlive;

        /// <summary>生命值池，以只读方式暴露给 HUD。</summary>
        public IHealth Health => _health;

        /// <summary>体力池，以只读方式暴露给 HUD。</summary>
        public IStaminaPool Stamina => _stamina;

        /// <summary>角色当前状态名称，供调试工具使用。</summary>
        public string CurrentStateName => _machine?.CurrentState?.Name ?? "Uninitialised";

        /// <summary>
        /// 构建是否完成。暴露此项，因为否则失败会被静默掩盖。
        /// </summary>
        /// <remarks>
        /// 初始化提前退出的控制器仍会响应 <see cref="TakeDamage"/>，只是忽略每次攻击，
        /// 因此外部看来与避开所有攻击的玩家无法区分。诊断面板必须能区分两者。
        /// </remarks>
        public bool IsInitialised => _isInitialised;

        /// <summary>闪避无敌帧生效时为 true，供调试工具使用。</summary>
        public bool IsInvulnerable => _context != null && _context.IsInvulnerable;

        /// <summary>防御举起时为 true，供调试工具使用。</summary>
        public bool IsGuarding => _context != null && _context.IsGuarding;

        /// <summary>训练模式让玩家保持不死时为 true，供调试工具使用。</summary>
        public bool IsImmortal => _immortal;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _input = GetComponent<PlayerInputReader>();
            _hitFlash = GetComponentInChildren<HitFlash>();
            _animator = GetComponentInChildren<CharacterAnimator>();
            _ragdoll = GetComponentInChildren<RagdollActivator>();
            _weaponSocket = GetComponentInChildren<WeaponSocket>();

            // Awake 期间注册，使 Boss 可在Start 解析。此注册是 Boss 接触玩家的完整接口，
            // 仅暴露屏幕上可见信息。
            ServiceRegistry.Current.RegisterOrReplace<IObservablePlayer>(this);
        }

        /// <summary>
        /// 所有其他对象的 <c>Awake</c> 运行后构建角色。
        /// </summary>
        /// <remarks>
        /// 延后到 <c>Start</c>，因为游戏启动器在 <c>Awake</c> 注册时间服务，
        /// 而 Unity 保证全部 <c>Awake</c> 先于全部 <c>Start</c>。
        /// </remarks>
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
            ServiceRegistry.Current.TryGet(out _screenShake);
            IScreenShake screenShake = _screenShake;

            var motor = new PlayerMotor(_characterController, _config, ResolveCameraTransform());
            _health = new HealthPool(_config.MaxHealth, CombatantTeam.Player);
            _stamina = new StaminaPool(
                _config.MaxStamina, _config.StaminaRegenPerSecond, _config.StaminaRegenDelaySeconds);

            // 复用 Boss 架势实现。玩家架势与 Boss 韧性是同一机制的两面，
            // 共享实现确保防御崩溃与硬直行为一致。
            _posture = new PoisePool(
                _config.MaxPosture, _config.PostureRegenPerSecond, StaggerDurations.BreakSeconds);

            // 通过良好防御获得，并用于强化特殊技能。初始为空：它奖励战斗中读懂 Boss，而非开场赠送资源。
            _focus = new FocusMeter(
                _config.MaxFocus, _config.FocusPerDeflect, _config.FocusPerPerfectDodge);

            var attacks = new AttackExecutor(
                transform, CombatantTeam.Player, Layers.PlayerAttackMask, _time, _events, screenShake,
                GetComponentInChildren<AttackVisualizer>());

            // 注册剑，使斩击命中刀刃真正经过的位置。
            attacks.SetStrikers(GetComponentsInChildren<StrikeVolume>(includeInactive: true));

            _context = new PlayerContext(
                _config, motor, _health, _stamina, _input, new InputBuffer(), _time, attacks, _events);

            _context.Reactions = new ReactionGate(_config.KnockdownImmunitySeconds);

            attacks.Parried += OnOwnAttackParried;

            BuildStateMachine();
            SubscribeToVitals();

            if (_config.Weapons != null && _config.Weapons.Length > 0)
            {
                EquipWeapon(_config.Weapons[0]);
            }

            _isInitialised = true;
            PublishVitals();
        }

        private void Update()
        {
            if (!_isInitialised)
            {
                return;
            }

            // 暂停时战斗时钟冻结，此时记录的按键永不从缓存中过期，并会在恢复瞬间触发。
            // 完全跳过更新比推理停住的时钟更简单安全。
            if (_time.IsPaused)
            {
                return;
            }

            float deltaTime = _time.DeltaTime;

            // 状态机运行前记录按键，使本帧输入可在本帧执行。若之后记录，每个动作都增加一帧延迟。
            _context.InputBuffer.RecordPressesFrom(_input, _time.CombatTime);

            _context.TickCooldowns(deltaTime);
            _stamina.Tick(deltaTime);

            // 只有防御放下时架势才恢复，因此无法通过无限格挡等待压力消失。
            if (!_context.IsGuarding)
            {
                _posture.Tick(deltaTime);
            }

            UpdateThreatFacing();
            TrySwapWeapon();
            _machine.Tick(deltaTime);
            ResolveWallImpact();

            DriveAnimation();
        }

        /// <summary>将本帧可见状态传递给程序化动画器。</summary>
        /// <remarks>
        /// 仅用于表现，输入来自 Boss 允许看到的相同可观察状态，即动作与攻击阶段，再加移动速度。
        /// 动画器把判定时间轴表现为挥击，却从不改变时间轴。
        /// </remarks>
        private void DriveAnimation()
        {
            if (_animator == null)
            {
                return;
            }

            float speed01 = _config.MoveSpeed > 0f
                ? _context.Motor.PlanarVelocity.magnitude / _config.MoveSpeed
                : 0f;

            float rising = _machine.CurrentState == _getUpState ? _getUpState.Progress(_context) : 0f;

            _animator.SetMotionState(
                _context.ObservableState, _context.Attacks.Phase, speed01,
                _context.Attacks.CurrentAttack, _context.Attacks.ElapsedSeconds, rising);
        }

        /// <inheritdoc />
        public DamageResult TakeDamage(in DamageInfo damage)
        {
            if (!_isInitialised || !_health.IsAlive)
            {
                return DamageResult.NoDamage(DamageOutcome.Ignored);
            }

            // 独立于物理层设置直接拒绝友军伤害，避免预制体层分配错误导致玩家伤害自身。
            if (damage.SourceTeam == Team)
            {
                return DamageResult.NoDamage(DamageOutcome.Ignored);
            }

            // 首先检查，包括危险区域：倒地或起身的身体不能受伤，否则击倒对失控时长的上限就不再有效。
            if (!_context.Reactions.CanBeHurt)
            {
                return DamageResult.NoDamage(DamageOutcome.Ignored);
            }

            if (_context.IsInvulnerable && !damage.IgnoresInvulnerability)
            {
                return ResolveEvasion();
            }

            // 与 Boss 共享的单一判断。危险且不可格挡的攻击在此得到 None，完全穿过举起的防御；
            // 上方无敌检查仍允许通过闪避避开。
            if (IsParryingThisBlow(damage))
            {
                return ResolveParry(damage);
            }

            switch (DescribeDefence(damage))
            {
                case DefenceOutcome.Deflected:
                    return ResolveDeflect(damage);

                case DefenceOutcome.Blocked:
                    return ResolveBlock(damage);
            }

            // 挑战修正缩放脆弱模式的传入伤害，并在训练模式使玩家保持至少能再活一次命中的生命，
            // 确保练习不会结束。其他受击效果不变：闪光、反震、击退和硬直仍正常播放，因此训练仍能学习。
            float amount = damage.Amount * _incomingDamageMultiplier;

            if (_immortal)
            {
                amount = Mathf.Min(amount, Mathf.Max(0f, _health.Current - TrainingHealthFloor));
            }

            float applied = _health.Reduce(amount, damage);

            _hitFlash?.Play();
            _animator?.Recoil(damage.HitDirection);

            ImpactReaction reaction = _context.Reactions.Admit(damage.Reaction, _time.CombatTime);
            ApplyKnockback(damage, reaction == ImpactReaction.Knockback ? _config.KnockbackReactionMultiplier : 1f);

            // 击倒或击飞本身就是完整反应；再叠加架势硬直只会在玩家刚起身时额外排入一次中断。
            if (reaction == ImpactReaction.Knockdown || reaction == ImpactReaction.Launch)
            {
                _context.RequestReaction(reaction);
            }
            else if (_context.Reactions.AdmitsStagger)
            {
                ResolveIncomingStagger(damage);
            }

            return DamageResult.Applied(applied, !_health.IsAlive);
        }

        /// <summary>
        /// 通过玩家架势池判断已命中攻击是否中断玩家。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 此前每次命中直接中断：攻击硬直强度直接变成硬直请求，从未查询架势池，
        /// 而 Boss 同样情况经过 <see cref="Combat.Vitals.PoisePool"/>。该池在已崩溃时忽略架势伤害，
        /// 恢复时回满，因此 Boss 不会连续硬直，玩家却可被每一击无限连锁中断。
        /// 受击因此像陷入无法脱出的停滞。
        /// </para>
        /// <para>
        /// 现在双方遵守相同规则。命中消耗架势，只有耗尽才中断，后续中断带有自身免疫。
        /// 承受命中不等于忽略命中：上方闪光、反震和击退仍播放，因此不中断玩家的攻击也可见地命中。
        /// </para>
        /// </remarks>
        /// <param name="damage">已命中的攻击。</param>
        private void ResolveIncomingStagger(in DamageInfo damage)
        {
            if (damage.Stagger == StaggerStrength.None)
            {
                return;
            }

            // Break 强度无论当前架势多少都会停止玩家，这是不能直接承受穿过的最高层级。
            // 通过清空资源池而非绕过它，仍沿唯一处理路径，也获得与其他崩溃相同的免疫。
            // 绕过会让连续两次此类攻击连锁，而本方法正要消除该问题。
            float postureCost = damage.Stagger == StaggerStrength.Break
                ? _posture.Maximum
                : damage.PoiseDamage;

            if (!_posture.ApplyPoiseDamage(postureCost))
            {
                return;
            }

            // 霸体仅拒绝中断，这正是文档承诺。上方仍消耗架势，因此巨剑顶住连击后可能只差一击便崩溃，
            // 而非毫发无损。拒绝整个裁决会无声地把霸体变成架势免疫；这是无人要求的强化，
            // 只因此前未格挡攻击从不经过架势路径而出现。
            if (ResistsIncomingStagger())
            {
                return;
            }

            BreakPosture();
        }

        /// <summary>在完整崩溃时长内中断玩家。</summary>
        private void BreakPosture()
        {
            _context.RequestStagger(StaggerDurations.BreakSeconds, StaggerReason.PoiseBreak);

            // 沉着被打破：精准防御积累的专注随之清空。
            _focus?.Reset();
        }

        /// <summary>装备武器的霸体让当前挥击承受命中继续执行时为 true。</summary>
        private bool ResistsIncomingStagger()
        {
            WeaponDefinition weapon = _context.Weapon;

            return weapon != null &&
                   _context.Attacks.IsRunning &&
                   DefenceResolver.ResistsStagger(weapon.Defence, _context.Attacks.Phase);
        }

        /// <summary>
        /// 捕获旁观者此刻能感知的玩家信息。
        /// </summary>
        /// <remarks>
        /// 全部防作弊保证集中于此方法。下方每个字段都对应屏幕可见内容。这里不读取输入、体力、
        /// 冷却或无敌标志，也绝不能添加会读取这些内容的字段。
        /// </remarks>
        /// <param name="timestamp">用于给快照加时间戳的战斗时钟时间。</param>
        /// <returns>已填充的快照。</returns>
        public PlayerObservation CaptureObservation(float timestamp)
        {
            if (!_isInitialised)
            {
                return default;
            }

            return new PlayerObservation
            {
                Timestamp = timestamp,
                Position = transform.position,
                Velocity = _context.Motor.PlanarVelocity,
                Facing = transform.forward,
                ActionState = _context.ObservableState,
                TimeInActionState = _time.CombatTime - _context.ObservableStateEnteredAt,
                NormalizedHealth = _health.Normalized,
                IsValid = true
            };
        }

        /// <summary>重试时恢复角色初始状态。</summary>
        /// <param name="spawnPosition">角色的放置位置。</param>
        public void ResetForNewAttempt(Vector3 spawnPosition)
        {
            if (!_isInitialised)
            {
                return;
            }

            _health.ResetToFull();
            _stamina.ResetToFull();

            _context.IsInvulnerable = false;
            _context.DashCooldownRemaining = 0f;
            _context.SpecialCooldownRemaining = 0f;
            _context.HealChargesRemaining = _config.HealCharges;
            _context.ComboIndex = 0;
            _context.DashStartedAt = float.NegativeInfinity;
            _context.StaggerRequested = false;
            _context.RequestedReaction = ImpactReaction.None;
            _context.IsGuarding = false;
            _context.RiposteConsumed = false;
            _context.IsThreatPostureBroken = false;
            _posture.ResetToFull();
            _focus.Reset();
            _context.Attacks.Cancel();
            _context.InputBuffer.Clear();
            _context.Motor.Teleport(spawnPosition);

            _input.SetEnabled(true);
            _machine.ForceState(_idleState);

            // 在强制切换状态后重置，否则退出起身状态会使重试从新的击倒免疫期开始。
            _context.Reactions.Reset();
            // 先把身体交回 Animator，再重置姿势，否则会修改仍归物理管理的骨骼。
            _ragdoll?.Restore();
            _animator?.ResetPose();

            PublishVitals();
        }

        /// <summary>连接各状态和状态间转移条件。</summary>
        private void BuildStateMachine()
        {
            _idleState = new PlayerIdleState();
            _moveState = new PlayerMoveState();
            _dashState = new PlayerDashState();
            _attackState = new PlayerAttackState();
            _parryState = new PlayerParryState();
            _parryStrikeState = new PlayerParryStrikeState();
            _riposteState = new PlayerRiposteState();
            _healState = new PlayerHealState();
            _staggerState = new PlayerStaggerState();
            _deadState = new PlayerDeadState();
            _airborneState = new PlayerAirborneState();
            _knockedDownState = new PlayerKnockedDownState();
            _getUpState = new PlayerGetUpState(() => _posture.ResetToFull());

            _machine = new StateMachine<PlayerContext>(_context, _idleState);

            _machine.AddTransition(_idleState, _moveState, HasMoveInput);
            _machine.AddTransition(_moveState, _idleState, IsStandingStill);

            // 闪避和攻击仅在地面状态提供。注册为全局转移会让它们取消硬直，抹去受击代价。
            AddGroundedTransitions(_idleState);
            AddGroundedTransitions(_moveState);

            // 翻滚缓慢尾段可由缓存攻击取消。优先于翻滚自然退出，使尾段按下的攻击直接接出，
            // 而非等待翻滚结束后从站立重新开始。
            _machine.AddTransition(_dashState, _attackState, WantsToCancelRollIntoAttack, AttackTransitionPriority);

            // 同样的缓慢尾段也可举起防御，避免翻滚落在攻击前方成为陷阱。
            _machine.AddTransition(_dashState, _parryState, WantsToCancelRollIntoGuard, GuardTransitionPriority);
            _machine.AddTransition(_dashState, _moveState, DashFinishedWithInput);
            _machine.AddTransition(_dashState, _idleState, DashFinished);

            // 闪避高于自然退出，使玩家在后摇中按闪避时，一恢复资格便逃开，而非等待攻击完全结束。
            _machine.AddTransition(_attackState, _dashState, WantsToDashCancel, DashTransitionPriority);
            _machine.AddTransition(_attackState, _parryState, WantsToGuardCancel, GuardTransitionPriority);
            _machine.AddTransition(_attackState, _moveState, AttackFinishedWithInput);
            _machine.AddTransition(_attackState, _idleState, AttackFinished);

            // 治疗引导早期闪避会放弃治疗，并消耗该次数。
            _machine.AddTransition(_healState, _dashState, WantsToAbandonHeal, DashTransitionPriority);
            _machine.AddTransition(_healState, _moveState, HealFinishedWithInput);
            _machine.AddTransition(_healState, _idleState, HealFinished);

            // 防御可随时转为翻滚或挥击，而非只能松开按钮。只有松开才受弹反窗口时长限制，
            // 使快速点击仍能弹反。
            _machine.AddTransition(_parryState, _dashState, WantsToDash, DashTransitionPriority);
            _machine.AddTransition(_parryState, _attackState, WantsToAttack, AttackTransitionPriority);
            _machine.AddTransition(_parryState, _moveState, GuardDroppedWithInput);
            _machine.AddTransition(_parryState, _idleState, GuardDropped);

            // 主动招架必须执行到结束：挥空必须可惩罚，否则它只会成为更好的防御。
            _machine.AddTransition(_parryStrikeState, _moveState, ParryFinishedWithInput);
            _machine.AddTransition(_parryStrikeState, _idleState, ParryFinished);

            _machine.AddTransition(_riposteState, _moveState, RiposteFinishedWithInput);
            _machine.AddTransition(_riposteState, _idleState, RiposteFinished);

            // 反击优先于一切：赢得的惩罚不能被偶然缓存的另一个动作吞掉。
            _machine.AddGlobalTransition(_riposteState, CanRiposte, RiposteTransitionPriority);

            _machine.AddTransition(_staggerState, _moveState, StaggerFinishedWithInput);
            _machine.AddTransition(_staggerState, _idleState, StaggerFinished);

            // 失去站姿单向推进：空中、倒地、起身、站立。只有死亡能提前离开。
            _machine.AddTransition(_airborneState, _knockedDownState, HasLanded);
            _machine.AddTransition(_knockedDownState, _getUpState, KnockdownFinished);
            _machine.AddTransition(_getUpState, _moveState, GetUpFinishedWithInput);
            _machine.AddTransition(_getUpState, _idleState, GetUpFinished);

            _machine.AddGlobalTransition(_deadState, IsDefeated, DeathTransitionPriority);
            _machine.AddGlobalTransition(_airborneState, IsLaunchRequested, ReactionTransitionPriority);
            _machine.AddGlobalTransition(_knockedDownState, IsKnockdownRequested, ReactionTransitionPriority);
            _machine.AddGlobalTransition(_staggerState, IsStaggerRequested, StaggerTransitionPriority);
        }

        /// <summary>
        /// 注册玩家完全控制状态下可执行的动作。
        /// </summary>
        /// <remarks>
        /// 顺序表达角色意图的优先级。闪避高于攻击，因为压力下同时按两者的玩家想逃开；若反而出招，
        /// 会感觉游戏覆盖了自身意图。
        /// </remarks>
        private void AddGroundedTransitions(IState<PlayerContext> from)
        {
            _machine.AddTransition(from, _dashState, WantsToDash, DashTransitionPriority);
            _machine.AddTransition(from, _parryStrikeState, WantsToParry, ParryTransitionPriority);
            _machine.AddTransition(from, _parryState, WantsToGuard, GuardTransitionPriority);
            _machine.AddTransition(from, _attackState, WantsToAttack, AttackTransitionPriority);
            _machine.AddTransition(from, _healState, WantsToHeal, HealTransitionPriority);
        }

        private static bool HasMoveInput(PlayerContext context) => context.HasMoveInput;

        private static bool IsStandingStill(PlayerContext context) =>
            !context.HasMoveInput && context.Motor.IsStationary;

        /// <summary>
        /// 确实可以闪避时消费缓存闪避按键。
        /// </summary>
        /// <remarks>
        /// 刻意先检查资格。尚不知道能否闪避就消费按键会吞掉它，玩家只会感到闪避没有出现。保留缓存后，
        /// 一旦体力或冷却允许就会触发，这正是缓存的目的。
        /// </remarks>
        private bool WantsToDash(PlayerContext context) =>
            context.CanBeginDash &&
            context.InputBuffer.TryConsume(PlayerInputAction.Dash, _time.CombatTime);

        private bool DashFinished(PlayerContext context) => _dashState.IsComplete(context);

        private bool WantsToCancelRollIntoAttack(PlayerContext context) =>
            _dashState.CanCancel(context) && WantsToAttack(context);

        private bool WantsToCancelRollIntoGuard(PlayerContext context) =>
            _dashState.CanCancel(context) && WantsToGuard(context);

        private bool WantsToGuardCancel(PlayerContext context) =>
            _attackState.CanGuardCancel(context) && WantsToGuard(context);

        private bool WantsToAbandonHeal(PlayerContext context) =>
            _healState.CanDodgeCancel(context) && WantsToDash(context);

        private bool DashFinishedWithInput(PlayerContext context) =>
            _dashState.IsComplete(context) && context.HasMoveInput;

        /// <summary>
        /// 选择并消费当前已缓存且资源足够的攻击输入。
        /// </summary>
        /// <remarks>
        /// 重击优先于轻击检查，使同时按下或从一种切换到另一种的玩家得到希望的高投入攻击，而非试探。
        /// </remarks>
        private bool WantsToAttack(PlayerContext context)
        {
            float now = _time.CombatTime;

            if (TryStartAttack(context, PlayerInputAction.HeavyAttack, context.HeavyAttack, now))
            {
                return true;
            }

            if (context.CanUseSpecial &&
                context.InputBuffer.TryConsume(PlayerInputAction.Special, now))
            {
                // 专注槽满时，特殊攻击会消耗专注并变为强化形态。若未配置强化攻击，则回退为普通攻击，
                // 因此即使武器没有独立强化版本，也不会白白浪费已充满的专注。
                bool empowered = _focus != null && _focus.TryConsumeFull();

                context.PendingAttack = empowered ? context.EmpoweredSpecialAttack : context.SpecialAttack;
                context.ComboIndex = 0;
                context.SpecialCooldownRemaining = context.Config.SpecialCooldownSeconds;
                return true;
            }

            AttackDefinition[] chain = context.LightChain;
            if (chain != null && chain.Length > 0 &&
                TryStartAttack(context, PlayerInputAction.LightAttack, chain[0], now))
            {
                return true;
            }

            return false;
        }

        /// <summary>资源足够时消费一次攻击的缓存按键。</summary>
        private static bool TryStartAttack(
            PlayerContext context,
            PlayerInputAction action,
            AttackDefinition attack,
            float now)
        {
            // 与闪避一样，先检查资源是否足够，再消费输入。吞掉角色无法执行的按键会让游戏看起来丢失了输
            // 入。
            if (attack == null || !context.Stamina.CanSpend(attack.StaminaCost))
            {
                return false;
            }

            if (!context.InputBuffer.TryConsume(action, now))
            {
                return false;
            }

            context.PendingAttack = attack;
            context.ComboIndex = 0;
            return true;
        }

        /// <summary>
        /// 按住控制键时举起防御。
        /// </summary>
        /// <remarks>
        /// 检查持续按住状态，而非缓存按键，因为防御举起的瞬间就会开启弹反窗口。
        /// 消费缓存按键可能比玩家实际按下晚一两帧才开启窗口，足以把弹反变成格挡，
        /// 令玩家感觉游戏吞掉了输入。
        /// </remarks>
        private static bool WantsToGuard(PlayerContext context) =>
            context.Input.IsHeld(PlayerInputAction.Guard);

        /// <summary>体力足够挥击时消费缓存招架按键。</summary>
        private bool WantsToParry(PlayerContext context) =>
            context.Stamina.CanSpend(PlayerParryStrikeState.StaminaCost) &&
            context.InputBuffer.TryConsume(PlayerInputAction.Parry, _time.CombatTime);

        private bool ParryFinished(PlayerContext context) => _parryStrikeState.IsComplete(context);

        private bool ParryFinishedWithInput(PlayerContext context) =>
            _parryStrikeState.IsComplete(context) && context.HasMoveInput;

        private bool GuardDropped(PlayerContext context) => _parryState.IsComplete(context);

        private bool GuardDroppedWithInput(PlayerContext context) =>
            _parryState.IsComplete(context) && context.HasMoveInput;

        /// <summary>Boss 防御已破且惩罚机会尚未使用时为 true。</summary>
        private bool CanRiposte(PlayerContext context) =>
            IsRiposteOffered(context) &&
            context.InputBuffer.TryConsume(PlayerInputAction.LightAttack, _time.CombatTime);

        /// <summary>
        /// Boss 是否跪倒在可触及范围内，且惩罚机会尚未使用。
        /// </summary>
        /// <remarks>
        /// 原先防御一破，反击就会从任何状态、任何距离自动触发，打断玩家正在做的事。
        /// 现在它是一项可选择的机会：走近、面向 Boss，在其跪倒期间按攻击，或任由机会过去。
        /// </remarks>
        private static bool IsRiposteOffered(PlayerContext context) =>
            context.IsThreatPostureBroken &&
            !context.RiposteConsumed &&
            !context.Reactions.IsReacting &&
            context.ThreatDistance <= RiposteReachMetres &&
            Vector3.Dot(context.Motor.Facing, context.FacingTowardThreat) >= RiposteFacingDot;

        /// <summary>玩家此刻可执行惩罚攻击时为 true，用于屏幕提示。</summary>
        public bool RiposteOffered => _context != null && IsRiposteOffered(_context);

        private bool RiposteFinished(PlayerContext context) => _riposteState.IsComplete(context);

        private bool RiposteFinishedWithInput(PlayerContext context) =>
            _riposteState.IsComplete(context) && context.HasMoveInput;

        /// <summary>
        /// 玩家请求且可以切换时，轮换至下一把武器。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 刻意拒绝动作中途切换。若能通过切换取消挥击，玩家便可免费取消任何投入，
        /// 攻击后摇也就失去了意义。
        /// </para>
        /// <para>
        /// 切换会作为战斗事件发布，使 Boss能观察玩家偏好的武器。武器就在玩家手中，属于合理可见信息；
        /// 这样武器系统便能为自适应提供依据，而非削弱自适应。
        /// </para>
        /// </remarks>
        private void TrySwapWeapon()
        {
            WeaponDefinition[] weapons = _config.Weapons;

            if (weapons == null || weapons.Length < 2)
            {
                return;
            }

            bool isFree = _machine.CurrentState == _idleState || _machine.CurrentState == _moveState;

            if (!isFree || !_context.InputBuffer.TryConsume(PlayerInputAction.SwapWeapon, _time.CombatTime))
            {
                return;
            }

            _weaponIndex = (_weaponIndex + 1) % weapons.Length;
            EquipWeapon(weapons[_weaponIndex]);
        }

        /// <summary>取出武器并发布通知。</summary>
        private void EquipWeapon(WeaponDefinition weapon)
        {
            if (weapon == null)
            {
                return;
            }

            _context.Weapon = weapon;
            _context.ComboIndex = 0;

            // 替换手持模型使其匹配武器。当前模型为空，因此会清空挂点，武器仍可通过拖尾和挥击颜色辨认。
            _weaponSocket?.Equip(weapon.ModelPrefab);

            _weaponChannel?.Raise(weapon.DisplayName);

            // 独立于名称发布通知，使音频导演可选择该武器的挥击声音，无需匹配显示字符串；否则文字一改，
            // 匹配就会失效。
            if (!string.IsNullOrEmpty(weapon.SwingCueId))
            {
                _weaponSwingCueChannel?.Raise(weapon.SwingCueId);
            }

            _context.PublishCombatEvent(CombatEventKind.WeaponDrawn);
        }

        /// <summary>将上下文指向 Boss，使防御和反击面向正确方向。</summary>
        private void UpdateThreatFacing()
        {
            if (_threat == null)
            {
                return;
            }

            Vector3 toThreat = _threat.position - transform.position;
            toThreat.y = 0f;
            _context.ThreatDistance = toThreat.magnitude;

            if (toThreat.sqrMagnitude > Mathf.Epsilon)
            {
                _context.FacingTowardThreat = toThreat.normalized;
            }
        }

        private bool WantsToHeal(PlayerContext context) =>
            context.CanHeal && context.InputBuffer.TryConsume(PlayerInputAction.Heal, _time.CombatTime);

        /// <summary>攻击进入可取消阶段时消费缓存闪避。</summary>
        private bool WantsToDashCancel(PlayerContext context) =>
            _attackState.CanDashCancel(context) && WantsToDash(context);

        private bool AttackFinished(PlayerContext context) => _attackState.IsComplete(context);

        private bool AttackFinishedWithInput(PlayerContext context) =>
            _attackState.IsComplete(context) && context.HasMoveInput;

        private bool HealFinished(PlayerContext context) => _healState.IsComplete(context);

        private bool HealFinishedWithInput(PlayerContext context) =>
            _healState.IsComplete(context) && context.HasMoveInput;

        private bool StaggerFinished(PlayerContext context) => _staggerState.IsComplete(context);

        private bool StaggerFinishedWithInput(PlayerContext context) =>
            _staggerState.IsComplete(context) && context.HasMoveInput;

        private static bool IsDefeated(PlayerContext context) => !context.Health.IsAlive;

        /// <summary>硬直只能在玩家站立时开始；在空中或倒地时触发会提前打断受击反应。</summary>
        private static bool IsStaggerRequested(PlayerContext context) =>
            context.StaggerRequested && context.Reactions.AdmitsStagger;

        private static bool IsLaunchRequested(PlayerContext context) =>
            context.RequestedReaction == ImpactReaction.Launch;

        private static bool IsKnockdownRequested(PlayerContext context) =>
            context.RequestedReaction == ImpactReaction.Knockdown;

        private bool HasLanded(PlayerContext context) => _airborneState.HasLanded(context);

        private bool KnockdownFinished(PlayerContext context) => _knockedDownState.IsComplete(context);

        private bool GetUpFinished(PlayerContext context) => _getUpState.IsComplete(context);

        private bool GetUpFinishedWithInput(PlayerContext context) =>
            _getUpState.IsComplete(context) && context.HasMoveInput;

        /// <summary>
        /// 判断避开的攻击属于普通闪避还是完美闪避。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 区别只在时机。若攻击在闪避开始后的短窗口内被抵消，说明玩家读懂攻击并在最后一刻行动；
        /// 同一次闪避中更晚抵消攻击，则说明玩家恰好已在移动。两者都避免伤害，
        /// 但只有前者是值得奖励的判断。
        /// </para>
        /// <para>
        /// 奖励是慢动作，而非伤害或资源，因为完美闪避应提供看清并利用刚创造的破绽的机会。
        /// </para>
        /// </remarks>
        private DamageResult ResolveEvasion()
        {
            float sinceDashBegan = _time.CombatTime - _context.DashStartedAt;

            if (sinceDashBegan > GameplayConstants.PerfectDodgeWindowSeconds)
            {
                return DamageResult.NoDamage(DamageOutcome.Invulnerable);
            }

            _time.RequestSlowMotion(
                PerfectDodgeTimeScale, GameplayConstants.PerfectDodgeSlowMotionSeconds);

            _context.PublishCombatEvent(CombatEventKind.PerfectDodge, _context.DashDirection);
            _perfectDodgeChannel?.Raise();
            _focus?.AddFromPerfectDodge();

            return DamageResult.NoDamage(DamageOutcome.PerfectDodged);
        }

        /// <summary>
        /// 判断防御承受的攻击是准确弹反还是较晚格挡。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 整场战斗的节奏都集中在这个分支。弹反完全抵消攻击、削减攻击者架势，
        /// 并以命中停顿和金属声奖励玩家，坚守位置也能推进战斗。较晚格挡虽能承受攻击，
        /// 却付出削减伤害和自身架势，因此龟缩只会缓慢落败。
        /// </para>
        /// <para>
        /// 这里只看时机。没有随难度变化的宽容度，也没有隐藏辅助；窗口始终固定，
        /// 才能让学习过程体现玩家进步，而非游戏让步。
        /// </para>
        /// </remarks>
        /// <summary>
        /// 询问共享解析器，玩家防御如何处理传入攻击。
        /// </summary>
        /// <remarks>
        /// 玩家总能退而格挡，因此这里的 <c>CanBlock</c> 为 true，Boss 则为 false。
        /// 双方防御只有这一个标志不同，其余判断都使用同一函数。
        /// </remarks>
        /// <param name="damage">传入的攻击。</param>
        /// <returns>攻击对防御的解析结果。</returns>
        private DefenceOutcome DescribeDefence(in DamageInfo damage) =>
            DefenceResolver.ResolveDefence(new DefenceQuery
            {
                IsDefending = _context.IsGuarding,
                CanDeflect = _context.CanDeflect,
                CanBlock = true,
                TimeInDefenceSeconds = _parryState.TimeInState,
                DeflectWindowSeconds = _context.DeflectWindowSeconds,
                Unblockable = damage.Unblockable,
                Unparryable = damage.Unparryable
            });

        /// <summary>
        /// 拒绝准确接下的攻击，玩家无需付出代价。
        /// </summary>
        /// <param name="damage">传入的攻击。</param>
        /// <returns>不包含伤害的结果。</returns>
        /// <summary>
        /// 招架挥击是否正在生效，以及这一击是否能被刀刃拨开。
        /// </summary>
        /// <remarks>
        /// 不能是不可格挡、不可招架、冲击波或环境危险攻击：战斗希望玩家以移动应对这些攻击；
        /// 若招架能挡住一切，闪避便没有了存在理由。
        /// </remarks>
        private bool IsParryingThisBlow(in DamageInfo damage) =>
            _context.IsParryStriking &&
            _parryStrikeState.IsWindowOpen &&
            !damage.Unblockable &&
            !damage.Unparryable &&
            damage.Type != DamageType.BossProjectile &&
            damage.Type != DamageType.Hazard;

        /// <summary>
        /// 以刀刃拨开攻击，并让世界停顿一拍。
        /// </summary>
        /// <param name="damage">传入的攻击。</param>
        /// <returns>不含伤害的结果。</returns>
        private DamageResult ResolveParry(in DamageInfo damage)
        {
            _time.RequestHitStop(ParryHitStopSeconds);
            _time.RequestSlowMotion(ParrySlowMotionScale, ParrySlowMotionSeconds);
            _screenShake?.AddTrauma(ParryTrauma);
            _screenShake?.Punch(ParryTrauma);

            // 以弹反事件报告，使火花、声音、Boss 后仰等所有监听者已知如何响应；
            // 下方返回的结果会告诉攻击者，这一次付出的代价更大。
            _context.LastDefenceWasParry = true;
            _context.PublishCombatEvent(CombatEventKind.Deflected, damage.HitDirection);
            _context.PublishCombatEvent(CombatEventKind.PlayerParried, damage.HitDirection);

            // 身体会对刚拨开的攻击作出反应；否则骑士会在战斗最重要的时刻完全静止。
            _animator?.Recoil(damage.HitDirection);
            _deflectChannel?.Raise();
            _focus?.AddFromDeflect();

            return DamageResult.NoDamage(DamageOutcome.Parried);
        }

        private DamageResult ResolveDeflect(in DamageInfo damage)
        {
            _context.LastDefenceWasParry = false;

            _time.RequestHitStop(DeflectHitStopSeconds);
            _screenShake?.AddTrauma(DeflectTrauma);
            _screenShake?.Punch(DeflectTrauma);

            // 携带攻击来向，使火花沿来向反射回去，而非无方向地散开。
            _context.PublishCombatEvent(CombatEventKind.Deflected, damage.HitDirection);
            _deflectChannel?.Raise();
            _focus?.AddFromDeflect();
            _animator?.Recoil(damage.HitDirection);

            // 结果是 Deflected，而非 Blocked。区别针对攻击者：前者会让造成这次攻击的挥击后仰，
            // 较晚格挡则不能如此。
            return DamageResult.NoDamage(DamageOutcome.Deflected);
        }

        /// <summary>
        /// 承受防御已举起但时机不准确的攻击。
        /// </summary>
        /// <param name="damage">传入的攻击。</param>
        /// <returns>穿过防御的削减伤害。</returns>
        private DamageResult ResolveBlock(in DamageInfo damage)
        {
            // 时机较晚。虽承受住攻击，却付出代价；次数足够便会彻底破防。这里持续防御损耗较慢，
            // 使其可以持续保持，而非只能抓时机。
            float chip = damage.Amount * _config.BlockChipFraction;
            float applied = _health.Reduce(chip, damage);

            float postureCost = _config.BlockPostureCost *
                                (_context.Weapon != null
                                    ? DefenceResolver.BlockPostureMultiplier(_context.Weapon.Defence)
                                    : 1f);

            if (_posture.ApplyPoiseDamage(postureCost))
            {
                BreakPosture();
            }

            _hitFlash?.Play();
            _animator?.Recoil(damage.HitDirection);
            _context.PublishCombatEvent(CombatEventKind.Blocked, damage.HitDirection);

            return DamageResult.Applied(applied, !_health.IsAlive);
        }

        /// <summary>
        /// 玩家被强力推向墙壁时触发硬直。
        /// </summary>
        /// <remarks>
        /// 只读取外部施加的推力，不读取玩家自身跑动或翻滚，因此走向柱子无害，被击退撞上柱子则会受罚。
        /// 与所有硬直一样遵循反应门控规则：被抛飞或倒地的玩家不会被落点附近的墙壁打断。
        /// </remarks>
        private void ResolveWallImpact()
        {
            if (_context.Motor.WallImpactSpeed < _config.WallImpactSpeed ||
                !_health.IsAlive ||
                !_context.Reactions.AdmitsStagger)
            {
                return;
            }

            _context.PublishCombatEvent(CombatEventKind.WallImpact);
            _context.RequestStagger(_config.WallImpactStaggerSeconds, StaggerReason.Hit);
        }

        /// <summary>将角色推离撞击位置。</summary>
        /// <param name="damage">此次攻击。</param>
        /// <param name="multiplier">攻击击退速度的倍率；击退反应使用大于一的值。</param>
        private void ApplyKnockback(in DamageInfo damage, float multiplier)
        {
            if (damage.KnockbackSpeed <= 0f)
            {
                return;
            }

            Vector3 direction = damage.HitDirection;
            direction.y = 0f;

            if (direction.sqrMagnitude < Mathf.Epsilon)
            {
                return;
            }

            // 使用叠加，而非赋值。直接赋值会在下一帧被玩家移动输入覆盖，
            // 导致持续按方向的玩家挨重击后完全不动。
            _context.Motor.AddImpulse(direction.normalized * (damage.KnockbackSpeed * multiplier));
        }

        /// <summary>将资源池变化转发至界面监听的事件通道。</summary>
        private void SubscribeToVitals()
        {
            _health.Changed += args => _healthChannel?.Raise(args.Normalized);
            _stamina.Changed += args => _staminaChannel?.Raise(args.Normalized);
            _posture.Changed += args => _postureChannel?.Raise(args.Normalized);
            _focus.Changed += args => _focusChannel?.Raise(args.Normalized);
            _health.Died += _ => _deathChannel?.Raise();

            // 沿致命一击的方向抛出，使倒下明显由这一击造成。
            _health.Died += args => _ragdoll?.Activate(
                args.FinalBlow.HitDirection.normalized * args.FinalBlow.KnockbackSpeed);
        }

        /// <summary>发布初始数值，使资源条在首次变化前就显示正确。</summary>
        private void PublishVitals()
        {
            _healthChannel?.Raise(_health.Normalized);
            _staminaChannel?.Raise(_stamina.Normalized);
            _focusChannel?.Raise(_focus.Normalized);

            // 原先这里遗漏架势，导致架势条从未获得初始值。它只有较晚格挡受击时才变化，
            // 未初始化的资源条与只是没有变化可报告的资源条因而无法区分。
            _postureChannel?.Raise(_posture.Normalized);
        }

        /// <summary>确定用于解释输入方向的相机。</summary>
        private Transform ResolveCameraTransform()
        {
            if (_cameraTransform != null)
            {
                return _cameraTransform;
            }

            Camera main = Camera.main;
            return main != null ? main.transform : null;
        }

        /// <summary>明确报告缺失依赖，避免以空引用失败。</summary>
        private bool ValidateSetup()
        {
            if (_config == null)
            {
                Debug.LogError(
                    "[Adaptive Boss Arena] PlayerController has no PlayerConfig assigned and has been " +
                    "disabled.", this);
                return false;
            }

            if (_input == null)
            {
                Debug.LogError(
                    "[Adaptive Boss Arena] PlayerController requires a PlayerInputReader on the same " +
                    "object and has been disabled.", this);
                return false;
            }

            if (!ServiceRegistry.Current.TryGet(out ITimeService _))
            {
                Debug.LogError(
                    "[Adaptive Boss Arena] No ITimeService is registered. Add a GameBootstrapper to the " +
                    "scene; it must run before the player initialises.", this);
                return false;
            }

            return true;
        }

        /// <summary>指定配置，供预制体生成器使用。</summary>
        /// <param name="config">角色调节参数。</param>
        public void SetConfig(PlayerConfig config) => _config = config;

        /// <summary>
        /// 告知玩家对手的位置，以及其防御当前是否已破。
        /// </summary>
        /// <remarks>
        /// 由遭遇导演推入，而非主动拉取，因为玩家不应持有 Boss引用，正如 Boss 不持有玩家引用。
        /// 跨越边界的只有一个 Transform 和一个标志。
        /// </remarks>
        /// <param name="threat">对手的 Transform。</param>
        public void SetThreat(Transform threat) => _threat = threat;

        /// <summary>告知角色相机是否锁定 Boss，由遭遇导演调用。</summary>
        /// <param name="lockedOn">锁定时为 true。</param>
        public void SetLockedOn(bool lockedOn)
        {
            if (_context != null)
            {
                _context.IsLockedOn = lockedOn;
            }
        }

        /// <summary>
        /// 将本轮挑战修正应用于玩家。
        /// </summary>
        /// <remarks>
        /// 由遭遇导演在一轮开始时调用一次。所有值默认对应普通战斗，因此未加修正的对局直接传入默认值，
        /// 不会改变玩家玩法。
        /// </remarks>
        /// <param name="healingEnabled">玩家是否可以治疗。</param>
        /// <param name="incomingDamageMultiplier">玩家承受伤害的倍率。</param>
        /// <param name="immortal">为 true 时，玩家始终保留高于死亡的一击生存空间（训练模式）。</param>
        public void ApplyRunModifiers(bool healingEnabled, float incomingDamageMultiplier, bool immortal)
        {
            _incomingDamageMultiplier = Mathf.Max(0f, incomingDamageMultiplier);
            _immortal = immortal;

            if (_context != null)
            {
                _context.HealingEnabled = healingEnabled;
            }
        }

        /// <summary>报告对手是否破防，从而开放反击机会。</summary>
        /// <param name="isBroken">对手因架势崩溃而硬直时为 true。</param>
        public void SetThreatPostureBroken(bool isBroken)
        {
            if (_context == null)
            {
                return;
            }

            // 离开破防状态时重置已使用标志，使下一次破防提供新的惩罚机会。
            if (!isBroken)
            {
                _context.RiposteConsumed = false;
            }

            _context.IsThreatPostureBroken = isBroken;
        }

        /// <summary>
        /// 玩家挥击被 Boss 准确接下时后仰。
        /// </summary>
        /// <remarks>
        /// 发送请求，而非立即应用。此调用发生在攻击执行器自身的阶段推进循环内，
        /// 立即拆除挥击会重入尚未结束的循环。硬直请求将处理推迟一帧，攻击状态退出时会自然取消挥击；
        /// 后仰与取消使用同一机制，因此无需再添加第二套机制。
        /// </remarks>
        private void OnOwnAttackParried() =>
            _context.RequestStagger(StaggerDurations.InterruptSeconds, StaggerReason.Parried);

        /// <summary>
        /// 玩家挥击被 Boss 准确接下时施加架势伤害。
        /// </summary>
        /// <remarks>
        /// 对应 <c>BossController.ApplyDeflectPosture</c> 的镜像操作，由遭遇导演调用，
        /// 使玩家始终无需持有 Boss 引用。这也是第二种能伤害玩家架势的方式：
        /// 此前资源池只接受较晚格挡的损耗，从不格挡的玩家便不可能破防，过度格挡代价极大，
        /// 过度出手却毫无代价。已破防期间到来的招架会被资源池丢弃，从而阻止连续破防。
        /// </remarks>
        /// <param name="postureDamage">要扣除的架势。</param>
        public void ApplyParriedPosture(float postureDamage)
        {
            if (!_isInitialised || _posture == null)
            {
                return;
            }

            if (_posture.ApplyPoiseDamage(postureDamage))
            {
                BreakPosture();
            }
        }

        /// <summary>当前判定框开启的挥击范围；若无则为零。</summary>
        /// <remarks>
        /// 刻意只暴露比攻击执行器更窄的信息。遭遇导演只需要知道是否有生效刀刃及其攻击范围，
        /// 以检测拼刀；交出执行器也会交出 Cancel，而从外部取消挥击正是阶段 D 的约束所要控制的行为。
        /// </remarks>
        public float LiveSwingReach =>
            _isInitialised &&
            _context.Attacks.IsRunning &&
            _context.Attacks.Phase == AttackPhase.Active &&
            _context.Attacks.CurrentAttack != null
                ? _context.Attacks.CurrentAttack.Range
                : 0f;

        /// <summary>
        /// 因挥击与另一把刀刃相撞而拒绝正在进行的挥击。
        /// </summary>
        /// <remarks>
        /// 与招架使用相同的硬直请求，原因也相同：推迟至下一帧，再由攻击状态退出拆除挥击。
        /// 调用来自导演的 LateUpdate，位于任何执行器阶段推进循环之外，因此此处同步取消其实安全；
        /// 但现有方式已能正确表现双方的中断，没有理由再加入第二种停止挥击的方式。
        /// </remarks>
        public void RecoilFromClash()
        {
            if (!_isInitialised)
            {
                return;
            }

            _context.RequestStagger(StaggerDurations.InterruptSeconds, StaggerReason.Parried);
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

        /// <summary>为防御条暴露的玩家架势。</summary>
        public IPoise Posture => _posture;

        /// <summary>准确弹反对攻击者造成的架势伤害。</summary>
        /// <remarks>
        /// 从上下文而非配置读取，使当前武器决定数值。原先读取配置使每种武器弹反都造成相同的 28 点架势
        /// 伤害，编写的每武器数值成了无效数据：长剑更严格且奖励更高的弹反，与能量刃代价更低、
        /// 威力更弱的弹反，在最重要的数值上毫无区别。未持武器时上下文会回退至配置，
        /// 因此无武器路径仍可运行。
        /// </remarks>
        public float DeflectPostureDamage
        {
            get
            {
                if (_context == null)
                {
                    return 0f;
                }

                return _context.LastDefenceWasParry ? ParryPostureDamage : _context.DeflectPostureDamage;
            }
        }

        /// <summary>指定事件通道，供预制体生成器使用。</summary>
        /// <param name="health">归一化生命值通道。</param>
        /// <param name="stamina">归一化体力通道。</param>
        /// <param name="death">玩家死亡通道。</param>
        /// <param name="perfectDodge">完美闪避通道。</param>
        /// <param name="deflect">准确弹反通道。</param>
        /// <param name="posture">归一化架势通道。</param>
        /// <param name="focus">归一化专注通道。</param>
        public void SetChannels(
            FloatEventChannel health,
            FloatEventChannel stamina,
            VoidEventChannel death,
            VoidEventChannel perfectDodge,
            VoidEventChannel deflect,
            FloatEventChannel posture,
            StringEventChannel weapon,
            FloatEventChannel focus,
            StringEventChannel weaponSwingCue = null)
        {
            _weaponSwingCueChannel = weaponSwingCue;

            _healthChannel = health;
            _staminaChannel = stamina;
            _deathChannel = death;
            _perfectDodgeChannel = perfectDodge;
            _deflectChannel = deflect;
            _postureChannel = posture;
            _weaponChannel = weapon;
            _focusChannel = focus;
        }
    }
}
