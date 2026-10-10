using System;
using UnityEngine;

namespace AdaptiveBossArena.Core.Combat
{
    /// <summary>战斗中可供学习系统分析的一个时刻。</summary>
    /// <remarks>
    /// 这里每个值都对应旁观者可见的事件：出招、挥空、闪避、开始治疗。没有任何值描述意图或内部状态。
    /// </remarks>
    public enum CombatEventKind
    {
        /// <summary>战斗参与者开始攻击。表现为前摇。</summary>
        AttackStarted = 0,

        /// <summary>攻击命中并造成伤害。</summary>
        AttackLanded = 1,

        /// <summary>攻击结束，未触及任何目标。</summary>
        AttackWhiffed = 2,

        /// <summary>攻击被无敌目标避开。</summary>
        AttackEvaded = 3,

        /// <summary>执行了一次闪避，其方向可见。</summary>
        DodgePerformed = 4,

        /// <summary>闪避时机足够精准，完全避开了攻击。</summary>
        PerfectDodge = 5,

        /// <summary>战斗参与者的架势被击破。</summary>
        PoiseBroken = 6,

        /// <summary>开始治疗。刻意使其显眼，以便被惩罚。</summary>
        HealStarted = 7,

        /// <summary>治疗未受打断并已完成。</summary>
        HealCompleted = 8,

        /// <summary>战斗参与者被击败。</summary>
        Defeated = 9,

        /// <summary>举起防御。</summary>
        GuardRaised = 10,

        /// <summary>攻击被精准弹反，攻击者损失架势。</summary>
        Deflected = 11,

        /// <summary>攻击被较晚格挡，防御者损失架势并承受擦伤伤害。</summary>
        Blocked = 12,

        /// <summary>被击破的防御遭到反击惩罚。</summary>
        Riposte = 13,

        /// <summary>
        /// 拔出武器。
        /// </summary>
        /// <remarks>
        /// 属于合理可观察的信息：武器明显握在玩家手中，因此Boss 可以学习玩家偏好哪种武器并作出应对。
        /// </remarks>
        WeaponDrawn = 14,

        /// <summary>
        /// 挥击被精准迎击并拒绝。
        /// </summary>
        /// <remarks>
        /// <b>Actor 是防御者</b>，即进行招架的一方，而非被招架的一方。这个约定很重要，
        /// 因为 <c>CombatMemory</c> 按行为主体路由事件。若在此填写攻击者，
        /// 被招架的玩家会被记为完成弹反，Boss 随后就会适应一个从未发生的统计事实。
        /// </remarks>
        Parried = 15,

        /// <summary>
        /// 战斗参与者猛烈撞墙，足以受到伤害。
        /// </summary>
        /// <remarks>
        /// Actor 是撞墙的一方。此事件仅用于统计：造成的硬直或架势损失由事件发生处决定，
        /// 不能由监听者决定。
        /// </remarks>
        WallImpact = 16,

        /// <summary>
        /// 玩家通过主动招架拨开攻击。
        /// </summary>
        /// <remarks>
        /// 与现有监听者已处理的 <see cref="Deflected"/>一同发布，因此此事件仅用于区分主动招架。
        /// 这种更难、风险更高的行动值得独特音效，并应在以后拥有独立的 Boss 统计位置。只能追加，
        /// 不能插入。
        /// </remarks>
        PlayerParried = 17,

        /// <summary>
        /// 攻击判定窗口已打开：武器正在经过其可命中的空间。
        /// </summary>
        /// <remarks>
        /// 发布此事件，让挥击音效在刀刃移动时而非前摇开始时播放。玩家反馈声音与画面不一致，
        /// 而挥砍前一秒就响起破风声是重要原因。仅用于表现，学习系统不会使用。只能追加，不能插入。
        /// </remarks>
        AttackActive = 18
    }

    /// <summary>
    /// 一次可观察的战斗事件。
    /// </summary>
    /// <remarks>
    /// 通过 <c>in</c> 传递，字段仅采用可直接按内存布局传递的类型，
    /// 因此长时间战斗的事件也可保存在环形缓冲区内而无需分配。
    /// </remarks>
    public readonly struct CombatEvent
    {
        /// <summary>发生的事件。</summary>
        public CombatEventKind Kind { get; init; }

        /// <summary>引发事件的一方。</summary>
        public CombatantTeam Actor { get; init; }

        /// <summary>事件发生时的战斗时钟时间。</summary>
        public float Timestamp { get; init; }

        /// <summary>涉及攻击时，所关联攻击的类别。</summary>
        public DamageType DamageType { get; init; }

        /// <summary>
        /// 开始的攻击为无法格挡的危险攻击时为 true。
        /// </summary>
        /// <remarks>
        /// 随前摇传递，使表现层能够发出“不要格挡”的声音和视觉提示。学习系统忽略它，
        /// 因为它描述 Boss 自己的攻击，而非玩家。
        /// </remarks>
        public bool Unblockable { get; init; }

        /// <summary>具有位置的事件发生地点。</summary>
        public Vector3 Position { get; init; }

        /// <summary>
        /// 与事件关联的方向，例如闪避的运动方向。
        /// </summary>
        public Vector3 Direction { get; init; }

        /// <summary>事件发生时双方的距离。</summary>
        public float SeparationDistance { get; init; }

        /// <summary>涉及伤害的事件造成的伤害量。</summary>
        public float Magnitude { get; init; }
    }

    /// <summary>
    /// 向负责统计的对象广播可观察的战斗事件。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Boss 学习系统通过此接口获知事件，而无需持有玩家引用。接口位于 Core，使 Combat 能发布事件、
    /// Learning 能消费事件，同时两者互不感知。
    /// </para>
    /// <para>
    /// 这里有个容易低估的重要限制：总线会立即投递事件，因此绝不能用于驱动反应。
    /// 它仅为每隔几秒重算的统计提供数据。Boss 对玩家的<em>即时应对</em>必须经过保证感知延迟的
    /// <see cref="AdaptiveBossArena.Core.Perception.IPerceptionSource"/>。
    /// 直接把 Boss 行为接到这里的事件，会重新引入整个设计试图避免的瞬时反应。
    /// </para>
    /// </remarks>
    public interface ICombatEventBus
    {
        /// <summary>每次记录事件时触发。</summary>
        event Action<CombatEvent> EventRecorded;

        /// <summary>记录事件并通知订阅者。</summary>
        /// <param name="combatEvent">要记录的事件。</param>
        void Publish(in CombatEvent combatEvent);

        /// <summary>移除全部订阅者，在战斗结束时使用。</summary>
        void Clear();
    }
}
