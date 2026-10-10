namespace AdaptiveBossArena.Core.Constants
{
    /// <summary>
    /// 属于结构约束而非可调配置的固定值。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 设计者需要无需重新编译就能调整的值应放进ScriptableObject。这里保存其他代码依赖的常量：
    /// 制定攻击帧数据时使用的物理频率、引擎级上限和缺失配置时的默认值。
    /// </para>
    /// <para>
    /// 此类型用于落实项目的禁用魔法数字规则：否则裸字面量会出现在多个文件，并逐渐产生不一致。
    /// </para>
    /// </remarks>
    public static class GameplayConstants
    {
        /// <summary>项目每秒执行的物理步数。</summary>
        /// <remarks>
        /// 攻击时序以秒保存，但按此频率的帧数构思。修改它会无声地改变全游戏攻击的时间尺度。
        /// </remarks>
        public const int PhysicsStepsPerSecond = 60;

        /// <summary>一个物理步的秒数。</summary>
        public const float FixedTimeStep = 1f / PhysicsStepsPerSecond;

        /// <summary>参考频率下一帧的秒数，用于表达帧数。</summary>
        public const float OneFrame = FixedTimeStep;

        /// <summary>空中角色的向下加速度，单位为米每秒平方。</summary>
        /// <remarks>
        /// 刻意强于真实重力。使用 9.8 时，被击飞的角色悬空太久，看起来像漂浮，与重量感相反；
        /// 此类游戏的厚重感来自快速下落和沉重落地。
        /// </remarks>
        public const float Gravity = 25f;

        /// <summary>落地时保持的向下速度，使控制器与地面稳定接触。</summary>
        /// <remarks>
        /// 替代旧的恒定贴地推力。旧值并非重力，因此无法支持击飞或下落。
        /// </remarks>
        public const float GroundedSpeed = -2f;

        /// <summary>
        /// 输入被记住，并在角色恢复行动后重放的时长。
        /// </summary>
        /// <remarks>
        /// 约九帧。没有输入缓存时，闪避后摇结束前一帧按下的攻击会被丢弃，玩家会准确地感到游戏漏掉输入。
        /// 此值对响应手感的改善大于任何动画工作。
        /// </remarks>
        public const float InputBufferSeconds = 0.15f;

        /// <summary>
        /// Boss 攻击进入有效期后，闪避可被判为完美闪避的窗口。
        /// </summary>
        public const float PerfectDodgeWindowSeconds = 0.12f;

        /// <summary>完美闪避慢动作奖励持续的真实时间。</summary>
        /// <remarks>
        /// 足够长以便察觉和行动，又足够短，避免善于闪避的玩家大部分战斗都处于慢动作。
        /// </remarks>
        public const float PerfectDodgeSlowMotionSeconds = 0.45f;

        /// <summary>单次命中允许请求的命中停顿上限，作为安全限制。</summary>
        public const float MaxHitStopSeconds = 0.2f;

        /// <summary>战斗参与者原点高于竞技场地面的高度。</summary>
        public const float GroundPlaneHeight = 0f;

        /// <summary>
        /// 战斗中 Boss 的阶段数，包括最终绝境阶段 Last Stand。
        /// </summary>
        public const int BossPhaseCount = 4;

        /// <summary>Boss 进入第二阶段的生命比例阈值。</summary>
        public const float BossPhaseTwoThreshold = 0.66f;

        /// <summary>Boss 进入第三阶段的生命比例阈值。</summary>
        public const float BossPhaseThreeThreshold = 0.33f;

        /// <summary>Boss 进入 Last Stand 绝境阶段的生命比例阈值。</summary>
        public const float BossPhaseFourThreshold = 0.15f;

        /// <summary>
        /// 学习系统重算玩家行为画像的间隔秒数。
        /// </summary>
        /// <remarks>
        /// 刻意采用较粗间隔。每帧重算会让 Boss 对单一动作立即反应，而这正是设计要避免的行为。
        /// 按间隔分析迫使 Boss 基于趋势行动。
        /// </remarks>
        public const float BehaviourAnalysisIntervalSeconds = 2.5f;

        /// <summary>两次适应生效之间允许的最短间隔秒数。</summary>
        /// <remarks>
        /// 适应叠加过快会像 Boss 作弊而非学习。玩家需要在下一次变化前有时间察觉当前变化并作出回应。
        /// </remarks>
        public const float MinSecondsBetweenAdaptations = 12f;
    }
}
