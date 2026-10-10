using AdaptiveBossArena.Combat.Movement;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Perception;
using AdaptiveBossArena.Core.StateMachine;
using UnityEngine;

namespace AdaptiveBossArena.Player.States
{
    /// <summary>
    /// 用于闪避的翻滚，包括其无敌帧。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 无敌仅覆盖翻滚开头，而非整个翻滚。这个选择使闪避成为技能，而非按钮：提前翻滚可以避开攻击，
    /// 太晚翻滚则会移入攻击并受罚。窗口使用绝对时间，并刻意保持旧滑行的 0.135 秒，
    /// 因为 Boss 的时机及其对玩家闪避习惯的学习均建立在该数值上。
    /// </para>
    /// <para>
    /// 翻滚替代了匀速滑行。它按 <see cref="DodgeRollProfile"/>爆发起步并逐渐减速，
    /// 在无敌期间完成大部分位移；开头仍可由摇杆调整方向，缓慢尾段可取消并转为缓存攻击。
    /// 进入时立即调整朝向，让角色明显投入动作，也为 Boss 的闪避预测自适应提供可读信息。
    /// </para>
    /// </remarks>
    public sealed class PlayerDashState : StateBase<PlayerContext>
    {
        /// <summary>翻滚持续达到配置时长时为 true。</summary>
        /// <param name="context">玩家上下文。</param>
        /// <returns>翻滚是否完成。</returns>
        public bool IsComplete(PlayerContext context) => TimeInState >= context.Config.DashDurationSeconds;

        /// <summary>翻滚减速到可以取消并转为攻击时为 true。</summary>
        /// <param name="context">玩家上下文。</param>
        /// <returns>缓存攻击是否可以打断翻滚。</returns>
        public bool CanCancel(PlayerContext context) =>
            CancelRules.RollCanBeLeft(TimeInState, context.Config.DashDurationSeconds, context.Config.DashCancelFraction);

        /// <inheritdoc />
        protected override void OnEnter(PlayerContext context)
        {
            context.SetObservableState(ObservableActionState.Dashing);

            // 在此处而非转移时消耗体力，保证代价与移动始终一致；只要翻滚发生，就已支付体力。
            context.Stamina.TrySpend(context.Config.DashStaminaCost);

            context.DashDirection = context.ResolveDashDirection();
            context.IsInvulnerable = true;
            context.DashStartedAt = context.Time.CombatTime;

            context.Motor.SnapToDirection(context.DashDirection);
            ApplyRollVelocity(context);

            // 发布事件，使 Boss 能了解玩家习惯向哪个方向翻滚。方向属于合理可观察信息；
            // Boss 和任何旁观者一样观察翻滚的发生。
            context.PublishCombatEvent(CombatEventKind.DodgePerformed, context.DashDirection);
        }

        /// <inheritdoc />
        protected override void OnTick(PlayerContext context, float deltaTime)
        {
            if (context.IsInvulnerable && TimeInState >= context.Config.InvulnerabilitySeconds)
            {
                context.IsInvulnerable = false;
            }

            SteerEarly(context, deltaTime);
            ApplyRollVelocity(context);

            context.Motor.Tick(deltaTime);
        }

        /// <inheritdoc />
        protected override void OnExit(PlayerContext context)
        {
            // 速度保持为翻滚结束时的数值。曲线已将其减速，因此自然结束的翻滚几乎静止，
            // 取消并转为攻击时则保留当时的惯性，无需另设退出速度比例也能保持衔接流畅。
            context.IsInvulnerable = false;
            context.DashCooldownRemaining = context.Config.DashCooldownSeconds;
        }

        /// <summary>只在翻滚起始时允许摇杆调整方向。</summary>
        /// <remarks>
        /// 若整个翻滚都可转向，实际位移便不可预测，并悄然破坏 Boss 距离逻辑所依据的间距。
        /// 短窗口已足以纠正起步时偏差几度的翻滚，这正是转向的用途。
        /// </remarks>
        private static void SteerEarly(PlayerContext context, float deltaTime)
        {
            float steerUntil = context.Config.DashDurationSeconds * context.Config.DashSteerFraction;

            if (!context.HasMoveInput || context.Motor == null)
            {
                return;
            }

            if (context.Time.CombatTime - context.DashStartedAt >= steerUntil)
            {
                return;
            }

            Vector3 wanted = context.Motor.ToWorldDirection(context.Input.MoveDirection);

            if (wanted.sqrMagnitude < Mathf.Epsilon)
            {
                return;
            }

            float maxRadians = context.Config.TurnSpeedDegreesPerSecond * Mathf.Deg2Rad * deltaTime;
            context.DashDirection = Vector3.RotateTowards(context.DashDirection, wanted.normalized, maxRadians, 0f);
            context.Motor.SnapToDirection(context.DashDirection);
        }

        private void ApplyRollVelocity(PlayerContext context)
        {
            float speed = DodgeRollProfile.SpeedAt(
                TimeInState, context.Config.DashDurationSeconds, context.Config.DashDistance);

            context.Motor.SetPlanarVelocity(context.DashDirection * speed);
        }
    }
}
