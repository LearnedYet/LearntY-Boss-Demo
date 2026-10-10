namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 生成的 Animator 控制器与桥接组件共用的状态和参数名称。
    /// </summary>
    /// <remarks>
    /// 只定义一份，因为控制器由编辑器构建、由运行时驱动，任何一方的名称拼写不同都会静默失败：
    /// 向不存在状态进行交叉淡化不会记录日志，并会让角色冻结在最后姿势。
    /// </remarks>
    public static class CharacterAnimatorParameters
    {
        /// <summary>零到一的水平速度，驱动移动混合。</summary>
        public const string Speed = "Speed";

        /// <summary>按速度缩放的朝向横向移动，右侧为正，驱动侧移。</summary>
        public const string MoveX = "MoveX";

        /// <summary>按速度缩放的朝向纵向移动，前方为正，驱动行走、奔跑和后退。</summary>
        public const string MoveZ = "MoveZ";

        /// <summary>正在播放的攻击状态的归一化片段时间，由攻击时间轴设置。</summary>
        public const string AttackTime = "AttackTime";

        /// <summary>
        /// 从地面起身的归一化进度，用于定位起身片段。
        /// </summary>
        /// <remarks>
        /// 躺下时保持零，即片段首帧：同一个片段同时承担倒地身体和起身过程，因此两者的姿势不会互相矛盾。
        /// </remarks>
        public const string ReactionTime = "ReactionTime";

        /// <summary>按速度混合的空闲、行走、慢跑和冲刺。</summary>
        public const string LocomotionState = "Locomotion";

        /// <summary>闪避翻滚。</summary>
        public const string RollState = "Roll";

        /// <summary>举起防御。</summary>
        public const string GuardState = "Guard";

        /// <summary>被打破的姿态。</summary>
        public const string StaggerState = "Stagger";

        /// <summary>招架姿态：向前撞击以化解打击。</summary>
        public const string ParryState = "Parry";

        /// <summary>阶段转换播放的咆哮，按变身进度定位。</summary>
        public const string RoarState = "Roar";

        /// <summary>
        /// 各攻击状态相同副本的后缀，重复挥击时会混合到该副本。
        /// </summary>
        /// <remarks>
        /// Unity 无法让状态向自身交叉淡化：若要求如此，它会直接重新开始而不混合，
        /// 身体便从一次挥击末尾跳到下次开头。每个攻击状态保留两个副本，
        /// 让重复片段的连击能够从一个混合到另一个。
        /// </remarks>
        public const string AlternateSuffix = " B";

        /// <summary>被抛到空中。</summary>
        public const string AirborneState = "Airborne";

        /// <summary>倒在地面并从地面起身。</summary>
        public const string KnockedDownState = "KnockedDown";

        /// <summary>
        /// 成功命中时播放的层，遮罩限制到上半身。
        /// </summary>
        /// <remarks>
        /// 使用遮罩让躯干与手臂承受打击，双腿则保持原有姿态或步伐：没有破坏站立能力的命中，
        /// 也不应重置脚步。
        /// </remarks>
        public const string HitLayer = "Hits";

        /// <summary>在受击层播放的轻微受击动作。</summary>
        public const string HitState = "Hit";

        /// <summary>受击层的静止状态，不播放任何内容。</summary>
        public const string HitRestState = "Rest";

        /// <summary>战败。</summary>
        public const string DeathState = "Death";

        /// <summary>没有绑定指定名称时，轻挥击使用的攻击状态。</summary>
        public const string DefaultLightState = "Light1";

        /// <summary>没有绑定指定名称时，重挥击使用的攻击状态。</summary>
        public const string DefaultHeavyState = "Heavy";

        /// <summary>没有绑定指定名称时，特殊技能使用的攻击状态。</summary>
        public const string DefaultSpecialState = "Special";
    }
}
