using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Core.Perception;
using AdaptiveBossArena.Core.StateMachine;
using UnityEngine;

namespace AdaptiveBossArena.Player.States
{
    /// <summary>
    /// 受到穿透防御的攻击后后仰。
    /// </summary>
    /// <remarks>
    /// 硬直持续时间放在上下文中，而非在此固定，因为打断多久属于攻击自身属性。
    /// 轻刺和 Boss 砸击都会造成硬直，但只有其中一种应让玩家失去接下来多次决策的机会。
    /// </remarks>
    public sealed class PlayerStaggerState : StateBase<PlayerContext>
    {
        private float _remainingSeconds;

        /// <summary>打断持续结束时为 true。</summary>
        /// <param name="context">玩家上下文。</param>
        /// <returns>玩家是否可以再次行动。</returns>
        public bool IsComplete(PlayerContext context) => _remainingSeconds <= 0f;

        /// <inheritdoc />
        protected override void OnEnter(PlayerContext context)
        {
            context.SetObservableState(ObservableActionState.Staggered);

            _remainingSeconds = context.RequestedStaggerSeconds;
            context.StaggerRequested = false;

            // 玩家硬直不发布通知，因此此处原因不会造成错误的表现，但仍须消费它。若保留该值，
            // 下一次普通攻击便会继承它；会发布通知的 Boss 硬直也共享该字段约定。
            context.RequestedStaggerReason = StaggerReason.Hit;

            // 若不清空，受击前缓存的按键会在恢复控制的瞬间触发，消耗玩家已不再想使用的攻击或闪避。
            context.InputBuffer.Clear();
        }

        /// <inheritdoc />
        protected override void OnTick(PlayerContext context, float deltaTime)
        {
            // 硬直期间再被命中无法重入此状态，因为状态机拒绝转移至当前状态。
            // 因此在此处接收它并延长打断，而非悄然丢弃。
            if (context.StaggerRequested)
            {
                _remainingSeconds = Mathf.Max(_remainingSeconds, context.RequestedStaggerSeconds);
                context.StaggerRequested = false;
                context.RequestedStaggerReason = StaggerReason.Hit;
            }

            _remainingSeconds -= deltaTime;

            context.Motor.Decelerate(deltaTime);
            context.Motor.Tick(deltaTime);
        }
    }

    /// <summary>
    /// 被击败。没有向外转移的终止状态。
    /// </summary>
    /// <remarks>
    /// 进入时暂停输入，而非仅忽略输入，使死亡动画期间的按键无法被缓存并在战斗重启后重放。
    /// </remarks>
    public sealed class PlayerDeadState : StateBase<PlayerContext>
    {
        /// <inheritdoc />
        protected override void OnEnter(PlayerContext context)
        {
            context.SetObservableState(ObservableActionState.Dead);

            context.IsInvulnerable = true;
            context.InputBuffer.Clear();
            context.Input.SetEnabled(false);
            context.Motor.Halt();
        }

        /// <inheritdoc />
        protected override void OnTick(PlayerContext context, float deltaTime)
        {
            context.Motor.Tick(deltaTime);
        }
    }
}
