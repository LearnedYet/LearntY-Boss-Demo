using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Perception;
using AdaptiveBossArena.Core.Services;
using AdaptiveBossArena.Core.StateMachine;
using AdaptiveBossArena.Learning;
using UnityEngine;

namespace AdaptiveBossArena.AI.States
{
    /// <summary>
    /// 执行攻击，包括 Boss 对目标位置的提前量。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 瞄准在前摇期间确定，之后锁定。Boss 转向预计玩家将到达的位置，挥击生效后无法修正。
    /// 这使整场战斗可通过移动闪避：有效帧仍追踪目标的攻击无法靠移动避开，只能依靠无敌。
    /// </para>
    /// <para>
    /// 提前量体现闪避预测适应。预测强度为零时，Boss瞄准上次观察到的位置。玩家闪避方向出现偏好后，
    /// 它开始瞄准该习惯暗示的下一位置。它仍在猜测，改变方向的玩家可以胜过预测。
    /// </para>
    /// </remarks>
    public sealed class BossAttackState : StateBase<BossContext>
    {
        /// <summary>全预测强度时 Boss 瞄准提前的秒数，按玩家运动计算。</summary>
        private const float MaxLeadSeconds = 0.35f;

        /// <summary>达到此比例的前摇后取消佯攻。</summary>
        /// <remarks>
        /// 足够晚以使玩家已投入应对，又足够早，使Boss 在玩家能惩罚取消行为前恢复。
        /// </remarks>
        private const float FeintCancelFraction = 0.6f;

        /// <summary>
        /// 适应前继续每个额外连招段的概率，使长连招逐渐更少出现。
        /// </summary>
        /// <remarks>
        /// 未经训练的基线。学会连续攻击的 Boss 通过
        /// <see cref="BossTuningParameter.ComboExtensionChance"/>提高此值；
        /// 暴露它使测试可引用基线而非重复数字。
        /// </remarks>
        public const float BaseComboContinueChance = 0.6f;

        /// <summary>佯攻付出的完整冷却比例，因为没有真正完成投入。</summary>
        private const float FeintCooldownFraction = 0.45f;

        private bool _isFeinting;
        private int _comboRemaining;

        /// <summary>
        /// 攻击及其延伸出的全部连招结束时为 true。
        /// </summary>
        /// <remarks>
        /// 后续攻击仍排队时报告未完成，避免状态机在连招段之间把控制权交给恢复状态。
        /// </remarks>
        /// <param name="context">Boss 上下文。</param>
        /// <returns>Boss 是否可以再次行动。</returns>
        public bool IsComplete(BossContext context) =>
            !context.Attacks.IsRunning && _comboRemaining <= 0;

        /// <inheritdoc />
        protected override void OnEnter(BossContext context)
        {
            AttackDefinition attack = context.PendingAttack;

            if (attack == null)
            {
                return;
            }

            // 在挥击开始前决定，因此预警确实与真攻击无法区分。中途才决定会让细心玩家读出差异。
            _isFeinting = context.Random.NextBool(
                Mathf.Clamp01(context.Tuning.Get(BossTuningParameter.FeintChance)));

            // 佯攻按定义只有单段；只有实际投入的起手会延伸为连招。学会连招施压的 Boss 会连接更多攻击。
            _comboRemaining = _isFeinting
                ? 0
                : ComboLength(
                    context.PhaseIndex,
                    context.Random,
                    BaseComboContinueChance +
                    context.Tuning.Get(BossTuningParameter.ComboExtensionChance));

            // 突进或不可格挡起手是真正投入，玩家可使挥空的 Boss 付出代价；轻微试探不是。
            // 佯攻永远不算投入，因为没有真正出招。
            context.LastAttackWasCommitted = !_isFeinting && (attack.LungeSpeed > 0f || attack.Unblockable);

            context.Motor.Halt();
            context.Attacks.Begin(attack, context.DistanceToPlayer);
        }

        /// <inheritdoc />
        protected override void OnTick(BossContext context, float deltaTime)
        {
            AttackDefinition attack = context.Attacks.CurrentAttack;

            if (TryCancelFeint(context, attack))
            {
                return;
            }

            if (attack != null && context.Attacks.Phase == AttackPhase.Startup)
            {
                // 早期追踪，然后锁定投入。突进在整个前摇保持速度，因此距离不变；
                // 锁定后沿既定面向运动而非每帧重新制导，让较晚的侧移仍能避开。
                if (attack.CanTrack(context.Attacks.Phase, context.Attacks.ElapsedSeconds))
                {
                    context.Motor.FaceDirection(ResolveAimDirection(context), deltaTime);
                }

                if (attack.LungeSpeed > 0f)
                {
                    context.Motor.SetPlanarVelocity(context.Motor.Facing * attack.LungeSpeed);
                }
            }
            else if (attack != null && attack.LungeSpeed > 0f && context.Motor.Speed > context.Motor.CurrentTopSpeed)
            {
                // 前摇结束后猛烈制动，而非按温和行走减速在自身攻击期间滑行数米。
                context.Motor.Brake(context.Config.LungeBrakeDeceleration, deltaTime);
            }
            else
            {
                context.Motor.Decelerate(deltaTime);
            }

            context.Motor.Tick(deltaTime);
            context.Attacks.Tick(deltaTime);

            // 在更新后接续，使刚结束的挥击直接进入后续段，中间不显示一帧恢复姿势。
            TryContinueCombo(context);
        }

        /// <inheritdoc />
        protected override void OnExit(BossContext context)
        {
            if (context.Attacks.IsRunning)
            {
                context.Attacks.Cancel();
            }

            context.AttackCooldownRemaining = AttackCooldownFor(
                context.CurrentPhase.AttackCooldownSeconds,
                context.Tuning.Get(BossTuningParameter.AttackDelay),
                _isFeinting);

            _isFeinting = false;
            _comboRemaining = 0;
            context.PendingAttack = null;
        }

        /// <summary>
        /// 计算 Boss 再次挥击前等待的时长。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 佯攻代价低于实际出招，因此形成威胁而非白送机会。但仍有代价，持续佯攻的 Boss 就无法真正施压。
        /// </para>
        /// <para>
        /// 学习后的延迟使 <c>CounterPostDodgeAggression</c> 真正有效。
        /// 面对闪避结束后立即反射性出招的玩家，Boss 会等待，让那次攻击挥空，
        /// 正对应“现在它会等待你的破绽”的提示。延迟采用相加而非倍率，使它表现为原阶段节奏上的耐心，
        /// 而非完全不同的节奏。
        /// </para>
        /// <para>
        /// 采用纯静态函数，使提示背后的行为由测试验证，而非观察 Boss 后主观判断。
        /// </para>
        /// </remarks>
        /// <param name="baseCooldownSeconds">当前阶段攻击之间的冷却。</param>
        /// <param name="learnedDelaySeconds">Boss 学会的额外等待秒数。</param>
        /// <param name="isFeint">正在恢复的挥击是否为佯攻。</param>
        /// <returns>Boss 再次攻击前的秒数。</returns>
        public static float AttackCooldownFor(
            float baseCooldownSeconds, float learnedDelaySeconds, bool isFeint)
        {
            float cooldown = isFeint ? baseCooldownSeconds * FeintCooldownFraction : baseCooldownSeconds;

            return cooldown + Mathf.Max(0f, learnedDelaySeconds);
        }

        /// <summary>
        /// 决定高投入攻击连接多少额外命中。
        /// </summary>
        /// <remarks>
        /// 开场阶段只使用单次易读攻击。每个后续阶段将额外段上限增加一，每段再次独立抽取概率，
        /// 因此第二阶段两段常见，三段爆发则是少见危险升级而非常态。
        /// 使用与所有 Boss 决策相同的种子随机流，使回放精确且可由编辑模式测试固定验证。
        /// 静态且不依赖上下文，避免测试需组装完整 Boss。
        /// </remarks>
        /// <param name="phaseIndex">从零开始的阶段，同时也是额外命中的上限。</param>
        /// <param name="random">可设种子的随机源，使连招可复现。</param>
        /// <param name="continueChance">
        /// 继续每一后续段的概率。作为参数传入而非固定，使学会连招施压的 Boss 确实更积极连接攻击，
        /// 正是 <see cref="BossTuningParameter.ComboExtensionChance"/>要改变的行为。
        /// </param>
        /// <returns>要连接的额外命中数，范围零到 <paramref name="phaseIndex"/>。</returns>
        public static int ComboLength(int phaseIndex, IRandomProvider random, float continueChance)
        {
            int maxExtra = Mathf.Max(0, phaseIndex);
            float chance = Mathf.Clamp01(continueChance);
            int extra = 0;

            while (extra < maxExtra && random.NextBool(chance))
            {
                extra++;
            }

            return extra;
        }

        /// <summary>
        /// 若玩家仍处于威胁范围，将已结束挥击连接到排队后续段。
        /// </summary>
        /// <remarks>
        /// 后续攻击从当前阶段招式集选择，因此连招不能使用原本不允许的攻击。目标离开感知范围立即放弃，
        /// 因为对空气持续挥击像损坏而非不懈追击。全部判断仍受感知延迟保护：Boss 根据旧信息连接攻击，
        /// 绝不读取玩家实时位置。
        /// </remarks>
        private void TryContinueCombo(BossContext context)
        {
            if (context.Attacks.IsRunning || _comboRemaining <= 0)
            {
                return;
            }

            AttackDefinition next = PickFollowUp(context);
            bool inReach = next != null && context.HasPerceivedPlayer &&
                           context.DistanceToPlayer <= next.EffectiveReach;

            if (!inReach)
            {
                _comboRemaining = 0;
                return;
            }

            _comboRemaining--;

            // 后续段按定义属于投入，因为 Boss 已把自己串入连招，因此挥空的连招和突进一样可能使其失衡。
            context.LastAttackWasCommitted = true;
            context.Attacks.Begin(next, context.DistanceToPlayer);
        }

        /// <summary>从当前阶段招式集中随机选择后续攻击。</summary>
        private static AttackDefinition PickFollowUp(BossContext context)
        {
            AttackDefinition[] moveset = context.CurrentPhase.Attacks;

            if (moveset == null || moveset.Length == 0)
            {
                return null;
            }

            return moveset[context.Random.NextInt(0, moveset.Length)];
        }

        /// <summary>
        /// 前摇中途放弃佯攻挥击。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 这里弹反系统终于与学习系统相连。Boss只有学到玩家按固定节奏应对前摇，
        /// 如可预测弹反或闪避时才佯攻；应对佯攻正是停止该习惯。从未形成这种习惯的玩家不会看见佯攻。
        /// </para>
        /// <para>
        /// 这也最清楚地说明感知延迟的意义：Boss在不可能知道玩家下一步动作前就决定佯攻，
        /// 因此与人类一样进行猜测。
        /// </para>
        /// </remarks>
        private bool TryCancelFeint(BossContext context, AttackDefinition attack)
        {
            if (!_isFeinting || attack == null || context.Attacks.Phase != AttackPhase.Startup)
            {
                return false;
            }

            if (context.Attacks.ElapsedSeconds < attack.StartupSeconds * FeintCancelFraction)
            {
                return false;
            }

            context.Attacks.Cancel();
            context.Motor.Decelerate(context.Time.DeltaTime);
            context.Motor.Tick(context.Time.DeltaTime);

            return true;
        }

        /// <summary>
        /// 瞄准玩家之前的位置，并按其习惯预测的去向调整。
        /// </summary>
        /// <remarks>
        /// 外推使用感知到的速度，而该速度本身已滞后。因此 Boss 从旧信息预测，
        /// 正如人类对手计算射击提前量。
        /// </remarks>
        private static Vector3 ResolveAimDirection(BossContext context)
        {
            PlayerObservation observation = context.Perceived;

            if (!observation.IsValid)
            {
                return context.Motor.Facing;
            }

            float predictionStrength =
                Mathf.Clamp01(context.Tuning.Get(BossTuningParameter.DodgePredictionStrength));

            Vector3 aimPoint = observation.Position +
                               observation.Velocity * (MaxLeadSeconds * predictionStrength);

            Vector3 direction = aimPoint - context.Transform.position;
            direction.y = 0f;

            return direction.sqrMagnitude > Mathf.Epsilon ? direction.normalized : context.Motor.Facing;
        }
    }

    /// <summary>
    /// 攻击后的暂停，期间 Boss 暴露给玩家惩罚。
    /// </summary>
    /// <remarks>
    /// 该窗口是玩家成功闪避的全部奖励，因此是实际状态而非标志。时长来自阶段定义，使后期阶段更有压迫感，
    /// 而无需改变攻击本身。
    /// </remarks>
    public sealed class BossRecoverState : StateBase<BossContext>
    {
        /// <summary>恢复窗口和失衡踉跄均结束时为 true。</summary>
        /// <param name="context">Boss 上下文。</param>
        /// <returns>Boss 是否可以再次行动。</returns>
        /// <remarks>
        /// 失衡 Boss 在这里停留到踉跄结束，使诱骗挥空形成真实可见空当，而非 Boss 立即继续行动。
        /// </remarks>
        public bool IsComplete(BossContext context) =>
            context.AttackCooldownRemaining <= 0f && !context.IsOverbalanced;

        /// <inheritdoc />
        protected override void OnTick(BossContext context, float deltaTime)
        {
            // 仍会转向，避免免费暴露背后，但不能移动或行动。恢复必须是真正空当，否则闪避不再值得尝试。
            context.Motor.FaceDirection(context.DirectionToPlayer, deltaTime * 0.5f);
            context.Motor.Decelerate(deltaTime);
            context.Motor.Tick(deltaTime);
        }
    }

    /// <summary>
    /// 以招架应对传入重击。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 设计要求对偏好重击玩家采用的反制。Boss只有感知到重击前摇且通过学习后的招架概率抽取才进入，
    /// 因此逐渐出现而非突然开启。
    /// </para>
    /// <para>
    /// 这是一种投入，不是盾牌。Boss 停止行动，判断错误就给玩家免费空当。这种对称使适应公平：
    /// 玩家可诱使 Boss 招架空气。
    /// </para>
    /// </remarks>
    public sealed class BossParryState : StateBase<BossContext>
    {
        /// <summary>招架尝试已成功或失败地结束时为 true。</summary>
        /// <remarks>
        /// 站姿持续窗口加尾段。以前整个半秒都拒绝伤害，只需等待而无需克服，
        /// 因为没有任何时刻能体现选择站姿的错误。
        /// </remarks>
        /// <param name="context">Boss 上下文。</param>
        /// <returns>Boss 是否可以再次行动。</returns>
        public bool IsComplete(BossContext context) =>
            TimeInState >= context.Config.ParryWindowSeconds + context.Config.ParryTailSeconds;

        /// <inheritdoc />
        protected override void OnEnter(BossContext context)
        {
            context.IsParrying = true;
            context.ParryWindowOpen = true;

            // 窗口打开瞬间提示，且只在此时提示。玩家必须看清要克服的内容，
            // 否则不可读的招架与 Boss 随机拒绝伤害无法区分。
            context.PublishCombatEvent(CombatEventKind.GuardRaised);
            context.Motor.Halt();
        }

        /// <inheritdoc />
        protected override void OnTick(BossContext context, float deltaTime)
        {
            context.ParryWindowOpen = TimeInState <= context.Config.ParryWindowSeconds;

            context.Motor.FaceDirection(context.DirectionToPlayer, deltaTime);
            context.Motor.Tick(deltaTime);
        }

        /// <inheritdoc />
        protected override void OnExit(BossContext context)
        {
            context.IsParrying = false;
            context.ParryWindowOpen = false;

            // 使用独立计时器而非攻击冷却。成功招架清除攻击冷却，提供应得惩罚机会；
            // 此前共用计时器也重新准备下一次招架，使持续猜对的 Boss 无法被攻击。
            // 旧的固定 0.4 秒限制只是掩盖了这个问题。
            context.ParryCooldownRemaining =
                Mathf.Max(context.ParryCooldownRemaining, context.Config.ParryCooldownSeconds);
        }
    }

    /// <summary>因架势击破而踉跄。</summary>
    public sealed class BossStaggerState : StateBase<BossContext>
    {
        private float _remainingSeconds;

        private StaggerReason _reason;

        /// <summary>中断结束时为 true。</summary>
        /// <param name="context">Boss 上下文。</param>
        /// <returns>Boss 是否可以再次行动。</returns>
        public bool IsComplete(BossContext context) => _remainingSeconds <= 0f;

        /// <inheritdoc />
        protected override void OnEnter(BossContext context)
        {
            _remainingSeconds = context.RequestedStaggerSeconds;
            _reason = context.RequestedStaggerReason;
            context.StaggerRequested = false;
            context.RequestedStaggerReason = StaggerReason.Hit;

            context.Motor.Halt();

            // 只有防线真正崩溃才提示。PoiseBroken触发最强烈特效和音效，若招架反震也触发，
            // 会错误告知玩家赢得架势击破；招架已在接触时拥有更轻的独立反馈。
            if (_reason == StaggerReason.PoiseBreak)
            {
                context.PublishCombatEvent(CombatEventKind.PoiseBroken);
            }
        }

        /// <inheritdoc />
        protected override void OnTick(BossContext context, float deltaTime)
        {
            if (context.StaggerRequested)
            {
                _remainingSeconds = Mathf.Max(_remainingSeconds, context.RequestedStaggerSeconds);
                context.StaggerRequested = false;

                // 同帧到来的架势击破不能降级为招架反震。之后清除原因也同样重要，
                // 否则下一次普通硬直会继承旧原因并发出错误提示。
                if (context.RequestedStaggerReason > _reason)
                {
                    _reason = context.RequestedStaggerReason;

                    if (_reason == StaggerReason.PoiseBreak)
                    {
                        context.PublishCombatEvent(CombatEventKind.PoiseBroken);
                    }
                }

                context.RequestedStaggerReason = StaggerReason.Hit;
            }

            _remainingSeconds -= deltaTime;

            context.Motor.Decelerate(deltaTime);
            context.Motor.Tick(deltaTime);
        }

        /// <inheritdoc />
        protected override void OnExit(BossContext context)
        {
            // 若硬直结束没有冷却，Boss 会立即报复，抹去玩家赢得的惩罚窗口。
            context.AttackCooldownRemaining = Mathf.Max(context.AttackCooldownRemaining, 0.5f);
        }
    }

    /// <summary>被击败。没有离开转移的终止状态。</summary>
    public sealed class BossDeadState : StateBase<BossContext>
    {
        /// <inheritdoc />
        protected override void OnEnter(BossContext context)
        {
            context.Motor.Halt();

            if (context.Attacks.IsRunning)
            {
                context.Attacks.Cancel();
            }

            context.PublishCombatEvent(CombatEventKind.Defeated);
        }

        /// <inheritdoc />
        protected override void OnTick(BossContext context, float deltaTime) =>
            context.Motor.Tick(deltaTime);
    }
}
