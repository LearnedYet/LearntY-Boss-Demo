namespace AdaptiveBossArena.Combat
{
    /// <summary>
    /// 让武器防御风格改变交锋结果的规则。
    /// </summary>
    /// <remarks>
    /// 从玩家中拆出为纯函数，因为这些规则正是三把武器防御手感不同的全部原因——决定挥击是否被打断的规
    /// 则，应由编辑模式测试固定验证，而不是在试玩时发现。
    /// 武器是否能够弹反由<see cref="WeaponDefinition.CanDeflect"/> 决定；具体情境下的判断位于这里。
    /// </remarks>
    public static class DefenceResolver
    {
        /// <summary>持续防御需要支付的普通延迟格挡姿态消耗比例。</summary>
        /// <remarks>
        /// 低于一，确保防御确实可以<em>持续维持</em>以抵挡连击，
        /// 而不是和为招架而非格挡设计的武器几乎一样快地被打破。
        /// </remarks>
        private const float SustainedGuardBlockFraction = 0.6f;

        /// <summary>
        /// 已投入的挥击是否在受到来袭命中后继续，而不是进入硬直。
        /// </summary>
        /// <remarks>
        /// 霸体保护前摇和生效帧——玩家已经投入、无法收回的部分——但绝不保护后摇，让挥空重击仍可被惩罚。
        /// 伤害不受影响，只有打断被拒绝。这种不对称让霸体成为真正的选择，而不是严格更好的选项：
        /// 用反应能力换取挥击命中的确定性。
        /// </remarks>
        /// <param name="defence">装备武器的防御风格。</param>
        /// <param name="attackPhase">进行中挥击的阶段。</param>
        /// <returns>应拒绝硬直时为 true。</returns>
        public static bool ResistsStagger(DefenceStyle defence, AttackPhase attackPhase) =>
            defence == DefenceStyle.HyperArmour &&
            (attackPhase == AttackPhase.Startup || attackPhase == AttackPhase.Active);

        /// <summary>
        /// 延迟格挡的姿态消耗倍率，让持续防御更慢地损耗姿态。
        /// </summary>
        /// <param name="defence">装备武器的防御风格。</param>
        /// <returns>应用于格挡姿态消耗的倍率。</returns>
        public static float BlockPostureMultiplier(DefenceStyle defence) =>
            defence == DefenceStyle.SustainedGuard ? SustainedGuardBlockFraction : 1f;

        /// <summary>
        /// 判断防御如何应对来袭命中。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 双方共用一个函数，因为招架是同一种机制在两个方向上的应用，双方不能逐渐偏离。
        /// 它们的区别是能力，而不是逻辑：玩家可以退而格挡，首领不能，
        /// 这通过 <see cref="DefenceQuery.CanBlock"/> 表达，而不是在别处复制一份规则。
        /// </para>
        /// <para>
        /// 它替代了分散的判断。过去，防御资格在一处判断，时机在另一处评估，
        /// 因此“此命中是否被防御”编码了两次，且可能自相矛盾。
        /// </para>
        /// </remarks>
        /// <param name="query">防御与来袭命中的全部已知信息。</param>
        /// <returns>命中与防御交锋后的处理结果。</returns>
        public static DefenceOutcome ResolveDefence(in DefenceQuery query)
        {
            if (!query.IsDefending)
            {
                return DefenceOutcome.None;
            }

            // 危险攻击同时无视防御和招架。这就是将攻击标记为无法格挡的目的：
            // 让防守从安全的默认选项变成错误答案，要求玩家判断格挡还是闪避，而不只是把握弹反时机。
            if (query.Unblockable)
            {
                return DefenceOutcome.None;
            }

            // Unparryable 只禁用弹反，然后继续后续判断。防守者已经赢得命中权利的攻击——对破防目标发动
            // 的反击——不能简单地被拒绝，但也不必成为无条件命中：面对能格挡的防守者，它会退为正常格挡，
            // 并支付格挡的普通代价。
            bool deflectable = query.CanDeflect && !query.Unparryable;

            if (deflectable && query.TimeInDefenceSeconds <= query.DeflectWindowSeconds)
            {
                return DefenceOutcome.Deflected;
            }

            return query.CanBlock ? DefenceOutcome.Blocked : DefenceOutcome.None;
        }
    }

    /// <summary>防御应对来袭命中的结果。</summary>
    public enum DefenceOutcome
    {
        /// <summary>命中未被防御，按普通方式处理。</summary>
        None = 0,

        /// <summary>被精准计时的防御化解，防守者不付出代价。</summary>
        Deflected = 1,

        /// <summary>延迟吸收，付出削血伤害和姿态的代价。</summary>
        Blocked = 2
    }

    /// <summary>
    /// <see cref="DefenceResolver.ResolveDefence"/> 判断一次交锋所需的全部信息。
    /// </summary>
    /// <remarks>
    /// 采用结构体而不是七个参数，因为调用点位于伤害路径中，
    /// 相邻参数被悄然调换会产生仍能编译的公平性缺陷。命名初始化器让调用明确表达含义。
    /// </remarks>
    public readonly struct DefenceQuery
    {
        /// <summary>防守者是否举起防御或已投入招架姿态。</summary>
        public bool IsDefending { get; init; }

        /// <summary>此防御是否具备计时弹反的能力。</summary>
        public bool CanDeflect { get; init; }

        /// <summary>
        /// 防御能否吸收未把握正确时机的命中。
        /// </summary>
        /// <remarks>
        /// 首领为 false，因为它投入招架姿态而不是持续举盾：能格挡削血的首领会严格更难被惩罚，
        /// 而招架落空后的可惩罚尾段正是值得诱导它使用该姿态的全部原因。
        /// </remarks>
        public bool CanBlock { get; init; }

        /// <summary>命中到达时，防御已经维持的时间。</summary>
        public float TimeInDefenceSeconds { get; init; }

        /// <summary>投入防御后，多长时间内的命中会被弹反而非吸收。</summary>
        public float DeflectWindowSeconds { get; init; }

        /// <summary>来袭攻击是否完全无视防御。</summary>
        public bool Unblockable { get; init; }

        /// <summary>来袭攻击是否无法被弹反。</summary>
        public bool Unparryable { get; init; }
    }
}
