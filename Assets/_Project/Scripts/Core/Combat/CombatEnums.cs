namespace AdaptiveBossArena.Core.Combat
{
    /// <summary>战斗参与者所属的一方。</summary>
    /// <remarks>
    /// 命中裁决除物理层外还检查阵营。仅靠物理层过于粗略，额外检查可防止预制体层分配错误时，
    /// Boss 被自身冲击波伤害。
    /// </remarks>
    public enum CombatantTeam
    {
        /// <summary>未分配。不会造成伤害，也不会受到任何伤害。</summary>
        None = 0,

        /// <summary>玩家角色。</summary>
        Player = 1,

        /// <summary>Boss 及其生成的对象。</summary>
        Boss = 2
    }

    /// <summary>传入攻击的分类。</summary>
    /// <remarks>
    /// 学习系统据此区分玩家偏好的战术。经常被 <see cref="Heavy"/>攻击打开防线的 Boss 应开始招架；
    /// 被 <see cref="Light"/>试探逐渐削血时，则应使用控制距离的手段。
    /// </remarks>
    public enum DamageType
    {
        /// <summary>快速普通攻击。投入较小，收益较低。</summary>
        Light = 0,

        /// <summary>缓慢且需要承担出招风险的攻击。生命和架势伤害均较高。</summary>
        Heavy = 1,

        /// <summary>玩家特殊技能造成的伤害。</summary>
        Special = 2,

        /// <summary>Boss 近战接触伤害。</summary>
        BossMelee = 3,

        /// <summary>Boss 远程或范围伤害。</summary>
        BossProjectile = 4,

        /// <summary>环境或竞技场危险区域伤害。</summary>
        Hazard = 5
    }

    /// <summary>向目标提交伤害后的结果。</summary>
    /// <remarks>
    /// 此返回值是学习系统评估战斗的唯一事实来源。尤其是 <see cref="Missed"/>和
    /// <see cref="PerfectDodged"/>，使 Boss 无需读取玩家输入，也能察觉当前套路已被识破。
    /// </remarks>
    public enum DamageOutcome
    {
        /// <summary>伤害已扣除生命值。</summary>
        Applied = 0,

        /// <summary>目标处于无敌状态，通常正在闪避。</summary>
        Invulnerable = 1,

        /// <summary>目标在完美闪避窗口内避开攻击，应获得奖励。</summary>
        PerfectDodged = 2,

        /// <summary>目标通过防御吸收攻击，付出擦伤伤害和架势消耗。</summary>
        Blocked = 3,

        /// <summary>攻击未触及有效目标。</summary>
        Missed = 4,

        /// <summary>目标已经死亡，或因其他原因无法接受伤害。</summary>
        Ignored = 5,

        /// <summary>
        /// 目标精准迎击并完全拒绝此次伤害。
        /// </summary>
        /// <remarks>
        /// 不同于付出代价吸收伤害的 <see cref="Blocked"/>。该区别对攻击者而非防御者重要：
        /// 只有招架结果应使挥击反震。此前，精准弹反和较晚格挡在攻击方看来无法区分，
        /// 因此被招架的挥击仍会继续。
        /// </remarks>
        Deflected = 6,

        /// <summary>
        /// 被主动招架拨开，攻击者付出的代价高于普通弹反。
        /// </summary>
        /// <remarks>只能追加，不能插入：结果会按数值记录和比较。</remarks>
        Parried = 7
    }

    /// <summary>攻击中断受击者当前动作的强度。</summary>
    public enum StaggerStrength
    {
        /// <summary>不中断，受击者继续当前动作。</summary>
        None = 0,

        /// <summary>短暂受击反应，不取消当前动作。</summary>
        Flinch = 1,

        /// <summary>取消当前动作，并附带短暂恢复期。</summary>
        Interrupt = 2,

        /// <summary>彻底击破架势，打开较长的惩罚窗口。</summary>
        Break = 3
    }

    /// <summary>命中除生命伤害和架势伤害外，对受击者身体产生的效果。</summary>
    /// <remarks>
    /// <para>
    /// 与 <see cref="StaggerStrength"/>分开，因为两者回答不同问题。硬直决定动作是否中断；
    /// 此项决定身体最终去向。将玩家砸倒的重击和仅打断挥击的刺拳都能中断动作，
    /// 但只有前者应使角色失去站姿。
    /// </para>
    /// <para>
    /// 这是请求而非最终裁决。受击者的反应门控可能将其降级：空中再次受击飞，或刚起身便再次被击倒，
    /// 均会退化为普通击退，防止连续攻击让角色无限倒地。
    /// </para>
    /// </remarks>
    public enum ImpactReaction
    {
        /// <summary>不改变站姿。攻击若带击退速度，仍会推动角色。</summary>
        None = 0,

        /// <summary>沿攻击方向推动，强度刻意高于普通攻击。</summary>
        Knockback = 1,

        /// <summary>击倒在地，然后起身。倒地期间无法受伤。</summary>
        Knockdown = 2,

        /// <summary>抛向空中，落地后转为倒地状态。</summary>
        Launch = 3
    }
}
