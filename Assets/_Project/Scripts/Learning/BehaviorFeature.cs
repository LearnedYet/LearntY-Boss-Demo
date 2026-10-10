namespace AdaptiveBossArena.Learning
{
    /// <summary>
    /// 首领据以构建玩家画像的可量化习惯。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这里的每个数值都来自首领能够合理观察到的事件：它目睹的攻击、闪避，以及能通过观察估量的距离。
    /// 这些数值都不读取玩家输入或内部状态。
    /// </para>
    /// <para>
    /// 每项特征都归一化到零至一，并附带可信度，因此反制策略可同时要求习惯足够明显，
    /// 且已经被观察足够多次，值得采信。
    /// </para>
    /// </remarks>
    public enum BehaviorFeature
    {
        /// <summary>相对于可持续攻击上限的玩家攻击频率。</summary>
        AttackFrequency = 0,

        /// <summary>玩家攻击中重攻击的比例。较高时会引发招架反制。</summary>
        HeavyAttackRatio = 1,

        /// <summary>
        /// 玩家在闪避后立即攻击的概率。
        /// </summary>
        /// <remarks>设计明确指出的习惯：首领通过延迟攻击来应对。</remarks>
        PostDodgeAggression = 2,

        /// <summary>玩家闪避方向的偏斜程度。较高时移动更容易预测。</summary>
        DashDirectionBias = 3,

        /// <summary>
        /// 按竞技场有效范围归一化的偏好交战距离。
        /// </summary>
        /// <remarks>较高时表示玩家倾向保持距离，首领会用突进攻击应对。</remarks>
        PreferredDistance = 4,

        /// <summary>逼近与后退之间的倾向。较高时表示玩家积极施压。</summary>
        Aggression = 5,

        /// <summary>玩家攻击中空挥的比例。较高时表示慌乱出手。</summary>
        WhiffRatio = 6,

        /// <summary>玩家选择治疗而非继续战斗的倾向。</summary>
        HealFrequency = 7,

        /// <summary>玩家在攻击前摇中同一时刻闪避的稳定程度。</summary>
        DodgeTimingConsistency = 8,

        /// <summary>玩家成功躲开首领攻击的比例，体现整体操作水平。</summary>
        EvasionSuccess = 9,

        /// <summary>
        /// 玩家依靠防御而非移动来应对攻击的程度。
        /// </summary>
        /// <remarks>
        /// 龟缩型玩家守住原地，依靠防御抵挡。合理的应对方式并非更高伤害，而是必须闪避的不可格挡攻击；
        /// 这本来就是首领已经拥有的能力，只是使用得更频繁。
        /// </remarks>
        GuardReliance = 10,

        /// <summary>
        /// 玩家用防御接下的攻击中，精准弹反的比例。
        /// </summary>
        /// <remarks>
        /// 用于辨别招架高手，而非只是一直格挡的玩家：<see cref="GuardReliance"/> 表示防御频率，
        /// 这里表示防御水平。这种习惯正适合用佯攻惩罚，因为如此精准的玩家是在辨认节奏。
        /// </remarks>
        DeflectSkill = 11
    }

    /// <summary>
    /// 反制策略可以调整的首领行为。
    /// </summary>
    /// <remarks>
    /// 适应只改变数值，不改变规则。首领不能获得原本没有的能力，只能更充分地利用已有能力。
    /// 这个约束让战斗保持可读：首领在最终阶段的所有行为，都曾以较弱的形式出现在第一阶段。
    /// </remarks>
    public enum BossTuningParameter
    {
        /// <summary>攻击前增加的延迟，用来应对提前行动的玩家。</summary>
        AttackDelay = 0,

        /// <summary>首领试图与玩家保持的距离。</summary>
        PreferredRange = 1,

        /// <summary>以招架应对来袭重攻击的概率。</summary>
        ParryChance = 2,

        /// <summary>选择攻击时施加给突进攻击的权重。</summary>
        GapCloserWeight = 3,

        /// <summary>首领朝玩家偏好闪避方向提前瞄准的强度。</summary>
        DodgePredictionStrength = 4,

        /// <summary>持续进攻而非重新站位的总体意愿。</summary>
        Aggression = 5,

        /// <summary>将单次攻击延伸为更长连段的概率。</summary>
        ComboExtensionChance = 6,

        /// <summary>佯攻概率：展示攻击预警后取消攻击。</summary>
        FeintChance = 7,

        /// <summary>针对善于闪避的玩家，施加给大范围攻击的权重。</summary>
        AreaDenialWeight = 8,

        /// <summary>
        /// 针对躲在防御后的玩家，施加给危险、不可格挡攻击的权重。
        /// </summary>
        /// <remarks>
        /// 合理应对龟缩玩家：使用必须闪避、不能硬接的攻击。这是首领原本已有的能力，只是使用得更频繁；
        /// 所有适应都遵守这条边界：改变数值，绝不增加新规则。
        /// </remarks>
        UnblockableWeight = 9
    }

    /// <summary>特征值与策略阈值的比较方式。</summary>
    public enum FeatureComparison
    {
        /// <summary>特征值达到或超过阈值时，策略可用。</summary>
        AtOrAbove = 0,

        /// <summary>特征值达到或低于阈值时，策略可用。</summary>
        AtOrBelow = 1
    }
}
