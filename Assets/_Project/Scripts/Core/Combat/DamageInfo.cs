using UnityEngine;

namespace AdaptiveBossArena.Core.Combat
{
    /// <summary>
    /// 单次攻击的不可变描述，由攻击者生成并由目标消费。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 声明为 <c>readonly struct</c>，各处均通过<c>in</c> 传递，使密集连击的命中处理也不产生分配。
    /// 只读声明还可避免读取属性时，<c>in</c> 参数产生防御性复制。
    /// </para>
    /// <para>
    /// 接收者可能需要的全部字段都包含在此值中。伤害链路不会回头访问攻击者组件，
    /// 因此玩家和 Boss即使位于互不引用的程序集，也能互相造成伤害。
    /// </para>
    /// </remarks>
    public readonly struct DamageInfo
    {
        /// <summary>应用目标侧修正前要扣除的原始生命值。</summary>
        public float Amount { get; init; }

        /// <summary>攻击类别，用于抗性处理和行为分析。</summary>
        public DamageType Type { get; init; }

        /// <summary>攻击来源阵营。对同阵营的攻击会被丢弃。</summary>
        public CombatantTeam SourceTeam { get; init; }

        /// <summary>
        /// 攻击对象的实例标识，用于归因和排除自身命中。
        /// </summary>
        /// <remarks>
        /// 刻意使用 <c>int</c> 而非组件引用：标识不会引入程序集依赖，
        /// 也无法用于访问攻击者并读取其状态。
        /// </remarks>
        public int SourceInstanceId { get; init; }

        /// <summary>命中发生的世界坐标，用于放置命中特效。</summary>
        public Vector3 HitPoint { get; init; }

        /// <summary>
        /// 攻击从攻击者指向目标的归一化方向。
        /// </summary>
        public Vector3 HitDirection { get; init; }

        /// <summary>命中时施加给目标的冲量，单位为世界单位每秒。</summary>
        public float KnockbackSpeed { get; init; }

        /// <summary>造成的架势伤害。累积足够伤害后使目标陷入硬直。</summary>
        public float PoiseDamage { get; init; }

        /// <summary>此次攻击中断目标当前动作的强度。</summary>
        public StaggerStrength Stagger { get; init; }

        /// <summary>受击者身体的去向：击退、倒地或击飞。</summary>
        /// <remarks>受击者反应门控可以降级的请求，参见 <see cref="ImpactReaction"/>。</remarks>
        public ImpactReaction Reaction { get; init; }

        /// <summary>
        /// 成功命中时冻结双方的命中停顿秒数。
        /// </summary>
        /// <remarks>
        /// 本项目影响打击手感最显著的单一参数。随每次攻击传递，而非使用全局值，
        /// 使重击明显比轻击更有分量。
        /// </remarks>
        public float HitStopSeconds { get; init; }

        /// <summary>为 true 时，攻击即使遇到闪避无敌帧也会命中。</summary>
        /// <remarks>仅供无法回避的竞技场危险区域使用；Boss 攻击绝不能设置此项。</remarks>
        public bool IgnoresInvulnerability { get; init; }

        /// <summary>为 true 时，举起的防御不能挡住攻击，必须通过闪避避开。</summary>
        /// <remarks>
        /// 此类游戏中的“危险攻击”。它同时忽略格挡和弹反窗口，因此防御中的玩家会受到完整伤害；
        /// 闪避仍能避开，因为 <see cref="IgnoresInvulnerability"/>保持 false。这种不对称正是判断重点：
        /// 不能用防御应对这种攻击。
        /// </remarks>
        public bool Unblockable { get; init; }

        /// <summary>为 true 时，精准弹反也不能拒绝此次攻击。</summary>
        /// <remarks>
        /// 与 <see cref="Unblockable"/> 施加的压力相反。前者移除防御者的应对手段；
        /// 此项保护攻击者已赢得的惩罚机会——向被击破的防线反击——使其不会被刚刚崩溃的防御轻易化解。
        /// 能够格挡的目标仍可按正常代价格挡，因此它也不是无条件命中。
        /// </remarks>
        public bool Unparryable { get; init; }

        /// <summary>创建攻击描述，未提供的字段使用合理默认值。</summary>
        /// <param name="amount">原始伤害。</param>
        /// <param name="type">攻击类别。</param>
        /// <param name="sourceTeam">攻击者阵营。</param>
        /// <param name="sourceInstanceId">攻击者实例标识。</param>
        /// <returns>已填充的攻击描述。</returns>
        public static DamageInfo Create(
            float amount,
            DamageType type,
            CombatantTeam sourceTeam,
            int sourceInstanceId) => new DamageInfo
            {
                Amount = amount,
                Type = type,
                SourceTeam = sourceTeam,
                SourceInstanceId = sourceInstanceId,
                HitDirection = Vector3.forward,
                Stagger = StaggerStrength.Flinch
            };

        /// <summary>返回此攻击位于指定接触点的副本。</summary>
        /// <param name="hitPoint">世界空间接触位置。</param>
        /// <param name="hitDirection">从攻击者指向目标的运动方向。</param>
        /// <returns>携带所提供接触数据的副本。</returns>
        public DamageInfo AtPoint(Vector3 hitPoint, Vector3 hitDirection) =>
            Rebuild(this, Amount, hitPoint, hitDirection);

        /// <summary>返回此攻击使用不同伤害量的副本。</summary>
        /// <remarks>用于会缩放伤害的受击区域，例如 Boss 弱点。</remarks>
        /// <param name="amount">替换后的伤害量。</param>
        /// <returns>携带所提供伤害量的副本。</returns>
        public DamageInfo WithAmount(float amount) => Rebuild(this, amount, HitPoint, HitDirection);

        /// <summary>
        /// 复制所有字段，仅替换调用者允许修改的三个字段。
        /// </summary>
        /// <remarks>
        /// 仅初始化属性构造后不能重新赋值，且 C# 9 不支持结构体的 <c>with</c> 表达式，
        /// 因此必须完整写出副本。让复制统一经过一个方法，可避免新增字段被部分复制路径遗漏。
        /// </remarks>
        private static DamageInfo Rebuild(
            in DamageInfo source,
            float amount,
            Vector3 hitPoint,
            Vector3 hitDirection) => new DamageInfo
            {
                Amount = amount,
                Type = source.Type,
                SourceTeam = source.SourceTeam,
                SourceInstanceId = source.SourceInstanceId,
                HitPoint = hitPoint,
                HitDirection = hitDirection,
                KnockbackSpeed = source.KnockbackSpeed,
                PoiseDamage = source.PoiseDamage,
                Stagger = source.Stagger,
                Reaction = source.Reaction,
                HitStopSeconds = source.HitStopSeconds,
                IgnoresInvulnerability = source.IgnoresInvulnerability,
                Unblockable = source.Unblockable,
                Unparryable = source.Unparryable
            };
    }

    /// <summary>向目标提交 <see cref="DamageInfo"/> 后的结果。</summary>
    public readonly struct DamageResult
    {
        /// <summary>目标对攻击的处理结果。</summary>
        public DamageOutcome Outcome { get; init; }

        /// <summary>经过抗性和范围限制后实际扣除的生命值。</summary>
        public float AmountApplied { get; init; }

        /// <summary>此次攻击使目标生命值降为零时为 true。</summary>
        public bool WasLethal { get; init; }

        /// <summary>目标确实损失生命值时为 true，区别于避开或吸收攻击。</summary>
        public bool Connected => Outcome == DamageOutcome.Applied;

        /// <summary>扣除了生命值的攻击结果。</summary>
        /// <param name="amountApplied">扣除的生命值。</param>
        /// <param name="wasLethal">目标是否死亡。</param>
        /// <returns>已填充的结果。</returns>
        public static DamageResult Applied(float amountApplied, bool wasLethal) => new DamageResult
        {
            Outcome = DamageOutcome.Applied,
            AmountApplied = amountApplied,
            WasLethal = wasLethal
        };

        /// <summary>攻击被避开或拒绝且未扣除生命值时的结果。</summary>
        /// <param name="outcome">未造成伤害的结果类型。</param>
        /// <returns>已填充的结果。</returns>
        public static DamageResult NoDamage(DamageOutcome outcome) => new DamageResult
        {
            Outcome = outcome
        };
    }
}
