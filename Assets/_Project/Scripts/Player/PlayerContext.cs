using System;
using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Combat.Vitals;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Perception;
using AdaptiveBossArena.Core.Services;
using AdaptiveBossArena.Player.Controls;
using AdaptiveBossArena.Player.Movement;
using UnityEngine;

namespace AdaptiveBossArena.Player
{
    /// <summary>
    /// 传给每个玩家状态的共享状态和服务。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 状态是无状态的行为，所有跨转移保留的数据都放在这里，使状态可安全重入，
    /// 也让调试器从一个对象检查整个玩家，而非从当前激活状态拼凑信息。
    /// </para>
    /// <para>
    /// 可变属性是状态间的交接契约。状态不访问另一个状态并修改它，而是写入此处，再由转移读取。
    /// </para>
    /// </remarks>
    public sealed class PlayerContext
    {
        /// <summary>创建上下文。</summary>
        /// <param name="config">角色调参值。</param>
        /// <param name="motor">移动驱动器。</param>
        /// <param name="health">生命值池。</param>
        /// <param name="stamina">体力池。</param>
        /// <param name="input">控制输入源。</param>
        /// <param name="inputBuffer">近期按键缓存。</param>
        /// <param name="time">提供战斗时钟的时间服务。</param>
        /// <exception cref="ArgumentNullException">任意依赖缺失时抛出。</exception>
        public PlayerContext(
            PlayerConfig config,
            PlayerMotor motor,
            HealthPool health,
            StaminaPool stamina,
            IPlayerInput input,
            InputBuffer inputBuffer,
            ITimeService time,
            AttackExecutor attacks,
            ICombatEventBus events)
        {
            Config = config != null ? config : throw new ArgumentNullException(nameof(config));
            Motor = motor ?? throw new ArgumentNullException(nameof(motor));
            Health = health ?? throw new ArgumentNullException(nameof(health));
            Stamina = stamina ?? throw new ArgumentNullException(nameof(stamina));
            Input = input ?? throw new ArgumentNullException(nameof(input));
            InputBuffer = inputBuffer ?? throw new ArgumentNullException(nameof(inputBuffer));
            Time = time ?? throw new ArgumentNullException(nameof(time));
            Attacks = attacks ?? throw new ArgumentNullException(nameof(attacks));
            Events = events ?? throw new ArgumentNullException(nameof(events));

            HealChargesRemaining = config.HealCharges;
        }

        /// <summary>角色调参值。</summary>
        public PlayerConfig Config { get; }

        /// <summary>移动驱动器。</summary>
        public PlayerMotor Motor { get; }

        /// <summary>生命值池。</summary>
        public HealthPool Health { get; }

        /// <summary>体力池。</summary>
        public StaminaPool Stamina { get; }

        /// <summary>控制输入源。</summary>
        public IPlayerInput Input { get; }

        /// <summary>近期动作按键缓存。</summary>
        public InputBuffer InputBuffer { get; }

        /// <summary>提供战斗时钟的时间服务。</summary>
        public ITimeService Time { get; }

        /// <summary>执行玩家攻击，并裁决命中的对象。</summary>
        public AttackExecutor Attacks { get; }

        /// <summary>用于发布可观察事件的总线。</summary>
        public ICombatEventBus Events { get; }

        /// <summary>下次进入攻击状态时应执行的攻击。</summary>
        public AttackDefinition PendingAttack { get; set; }

        /// <summary>轻击连招到达的索引，连招结束时重置。</summary>
        public int ComboIndex { get; set; }

        /// <summary>再次使用特殊技能前剩余的秒数。</summary>
        public float SpecialCooldownRemaining { get; set; }

        /// <summary>本次挑战剩余治疗次数。</summary>
        public int HealChargesRemaining { get; set; }

        /// <summary>
        /// 当前闪避开始的战斗时钟时间。
        /// </summary>
        /// <remarks>
        /// 完美闪避据此判断。闪避开始后很快避开命中，意味着玩家在最后可能时机投入，值得奖励；
        /// 同次闪避较晚才避开命中，仅说明此前已在闪避。
        /// </remarks>
        public float DashStartedAt { get; set; } = float.NegativeInfinity;

        /// <summary>举起防御期间为 true。</summary>
        public bool IsGuarding { get; set; }

        /// <summary>刀刃正在主动拨开攻击时为 true。</summary>
        /// <remarks>
        /// 不同于 <see cref="IsGuarding"/>：招架是短窗口、长恢复的实际挥击，而防御是可持续保持的站姿。
        /// </remarks>
        public bool IsParryStriking { get; set; }

        /// <summary>上次拨开攻击是否通过主动招架，而非精准举防御。</summary>
        /// <remarks>决定这次交锋击破多少 Boss 架势。</remarks>
        public bool LastDefenceWasParry { get; set; }

        /// <summary>当前架势击破已被一次反击消耗时为 true。</summary>
        /// <remarks>防止一次硬直期间反复兑现同一次架势击破。</remarks>
        public bool RiposteConsumed { get; set; }

        /// <summary>
        /// 玩家应转向威胁的方向。
        /// </summary>
        /// <remarks>
        /// 控制器根据 Boss 位置提供，因为防御和反击必须面向对手，期间玩家不自主转向。
        /// 没有已知对手时回退到当前面向。
        /// </remarks>
        public Vector3 FacingTowardThreat { get; set; } = Vector3.forward;

        /// <summary>
        /// 相机锁定 Boss 期间为 true。
        /// </summary>
        /// <remarks>
        /// 锁定时骑士始终面向 Boss，通过侧移和后退绕行，而非转身跑向摇杆方向。
        /// 这是玩家要求的类魂决斗站姿。
        /// </remarks>
        public bool IsLockedOn { get; set; }

        /// <summary>Boss 距离，单位米，由控制器提供。</summary>
        /// <remarks>反击需要走近，因此惩罚判断不仅需要方向，也需要距离。</remarks>
        public float ThreatDistance { get; set; } = float.PositiveInfinity;

        /// <summary>Boss 累积的架势状态，暴露它以便提供反击机会。</summary>
        public bool IsThreatPostureBroken { get; set; }

        /// <summary>当前拔出的武器，使用备用徒手招式时为 null。</summary>
        public WeaponDefinition Weapon { get; set; }

        /// <summary>
        /// 当前武器的轻击连招。
        /// </summary>
        /// <remarks>
        /// 所有招式查询都经过这些属性而非直接读取配置，使装备武器确实改变战斗方式，
        /// 也让备用路径持续使用而非逐渐失效。
        /// </remarks>
        public AttackDefinition[] LightChain =>
            Weapon != null && Weapon.LightComboChain != null && Weapon.LightComboChain.Length > 0
                ? Weapon.LightComboChain
                : Config.LightComboChain;

        /// <summary>当前武器的重击。</summary>
        public AttackDefinition HeavyAttack => Weapon?.HeavyAttack ?? Config.HeavyAttack;

        /// <summary>当前武器的特殊攻击。</summary>
        public AttackDefinition SpecialAttack => Weapon?.SpecialAttack ?? Config.SpecialAttack;

        /// <summary>
        /// 专注充满时使用的强化特殊技能，回退到普通技能，避免充满的资源被白白消耗。
        /// </summary>
        public AttackDefinition EmpoweredSpecialAttack =>
            Weapon?.EmpoweredSpecialAttack ?? Config.EmpoweredSpecialAttack ?? SpecialAttack;

        /// <summary>当前武器的反击。</summary>
        public AttackDefinition RiposteAttack =>
            Weapon?.RiposteAttack ?? Config.RiposteAttack ?? HeavyAttack;

        /// <summary>对被击破防线使用的处决，回退到普通反击。</summary>
        public AttackDefinition ExecutionAttack => Config.ExecutionAttack ?? RiposteAttack;

        /// <summary>当前武器的弹反窗口。</summary>
        public float DeflectWindowSeconds =>
            Weapon != null ? Weapon.DeflectWindowSeconds : States.PlayerParryState.DeflectWindowSeconds;

        /// <summary>当前武器精准弹反造成的架势伤害。</summary>
        public float DeflectPostureDamage =>
            Weapon != null ? Weapon.DeflectPostureDamage : Config.DeflectPostureDamage;

        /// <summary>当前武器具备弹反能力时为 true。</summary>
        public bool CanDeflect => Weapon == null || Weapon.CanDeflect;

        /// <summary>
        /// 应忽略传入伤害期间为 true。
        /// </summary>
        /// <remarks>由闪避状态管理，进入时开启，进行到中途时清除。</remarks>
        public bool IsInvulnerable { get; set; }

        /// <summary>当前闪避运动的世界方向。</summary>
        public Vector3 DashDirection { get; set; } = Vector3.forward;

        /// <summary>再次闪避前剩余的秒数。</summary>
        public float DashCooldownRemaining { get; set; }

        /// <summary>
        /// 观察者能看到玩家执行的动作。
        /// </summary>
        /// <remarks>
        /// 各状态进入时设置，仅由提供给 Boss 的观察捕获读取。刻意采用粗略描述，因为观察者的视图也粗略。
        /// </remarks>
        public ObservableActionState ObservableState { get; set; } = ObservableActionState.Idle;

        /// <summary>当前可观察动作开始时的战斗时钟时间。</summary>
        public float ObservableStateEnteredAt { get; set; }

        /// <summary>已命中的攻击应中断玩家时设置。</summary>
        public bool StaggerRequested { get; set; }

        /// <summary>请求硬直的秒数。</summary>
        public float RequestedStaggerSeconds { get; set; }

        /// <summary>请求中断的原因。</summary>
        public StaggerReason RequestedStaggerReason { get; set; }

        /// <summary>决定玩家实际承受哪些击倒和击飞。</summary>
        /// <remarks>由控制器设置一次，各受击状态通过它改变玩家站姿。</remarks>
        public ReactionGate Reactions { get; set; }

        /// <summary>通过门控的击倒或击飞应使玩家失去站姿时设置。</summary>
        /// <remarks>
        /// 由状态机在下一次更新消费的标志，绝不当场改变状态。伤害在攻击者时间轴循环内裁决，
        /// 当场切换状态会在迭代中途取消动作。
        /// </remarks>
        public ImpactReaction RequestedReaction { get; set; }

        /// <summary>记录通过门控的击倒或击飞，供状态机处理。</summary>
        /// <param name="reaction">门控允许的反应。</param>
        public void RequestReaction(ImpactReaction reaction)
        {
            if (reaction == ImpactReaction.Knockdown || reaction == ImpactReaction.Launch)
            {
                RequestedReaction = reaction;
            }
        }

        /// <summary>此刻可开始闪避时为 true。</summary>
        public bool CanBeginDash =>
            DashCooldownRemaining <= 0f && Stamina.CanSpend(Config.DashStaminaCost);

        /// <summary>移动轴发生偏移时为 true。</summary>
        public bool HasMoveInput => Input.MoveDirection.sqrMagnitude > 0f;

        /// <summary>
        /// 选择闪避的运动方向。
        /// </summary>
        /// <remarks>
        /// 摇杆居中时回退到当前面向，使闪避始终有方向。因未按方向而静默失败的闪避与漏输入无法区分。
        /// </remarks>
        /// <returns>水平面上的单位方向。</returns>
        public Vector3 ResolveDashDirection()
        {
            Vector3 fromInput = Motor.ToWorldDirection(Input.MoveDirection);
            return fromInput.sqrMagnitude > Mathf.Epsilon ? fromInput.normalized : Motor.Facing;
        }

        /// <summary>记录一次硬直应中断玩家。</summary>
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

        /// <summary>推进不受当前状态限制的冷却计时器。</summary>
        /// <param name="deltaTime">经过的缩放时间。</param>
        public void TickCooldowns(float deltaTime)
        {
            if (DashCooldownRemaining > 0f)
            {
                DashCooldownRemaining = Mathf.Max(0f, DashCooldownRemaining - deltaTime);
            }

            if (SpecialCooldownRemaining > 0f)
            {
                SpecialCooldownRemaining = Mathf.Max(0f, SpecialCooldownRemaining - deltaTime);
            }
        }

        /// <summary>特殊技能冷却结束且资源足够时为 true。</summary>
        public bool CanUseSpecial =>
            SpecialAttack != null &&
            SpecialCooldownRemaining <= 0f &&
            Stamina.CanSpend(SpecialAttack.StaminaCost);

        /// <summary>本次挑战是否允许治疗，禁止治疗修正会清除此项。</summary>
        public bool HealingEnabled { get; set; } = true;

        /// <summary>允许治疗且尚有治疗次数时为 true。</summary>
        public bool CanHeal => HealingEnabled && HealChargesRemaining > 0 && Health.Current < Health.Maximum;

        /// <summary>发布位于玩家位置的简单事件。</summary>
        /// <param name="kind">发生的事件。</param>
        /// <param name="direction">事件适用时关联的方向。</param>
        public void PublishCombatEvent(CombatEventKind kind, Vector3 direction = default) =>
            Events.Publish(new CombatEvent
            {
                Kind = kind,
                Actor = CombatantTeam.Player,
                Timestamp = Time.CombatTime,
                Position = Motor.Position,
                Direction = direction
            });

        /// <summary>记录可观察动作及开始时刻。</summary>
        /// <param name="state">观察者此刻可见的动作。</param>
        public void SetObservableState(ObservableActionState state)
        {
            ObservableState = state;
            ObservableStateEnteredAt = Time.CombatTime;
        }
    }
}
