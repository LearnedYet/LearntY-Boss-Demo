using AdaptiveBossArena.Core.Perception;
using AdaptiveBossArena.Core.StateMachine;
using UnityEngine;

namespace AdaptiveBossArena.Player.States
{
    /// <summary>
    /// 静止站立。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="PlayerMoveState"/>分开，而非合并为一个地面状态。分离几乎没有额外成本，
    /// 却能容纳待机专属表现，如呼吸动画、站姿和第一步不同的脚步声，无需在移动代码中散布速度判断。
    /// </remarks>
    public sealed class PlayerIdleState : StateBase<PlayerContext>
    {
        /// <inheritdoc />
        protected override void OnEnter(PlayerContext context)
        {
            context.SetObservableState(ObservableActionState.Idle);
        }

        /// <inheritdoc />
        protected override void OnTick(PlayerContext context, float deltaTime)
        {
            // 仍然减速：从闪避滑行进入待机的角色应惯性滑至停止，而非突然停住。
            context.Motor.Decelerate(deltaTime);

            // 锁定时，站立的骑士会随着 Boss 绕行而持续正对它。
            if (context.IsLockedOn)
            {
                context.Motor.FaceDirection(context.FacingTowardThreat, deltaTime);
            }

            context.Motor.Tick(deltaTime);
        }
    }

    /// <summary>受玩家控制跑动。</summary>
    public sealed class PlayerMoveState : StateBase<PlayerContext>
    {
        /// <inheritdoc />
        protected override void OnEnter(PlayerContext context)
        {
            context.SetObservableState(ObservableActionState.Moving);
        }

        /// <inheritdoc />
        protected override void OnTick(PlayerContext context, float deltaTime)
        {
            Vector2 input = context.Input.MoveDirection;
            context.Motor.ApplyMoveInput(input, deltaTime);

            // 朝向跟随摇杆，而非速度。原先跟随速度使反向输入时骑士只能随动量消退缓慢转身，
            // 明显落后于输入方向。锁定时保持面向 Boss，方向混合则播放横移和后退动作。
            Vector3 facing = context.IsLockedOn
                ? context.FacingTowardThreat
                : context.Motor.ToWorldDirection(input);

            context.Motor.FaceDirection(facing, deltaTime);
            context.Motor.Tick(deltaTime);
        }
    }
}
