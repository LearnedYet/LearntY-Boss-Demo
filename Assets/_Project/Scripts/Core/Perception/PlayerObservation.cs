using UnityEngine;

namespace AdaptiveBossArena.Core.Perception
{
    /// <summary>
    /// 旁观者可见的玩家动作。
    /// </summary>
    /// <remarks>
    /// 每个值都对应明显不同的画面表现。这里不描述意图：没有“即将攻击”状态，因为人类对手也看不到。
    /// 前摇根据 <see cref="PlayerObservation.TimeInActionState"/>推断。
    /// </remarks>
    public enum ObservableActionState
    {
        /// <summary>站立不动。</summary>
        Idle = 0,

        /// <summary>跑动或行走。</summary>
        Moving = 1,

        /// <summary>闪避中。表现明显不同，也是玩家无敌的时间窗口。</summary>
        Dashing = 2,

        /// <summary>执行快速普通攻击。</summary>
        LightAttacking = 3,

        /// <summary>执行缓慢、投入风险较高的攻击。</summary>
        HeavyAttacking = 4,

        /// <summary>使用特殊技能。</summary>
        UsingAbility = 5,

        /// <summary>受击后踉跄。</summary>
        Staggered = 6,

        /// <summary>正在引导治疗。刻意使其明显，以便 Boss 可以合理地惩罚它。</summary>
        Healing = 7,

        /// <summary>已被击败。</summary>
        Dead = 8,

        /// <summary>
        /// 保持防御。表现清晰，属于合理可观察的信息。
        /// </summary>
        /// <remarks>
        /// Boss 能知道防御已举起，却不能知道弹反窗口是否打开。观察站姿是对手可以做到的；
        /// 知道招架精确在哪一帧生效却不是。暴露后者会使 Boss精准控制佯攻，
        /// 而不是像其他对手一样判断和猜测。
        /// </remarks>
        Guarding = 9,

        /// <summary>
        /// 被抛离地面，尚未落地。
        /// </summary>
        /// <remarks>
        /// 只能追加，不能插入：这些值按数值比较和记录。空中身体与站立身体一样明显，因此 Boss 可以知道。
        /// </remarks>
        Airborne = 10,

        /// <summary>
        /// 倒地或正在起身。
        /// </summary>
        /// <remarks>
        /// 可见的是地上的身体。不会暴露是否可受伤：Boss必须像人类一样通过攻击来学习这一点。
        /// </remarks>
        KnockedDown = 11,

        /// <summary>
        /// 主动挥刃拨开攻击。
        /// </summary>
        /// <remarks>
        /// 只能追加，不能插入。招架站姿与举起防御一样明显，Boss看见后可学习诱骗它，
        /// 这正是让招架成为有投入、有惩罚风险动作的目的。与防御一样，当前帧窗口是否真正打开仍然隐藏。
        /// </remarks>
        Parrying = 12
    }

    /// <summary>
    /// Boss 在某个时刻允许知道的全部玩家信息。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 此结构体是防作弊隔离屏障的狭窄窗口。Boss的程序集无法引用玩家程序集，
    /// 因此此快照是玩家状态进入 AI 的唯一通道，严格只包含熟练人类对手通过观察屏幕可感知的信息。
    /// </para>
    /// <para>
    /// 以下信息被省略，原因如下：
    /// </para>
    /// <list type="bullet"><item><description>没有输入向量、缓存输入或按住按钮的状态。
    /// 在意图变成动作前读取它，正是本设计禁止的作弊。</description></item><item><description>没有体力
    /// 值。对手看不到数值，必须从未发生的闪避推断体力耗尽。
    /// </description></item><item><description>没有冷却计时器。
    /// Boss 像人类一样根据可见频率学习玩家技能节奏。</description></item><item><description>没有无敌
    /// 标志。Boss 通过挥空发现闪避避开了攻击，而挥空才是学习系统的输入。</description></item></list>
    /// </remarks>
    public readonly struct PlayerObservation
    {
        /// <summary>捕获此快照时战斗时钟的秒数。</summary>
        public float Timestamp { get; init; }

        /// <summary>玩家世界坐标。</summary>
        public Vector3 Position { get; init; }

        /// <summary>世界空间速度。表现为移动，因此可以合理感知。</summary>
        public Vector3 Velocity { get; init; }

        /// <summary>玩家面向的单位向量。</summary>
        public Vector3 Facing { get; init; }

        /// <summary>玩家正在执行的可明确区分的动作。</summary>
        public ObservableActionState ActionState { get; init; }

        /// <summary>
        /// 玩家进入当前动作状态后的秒数。
        /// </summary>
        /// <remarks>
        /// 这只是旁观者观看动画的时长，因此属于合理信息。Boss 可据此惩罚长重击前摇，
        /// 而无需被告知攻击即将发生。
        /// </remarks>
        public float TimeInActionState { get; init; }

        /// <summary>玩家生命占最大生命的比例，可由生命条看见。</summary>
        public float NormalizedHealth { get; init; }

        /// <summary>此快照包含真实数据而非默认值时为 true。</summary>
        public bool IsValid { get; init; }

        /// <summary>
        /// 当前动作开始的时间，使用与 <see cref="Timestamp"/> 相同的时钟。
        /// </summary>
        /// <remarks>
        /// 跨多帧识别同一次动作。仅凭时间戳不行：它每帧变化，
        /// 以它为键的观察者会把一次挥击视为每秒六十次新事件，将每动作一次的概率判断变成几乎必然。
        /// 它不额外泄露信息，只是快照中两个本已可观察值的差。
        /// </remarks>
        public float ActionStartedAt => Timestamp - TimeInActionState;

        /// <summary>便捷判断玩家是否正在执行任意攻击。</summary>
        public bool IsAttacking =>
            ActionState == ObservableActionState.LightAttacking ||
            ActionState == ObservableActionState.HeavyAttacking ||
            ActionState == ObservableActionState.UsingAbility;

        /// <summary>便捷判断玩家是否当前无法行动。</summary>
        public bool IsIncapacitated =>
            ActionState == ObservableActionState.Staggered ||
            ActionState == ObservableActionState.Airborne ||
            ActionState == ObservableActionState.KnockedDown ||
            ActionState == ObservableActionState.Dead;
    }
}
