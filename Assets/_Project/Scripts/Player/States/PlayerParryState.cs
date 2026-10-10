using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Perception;
using AdaptiveBossArena.Core.StateMachine;
using UnityEngine;

namespace AdaptiveBossArena.Player.States
{
    /// <summary>
    /// 保持防御，并在其起始短时段内弹反。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 战斗的核心机制。按下防御会开启短暂弹反窗口；窗口内到来的攻击会被完全拨开、削减攻击者架势，
    /// 并触发清晰的金属撞击声和停顿，使时机值得学习。按得太早或保持太久，同一输入便成为普通格挡：
    /// 承受住攻击，但付出削减伤害和防御者自身架势。
    /// </para>
    /// <para>
    /// 刻意让弹反成为进攻选项，而非安全选项。格挡持续消耗自身架势直至崩溃，弹反则消耗对方架势。
    /// 这种反转避免战斗变成龟缩，也让 Boss学会玩家弹反节奏后的佯攻有了意义。
    /// </para>
    /// <para>
    /// 状态只暴露窗口，不自行解析伤害。玩家的 <see cref="IDamageable"/>实现是唯一决定如何处理传入攻击
    /// 的位置，因此时机知识集中在这里，解析则集中在那里。
    /// </para>
    /// </remarks>
    public sealed class PlayerParryState : StateBase<PlayerContext>
    {
        /// <summary>按下防御后，攻击会被弹反而非格挡的时长。</summary>
        public const float DeflectWindowSeconds = 0.2f;

        /// <summary>防御保持的最短时间，使轻点也表现为主动动作。</summary>
        private const float MinimumGuardSeconds = 0.2f;

        /// <summary>玩家被迫放下防御前可持续保持的最长时间。</summary>
        private const float MaximumGuardSeconds = 1.6f;

        /// <summary>防御时每秒消耗的体力，使持续保持防御有代价。</summary>
        private const float GuardStaminaDrainPerSecond = 12f;

        /// <summary>此刻到来的攻击会被完全弹反时为 true。</summary>
        /// <remarks>
        /// 由共享解析器回答，而非复制第二份时机规则，使此处报告的结果与实际伤害路径始终一致。
        /// 询问时不针对特定攻击，因此它只是供体力消耗和提示使用的表现读取，并非实际判定本身。
        /// </remarks>
        /// <param name="context">玩家上下文。</param>
        /// <returns>弹反窗口当前是否开启。</returns>
        public bool IsInDeflectWindow(PlayerContext context) =>
            DefenceResolver.ResolveDefence(new DefenceQuery
            {
                IsDefending = true,
                CanDeflect = context.CanDeflect,
                CanBlock = true,
                TimeInDefenceSeconds = TimeInState,
                DeflectWindowSeconds = context.DeflectWindowSeconds
            }) == DefenceOutcome.Deflected;

        /// <summary>防御可以松开时为 true。</summary>
        /// <param name="context">玩家上下文。</param>
        /// <returns>玩家是否可以再次行动。</returns>
        public bool IsComplete(PlayerContext context)
        {
            if (TimeInState < MinimumGuardSeconds)
            {
                return false;
            }

            // 松开按钮时可提前结束；保持过久或体力耗尽时自动放下。若可无限保持防御，决策就会失去意义。
            bool stillHeld = context.Input.IsHeld(Controls.PlayerInputAction.Guard);

            return !stillHeld ||
                   TimeInState >= MaximumGuardSeconds ||
                   context.Stamina.Current <= 0f;
        }

        /// <inheritdoc />
        protected override void OnEnter(PlayerContext context)
        {
            context.SetObservableState(ObservableActionState.Guarding);
            context.IsGuarding = true;

            context.PublishCombatEvent(CombatEventKind.GuardRaised);
        }

        /// <inheritdoc />
        protected override void OnTick(PlayerContext context, float deltaTime)
        {
            // 防御使玩家定在原地。若能一边防御一边走动，它就会严格优于单纯移动，战斗也就失去间距管理。
            context.Motor.Decelerate(deltaTime);
            context.Motor.Tick(deltaTime);

            context.Motor.FaceDirection(context.FacingTowardThreat, deltaTime);

            // 只有持续防御阶段消耗体力。若弹反窗口本身也收费，就会惩罚整个机制想鼓励的准确时机。
            if (!IsInDeflectWindow(context))
            {
                context.Stamina.TrySpend(GuardStaminaDrainPerSecond * deltaTime);
            }
        }

        /// <inheritdoc />
        protected override void OnExit(PlayerContext context)
        {
            context.IsGuarding = false;
        }
    }

    /// <summary>
    /// 反击：对已破防目标的一次毁灭性打击。
    /// </summary>
    /// <remarks>
    /// 只在 Boss 架势崩溃时可进入，且窗口很短。这是弹反系统的完整回报：每次弹反都为其积累一点进度，
    /// 使坚守位置表现为取得进展，而非单纯求生。
    /// </remarks>
    /// <summary>
    /// 投入一次刀刃挥击，将攻击拨开。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 玩家对旧防御的评价是“感觉并不像招架”：只有一个防御动作，开头五分之一秒恰好可以弹反。
    /// 这是补齐的另一半：主动挥击、短窗口，以及长到足以惩罚挥空的后摇，才使成功招架有分量。
    /// 它比弹反削减更多 Boss 防御，并使其后仰更久。
    /// </para>
    /// <para>
    /// 无法应对不可格挡、不可招架攻击或冲击波。这些仍必须闪避，因此招架不会成为所有问题的唯一答案。
    /// </para>
    /// </remarks>
    public sealed class PlayerParryStrikeState : StateBase<PlayerContext>
    {
        /// <summary>挥击开始后，可以拨开攻击的时长。</summary>
        public const float WindowSeconds = 0.22f;

        /// <summary>包含窗口与后摇的完整动作；挥空会暴露玩家。</summary>
        public const float DurationSeconds = 0.72f;

        /// <summary>一次招架消耗的体力，使玩家无法只靠连续乱按。</summary>
        public const float StaminaCost = 14f;

        /// <summary>此刻到来的攻击会被拨开时为 true。</summary>
        public bool IsWindowOpen => TimeInState <= WindowSeconds;

        /// <summary>挥击及其后摇结束时为 true。</summary>
        /// <param name="context">玩家上下文。</param>
        /// <returns>玩家是否可以再次行动。</returns>
        public bool IsComplete(PlayerContext context) => TimeInState >= DurationSeconds;

        /// <inheritdoc />
        protected override void OnEnter(PlayerContext context)
        {
            context.SetObservableState(ObservableActionState.Parrying);
            context.IsParryStriking = true;
            context.Stamina.TrySpend(StaminaCost);

            context.PublishCombatEvent(CombatEventKind.GuardRaised);
        }

        /// <inheritdoc />
        protected override void OnTick(PlayerContext context, float deltaTime)
        {
            // 定在原地并转向 Boss：招架瞄准来袭攻击，而非对着空气挥击。
            context.Motor.Decelerate(deltaTime);
            context.Motor.FaceDirection(context.FacingTowardThreat, deltaTime);
            context.Motor.Tick(deltaTime);
        }

        /// <inheritdoc />
        protected override void OnExit(PlayerContext context) => context.IsParryStriking = false;
    }

    public sealed class PlayerRiposteState : StateBase<PlayerContext>
    {
        /// <summary>反击完成时为 true。</summary>
        /// <param name="context">玩家上下文。</param>
        /// <returns>玩家是否可以再次行动。</returns>
        public bool IsComplete(PlayerContext context) => !context.Attacks.IsRunning;

        /// <inheritdoc />
        protected override void OnEnter(PlayerContext context)
        {
            context.SetObservableState(ObservableActionState.HeavyAttacking);
            context.RiposteConsumed = true;

            // 处决：对破防目标更重的多段打击；未配置时回退为普通反击。
            AttackDefinition execution = context.ExecutionAttack;

            if (execution == null)
            {
                return;
            }

            // 立即转向目标，而非逐渐转向，因为若角色仍在转身导致反击落空，
            // 会让玩家觉得游戏拒绝了已赢得的奖励。
            context.Motor.SnapToDirection(context.FacingTowardThreat);
            context.Attacks.Begin(execution);

            context.PublishCombatEvent(CombatEventKind.Riposte);
        }

        /// <inheritdoc />
        protected override void OnTick(PlayerContext context, float deltaTime)
        {
            context.Motor.Decelerate(deltaTime);
            context.Motor.Tick(deltaTime);
            context.Attacks.Tick(deltaTime);
        }

        /// <inheritdoc />
        protected override void OnExit(PlayerContext context)
        {
            if (context.Attacks.IsRunning)
            {
                context.Attacks.Cancel();
            }
        }
    }
}
