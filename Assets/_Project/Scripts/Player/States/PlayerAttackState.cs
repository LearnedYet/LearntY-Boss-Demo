using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Perception;
using AdaptiveBossArena.Core.StateMachine;
using AdaptiveBossArena.Player.Controls;
using UnityEngine;

namespace AdaptiveBossArena.Player.States
{
    /// <summary>
    /// 执行攻击，包括衔接连招的下一段。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 一个状态涵盖玩家所有攻击。轻击和重击的区别完全由 <see cref="AttackDefinition"/>中的数据决定；
    /// 若每种攻击各设一个状态，就会出现五份仅所读资源不同的重复逻辑。
    /// </para>
    /// <para>
    /// 连招在状态内部衔接，而非通过离开再进入的转移。退出并重入会重置状态自身计时、丢失连招索引，
    /// 也会让其它转移插入玩家已决定执行的两个连招段之间。
    /// </para>
    /// <para>
    /// 攻击期间交出移动控制。攻击产生的唯一位移是突进，使已投入的挥击同时成为位置与时机的真实决策。
    /// </para>
    /// </remarks>
    public sealed class PlayerAttackState : StateBase<PlayerContext>
    {
        /// <summary>前摇期间可使用的正常移速比例，用于瞄准挥击。</summary>
        private const float StartupControlFraction = 0.35f;

        /// <summary>攻击及其衔接的连招均完成时为 true。</summary>
        /// <param name="context">玩家上下文。</param>
        /// <returns>玩家是否可以再次行动。</returns>
        public bool IsComplete(PlayerContext context) => !context.Attacks.IsRunning;

        /// <inheritdoc />
        protected override void OnEnter(PlayerContext context)
        {
            BeginAttack(context, context.PendingAttack);
        }

        /// <inheritdoc />
        protected override void OnTick(PlayerContext context, float deltaTime)
        {
            TrackTarget(context, deltaTime);
            ApplyAttackMovement(context, deltaTime);

            context.Motor.Tick(deltaTime);
            context.Attacks.Tick(deltaTime);

            TryChainCombo(context);
        }

        /// <summary>
        /// 闪避可以取消攻击时为 true。
        /// </summary>
        /// <remarks>
        /// 后摇中可取消并转为闪避，避免已投入的攻击让玩家觉得按下攻击键反而受罚。
        /// 刻意等到判定框关闭才开放取消，因此攻击投入仍有实际意义。
        /// </remarks>
        /// <param name="context">玩家上下文。</param>
        /// <returns>闪避是否可以打断攻击。</returns>
        public bool CanDashCancel(PlayerContext context) =>
            CancelRules.AttackCanDodge(context.Attacks.Phase);

        /// <summary>攻击后摇进行到足以从中举起防御时为 true。</summary>
        /// <param name="context">玩家上下文。</param>
        /// <returns>防御是否可以打断攻击。</returns>
        public bool CanGuardCancel(PlayerContext context) =>
            CancelRules.AttackCanGuard(context.Attacks.Phase, context.Attacks.ElapsedSeconds, context.Attacks.CurrentAttack);

        /// <inheritdoc />
        protected override void OnExit(PlayerContext context)
        {
            // 挥击中途退出意味着玩家受到打断。必须拆除攻击，
            // 否则下一次攻击时执行器仍会把判定框视为开启。
            if (context.Attacks.IsRunning)
            {
                context.Attacks.Cancel();
            }

            context.ComboIndex = 0;
        }

        /// <summary>仅在前摇早期将挥击转向目标。</summary>
        /// <remarks>
        /// 挥击开始时仍会立即转向摇杆方向：轻击前摇太短，无法按行走转身速度完成转向。
        /// 摇杆有输入时玩家自行瞄准，无输入时挥击缓慢转向 Boss。无论哪种方式，
        /// 追踪都在与 Boss相同的前摇节点停止，使双方挥击遵循同一投入规则。
        /// </remarks>
        private static void TrackTarget(PlayerContext context, float deltaTime)
        {
            AttackDefinition attack = context.Attacks.CurrentAttack;

            if (attack == null || !attack.CanTrack(context.Attacks.Phase, context.Attacks.ElapsedSeconds))
            {
                return;
            }

            Vector3 direction = context.HasMoveInput
                ? context.Motor.ToWorldDirection(context.Input.MoveDirection)
                : context.FacingTowardThreat;

            context.Motor.FaceDirection(direction, deltaTime);
        }

        /// <summary>开始一次攻击并支付代价。</summary>
        private static void BeginAttack(PlayerContext context, AttackDefinition attack)
        {
            if (attack == null)
            {
                return;
            }

            context.SetObservableState(ObservableStateFor(attack.DamageType));
            context.Stamina.TrySpend(attack.StaminaCost);

            // 挥击开始时，朝向立即转至摇杆方向。随后仍可短暂追踪，但只限前摇早期（见 TrackTarget）；
            // 之后挥击已经投入，因此转向跟随闪避中的对手仍需为所选时机承担代价。
            if (context.HasMoveInput)
            {
                context.Motor.SnapToDirection(context.Motor.ToWorldDirection(context.Input.MoveDirection));
            }

            context.Attacks.Begin(attack);
        }

        /// <summary>
        /// 攻击期间移动角色：突进、投入，随后恢复控制。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 让角色整个攻击期间静止是错误的，近一秒的重击尤其严重。没有动画来解释停顿时，
        /// 角色只会看起来停止响应，像游戏卡死，而非已投入的挥击。
        /// </para>
        /// <para>
        /// 现在控制权逐步恢复。前摇允许部分正常移速以瞄准挥击；生效窗口必须完全投入，
        /// 因为此刻正承担决策代价；后摇后半段则恢复控制。攻击仍会占用真实位置，但角色始终回应摇杆。
        /// </para>
        /// </remarks>
        private static void ApplyAttackMovement(PlayerContext context, float deltaTime)
        {
            AttackDefinition attack = context.Attacks.CurrentAttack;

            if (attack == null)
            {
                context.Motor.Decelerate(deltaTime);
                return;
            }

            AttackPhase phase = context.Attacks.Phase;

            if (phase == AttackPhase.Startup && attack.LungeSpeed > 0f)
            {
                context.Motor.SetPlanarVelocity(context.Motor.Facing * attack.LungeSpeed);
                return;
            }

            float control = ResolveControlFraction(context, attack, phase);

            if (control <= 0f || !context.HasMoveInput)
            {
                context.Motor.Decelerate(deltaTime);
                return;
            }

            context.Motor.ApplyMoveInput(context.Input.MoveDirection * control, deltaTime);
        }

        /// <summary>角色在当前阶段可以使用的正常移速比例。</summary>
        private static float ResolveControlFraction(
            PlayerContext context,
            AttackDefinition attack,
            AttackPhase phase)
        {
            switch (phase)
            {
                case AttackPhase.Startup:
                    // 足以调整挥击，但不足以借此重新拉开位置。
                    return StartupControlFraction;

                case AttackPhase.Recovery:
                    // 后摇后半段恢复控制，使较长的重击后摇表现为重量感，而非输入丢失。
                    float intoRecovery = context.Attacks.ElapsedSeconds - attack.ActiveEndSeconds;
                    float fraction = attack.RecoverySeconds <= 0f
                        ? 1f
                        : Mathf.Clamp01(intoRecovery / attack.RecoverySeconds);

                    return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 1f, fraction));

                default:
                    return 0f;
            }
        }

        /// <summary>窗口允许时推进轻击连招的下一段。</summary>
        private static void TryChainCombo(PlayerContext context)
        {
            if (!context.Attacks.IsInComboWindow)
            {
                return;
            }

            AttackDefinition[] chain = context.LightChain;
            int nextIndex = context.ComboIndex + 1;

            if (chain == null || nextIndex >= chain.Length || chain[nextIndex] == null)
            {
                return;
            }

            if (!context.InputBuffer.TryConsume(PlayerInputAction.LightAttack, context.Time.CombatTime))
            {
                return;
            }

            context.ComboIndex = nextIndex;
            context.PendingAttack = chain[nextIndex];

            BeginAttack(context, chain[nextIndex]);
        }

        /// <summary>将攻击类别映射为旁观者可见的表现。</summary>
        private static ObservableActionState ObservableStateFor(DamageType damageType)
        {
            switch (damageType)
            {
                case DamageType.Heavy: return ObservableActionState.HeavyAttacking;
                case DamageType.Special: return ObservableActionState.UsingAbility;
                default: return ObservableActionState.LightAttacking;
            }
        }
    }

    /// <summary>
    /// 引导治疗。
    /// </summary>
    /// <remarks>
    /// 刻意设置较长引导和显眼状态。治疗必须是 Boss 可合理惩罚的决策，否则玩家可以随时补满生命，
    /// 战斗便失去压力。治疗也是 Boss 画像记录的习惯之一，
    /// 因此总在同一时刻治疗的玩家会发现那个时刻逐渐不再安全。
    /// </remarks>
    public sealed class PlayerHealState : StateBase<PlayerContext>
    {
        private bool _wasInterrupted;

        /// <summary>引导完成时为 true。</summary>
        /// <param name="context">玩家上下文。</param>
        /// <returns>玩家是否可以再次行动。</returns>
        public bool IsComplete(PlayerContext context) =>
            _wasInterrupted || TimeInState >= context.Config.HealChannelSeconds;

        /// <summary>治疗仍可放弃并转为闪避时为 true，治疗次数会损失。</summary>
        /// <param name="context">玩家上下文。</param>
        /// <returns>闪避是否可以打断治疗。</returns>
        public bool CanDodgeCancel(PlayerContext context) =>
            CancelRules.HealCanDodge(TimeInState, context.Config.HealChannelSeconds);

        /// <inheritdoc />
        protected override void OnEnter(PlayerContext context)
        {
            context.SetObservableState(ObservableActionState.Healing);

            _wasInterrupted = false;
            context.HealChargesRemaining = Mathf.Max(0, context.HealChargesRemaining - 1);

            context.PublishCombatEvent(CombatEventKind.HealStarted);
        }

        /// <inheritdoc />
        protected override void OnTick(PlayerContext context, float deltaTime)
        {
            context.Motor.Decelerate(deltaTime);
            context.Motor.Tick(deltaTime);

            // 引导中途收到硬直请求意味着攻击命中。治疗次数已经消耗，
            // 因此受到打断会让玩家直接损失这次治疗。
            if (context.StaggerRequested)
            {
                _wasInterrupted = true;
            }
        }

        /// <inheritdoc />
        protected override void OnExit(PlayerContext context)
        {
            // 引导中受击，或完成前放弃并转为闪避：两种情况都会损失治疗次数。
            if (_wasInterrupted || TimeInState < context.Config.HealChannelSeconds)
            {
                return;
            }

            context.Health.Heal(context.Config.HealAmount);
            context.PublishCombatEvent(CombatEventKind.HealCompleted);
        }
    }
}
