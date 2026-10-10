using System;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Perception;
using AdaptiveBossArena.Core.StateMachine;

namespace AdaptiveBossArena.Player.States
{
    /// <summary>
    /// 被挑飞至空中，落地前无法控制。
    /// </summary>
    /// <remarks>
    /// 落地由控制器读取，而非按挑飞速度预测，因此被抛到平台或悬崖外的玩家会在实际接触地面时落地。
    /// 配置的最长时间仍会结束此状态，因为只有每个阶段都受时限约束，防连续浮空上限才是真正的上限。
    /// </remarks>
    public sealed class PlayerAirborneState : StateBase<PlayerContext>
    {
        /// <summary>
        /// 地面接触可以被认作落地之前的等待时间。身体在起飞第一步仍位于原地面，若将其视为落地，
        /// 所有挑飞都会被取消。
        /// </summary>
        private const float LiftOffSeconds = 0.08f;

        private float _elapsed;

        /// <summary>被抛飞的身体已返回地面，或在空中过久时为 true。</summary>
        /// <param name="context">玩家上下文。</param>
        /// <returns>是否进入倒地状态。</returns>
        public bool HasLanded(PlayerContext context) =>
            _elapsed >= context.Config.MaximumAirborneSeconds ||
            (_elapsed >= LiftOffSeconds && context.Motor.IsGrounded && context.Motor.VerticalVelocity <= 0f);

        /// <inheritdoc />
        protected override void OnEnter(PlayerContext context)
        {
            context.SetObservableState(ObservableActionState.Airborne);
            context.RequestedReaction = ImpactReaction.None;
            _elapsed = 0f;

            LoseControl(context);
            context.Motor.Launch(context.Config.LaunchUpwardSpeed);
        }

        /// <inheritdoc />
        protected override void OnTick(PlayerContext context, float deltaTime)
        {
            _elapsed += deltaTime;
            context.StaggerRequested = false;
            context.Motor.Tick(deltaTime);
        }

        /// <summary>每个受击反应状态进入时的共同行为：玩家原有计划不再适用。</summary>
        /// <param name="context">玩家上下文。</param>
        internal static void LoseControl(PlayerContext context)
        {
            context.Attacks.Cancel();
            context.IsGuarding = false;
            context.IsInvulnerable = false;
            context.StaggerRequested = false;
            context.InputBuffer.Clear();
            context.Motor.Halt();
        }
    }

    /// <summary>
    /// 击倒或落地后躺在地面，无法受伤。
    /// </summary>
    /// <remarks>
    /// 此处无敌由反应门控提供，而非闪避标志，因此攻击倒地身体会直接被忽略，不会算作玩家从未执行的闪避。
    /// </remarks>
    public sealed class PlayerKnockedDownState : StateBase<PlayerContext>
    {
        private float _remainingSeconds;

        /// <summary>玩家倒地达到配置时长时为 true。</summary>
        /// <param name="context">玩家上下文。</param>
        /// <returns>是否开始起身。</returns>
        public bool IsComplete(PlayerContext context) => _remainingSeconds <= 0f;

        /// <inheritdoc />
        protected override void OnEnter(PlayerContext context)
        {
            context.SetObservableState(ObservableActionState.KnockedDown);
            context.RequestedReaction = ImpactReaction.None;
            context.Reactions.Land();
            _remainingSeconds = context.Config.KnockdownSeconds;

            PlayerAirborneState.LoseControl(context);
        }

        /// <inheritdoc />
        protected override void OnTick(PlayerContext context, float deltaTime)
        {
            _remainingSeconds -= deltaTime;
            context.StaggerRequested = false;

            context.Motor.Decelerate(deltaTime);
            context.Motor.Tick(deltaTime);
        }
    }

    /// <summary>
    /// 从地面起身，全程无敌，结束时补满架势。
    /// </summary>
    /// <remarks>
    /// 观察结果仍是击倒：起身属于同一个可见事件。若提供独立观察状态，就会告诉 Boss 控制恢复的准确时刻，
    /// 而旁观者只能凭肉眼判断。
    /// </remarks>
    public sealed class PlayerGetUpState : StateBase<PlayerContext>
    {
        private readonly Action _onStood;
        private float _elapsed;

        /// <summary>创建状态。</summary>
        /// <param name="onStood">
        /// 玩家重新站稳后调用。控制器在此补满架势，因为架势不归上下文所有。
        /// </param>
        public PlayerGetUpState(Action onStood)
        {
            _onStood = onStood;
        }

        /// <summary>起身进行程度，范围为零到一，用于驱动起身动画片段。</summary>
        /// <param name="context">玩家上下文。</param>
        /// <returns>已完成的起身比例。</returns>
        public float Progress(PlayerContext context) =>
            context.Config.GetUpSeconds > 0f ? Math.Min(1f, _elapsed / context.Config.GetUpSeconds) : 1f;

        /// <summary>玩家已站立时为 true。</summary>
        /// <param name="context">玩家上下文。</param>
        /// <returns>是否恢复控制。</returns>
        public bool IsComplete(PlayerContext context) => _elapsed >= context.Config.GetUpSeconds;

        /// <inheritdoc />
        protected override void OnEnter(PlayerContext context)
        {
            _elapsed = 0f;
            context.Reactions.BeginGettingUp();
        }

        /// <inheritdoc />
        protected override void OnTick(PlayerContext context, float deltaTime)
        {
            _elapsed += deltaTime;
            context.StaggerRequested = false;

            context.Motor.Decelerate(deltaTime);
            context.Motor.Tick(deltaTime);
        }

        /// <inheritdoc />
        protected override void OnExit(PlayerContext context)
        {
            context.Reactions.FinishGettingUp(context.Time.CombatTime);

            // 倒地期间的按键不代表站起后的行动意图。
            context.InputBuffer.Clear();
            _onStood?.Invoke();
        }
    }
}
