using AdaptiveBossArena.Core.Perception;
using AdaptiveBossArena.Core.StateMachine;
using AdaptiveBossArena.Learning;
using UnityEngine;

namespace AdaptiveBossArena.AI.States
{
    /// <summary>
    /// Boss 尚未看见值得反应的信息时等待。
    /// </summary>
    /// <remarks>
    /// 战斗刚开始、感知历史不足以满足延迟时进入。Boss 确实不知道玩家在哪，站着不动是诚实响应。
    /// </remarks>
    public sealed class BossIdleState : StateBase<BossContext>
    {
        /// <inheritdoc />
        protected override void OnTick(BossContext context, float deltaTime)
        {
            context.Motor.Decelerate(deltaTime);
            context.Motor.Tick(deltaTime);
        }
    }

    /// <summary>
    /// 保持 Boss 当前首选距离，观察空当。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Boss 大部分战斗处于此状态，也是适应变化最明显的状态。首选距离和进攻性均为可调值，
    /// 因此学到玩家喜欢保持距离后，Boss会更靠近并更早施压，而无需新增行为。
    /// </para>
    /// <para>
    /// 在首选距离附近刻意设置死区，避免 Boss已大致处于目标位置时不断进出振荡。
    /// </para>
    /// </remarks>
    public sealed class BossObserveState : StateBase<BossContext>
    {
        /// <inheritdoc />
        protected override void OnEnter(BossContext context)
        {
            // 进入此状态意味着没有已确定的攻击。在此清除选择，
            // 防止中断前基于已过时场景作出的决定以后复活。
            context.PendingAttack = null;

            // 进入时开启新的反应延迟，确保即使空当明显，Boss也不能到达同一瞬间立即离开并攻击。
            context.BeginReactionDelay();
        }

        /// <inheritdoc />
        protected override void OnTick(BossContext context, float deltaTime)
        {
            PlayerObservation observation = context.Perceived;

            if (!observation.IsValid)
            {
                context.Motor.Decelerate(deltaTime);
                context.Motor.Tick(deltaTime);
                return;
            }

            context.Motor.FaceDirection(context.DirectionToPlayer, deltaTime);

            // 缓慢绕圈逼近，接近与让步之间平滑变化；参见 BossPacing。
            context.Motor.MoveInDirection(
                BossPacing.Stalk(
                    context.DirectionToPlayer, context.DistanceToPlayer, context.PreferredRange,
                    context.Config.StalkSpeedFraction),
                deltaTime);

            context.Motor.Tick(deltaTime);
        }
    }

    /// <summary>
    /// 带明确意图接近目标，而非漫无目的漂移。
    /// </summary>
    /// <remarks>
    /// 与观察状态区分，因为决定攻击的 Boss应投入接近玩家，而非继续绕圈。
    /// 作出该决定的积极程度由学习后的进攻性控制。
    /// </remarks>
    public sealed class BossApproachState : StateBase<BossContext>
    {
        /// <summary>Boss 已接近到所选攻击能触及时为 true。</summary>
        /// <param name="context">Boss 上下文。</param>
        /// <returns>接近是否成功。</returns>
        public bool HasArrived(BossContext context)
        {
            if (context.PendingAttack == null)
            {
                return true;
            }

            float reach = context.PendingAttack.EffectiveReach;

            return context.DistanceToPlayer <= reach;
        }

        /// <inheritdoc />
        protected override void OnTick(BossContext context, float deltaTime)
        {
            Vector3 toPlayer = context.DirectionToPlayer;

            context.Motor.FaceDirection(toPlayer, deltaTime);

            // 攻击将够到目标时走近，只有真实距离较大才奔跑，并按学习后的进攻性缩放；参见 BossPacing。
            float reach = context.PendingAttack != null ? context.PendingAttack.EffectiveReach : 0f;
            float speed = BossPacing.ApproachSpeed(
                context.DistanceToPlayer - reach,
                context.Config.ApproachRunDistance,
                context.Config.ApproachWalkFraction,
                context.Tuning.Get(BossTuningParameter.Aggression));

            context.Motor.MoveInDirection(toPlayer.normalized * speed, deltaTime);
            context.Motor.Tick(deltaTime);
        }
    }
}
