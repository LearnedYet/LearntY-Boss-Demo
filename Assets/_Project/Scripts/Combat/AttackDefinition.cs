using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.ScriptableObjects;
using UnityEngine;

namespace AdaptiveBossArena.Combat
{
    /// <summary>用于检测攻击命中对象的几何形状。</summary>
    public enum AttackShape
    {
        /// <summary>以攻击者偏移位置为中心的径向爆发。</summary>
        Sphere = 0,

        /// <summary>矩形体积，适合沿单一轴线进行的突刺和横扫。</summary>
        Box = 1,

        /// <summary>攻击者前方的扇形区域，是俯视角近战挥击的自然形状。</summary>
        Arc = 2
    }

    /// <summary>
    /// 作为资源创作的一次攻击的完整定义。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 时序以三个阶段的秒数表示，而不是动画事件。项目使用基础几何体进行原型开发，
    /// 没有可用于挂接事件的动画片段；将帧数据绑定到片段会导致真正的动画加入后需要重新创作每次攻击。
    /// 因此，<see cref="IAttackTimeline"/> 会触发与动画事件相同的回调，以后切换到动画驱动时序时，
    /// 只需替换驱动器。
    /// </para>
    /// <para>
    /// 这三个阶段构成战斗系统的全部表达方式。前摇是供玩家观察并作出反应的动作承诺，
    /// 生效阶段是唯一能造成伤害的窗口，后摇则是可被惩罚的窗口，让挥空重击成为真正的失误。
    /// 调整三者的比例，就能决定一次攻击是公平还是具有压迫性。
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "AttackDefinition",
        menuName = "Adaptive Boss Arena/Combat/Attack Definition",
        order = 0)]
    public sealed class AttackDefinition : IdentifiableSO
    {
        [Header("Presentation")]
        [SerializeField]
        [Tooltip("Name shown in debug tooling and the boss's tell readout.")]
        private string _displayName = "Unnamed Attack";

        [SerializeField]
        [Tooltip("Optional effect spawned at the attacker when this attack begins. Empty today; drop " +
                 "an ability particle prefab in to give a special its own signature cast, with no " +
                 "other change. Shared by player and boss.")]
        private GameObject _castVfxPrefab;

        [Header("Damage")]
        [SerializeField]
        [Tooltip("Health removed on a clean hit.")]
        private float _damage = 10f;

        [SerializeField]
        [Tooltip("Category, used both for resistances and for behaviour analysis.")]
        private DamageType _damageType = DamageType.Light;

        [SerializeField]
        [Tooltip("Poise damage dealt. Enough accumulated poise damage staggers the target.")]
        private float _poiseDamage = 10f;

        [SerializeField]
        [Tooltip("How forcefully this hit interrupts what the target was doing.")]
        private StaggerStrength _stagger = StaggerStrength.Flinch;

        [SerializeField]
        [Tooltip("Speed imparted to the target away from the impact, in world units per second.")]
        private float _knockbackSpeed = 4f;

        [SerializeField]
        [Tooltip("Where the target's body goes: shoved, floored or thrown. Reserved for big, readable attacks.")]
        private ImpactReaction _impactReaction = ImpactReaction.None;

        [Header("Timing (seconds)")]
        [SerializeField]
        [Range(0.02f, 2f)]
        [Tooltip("Wind-up before the hitbox activates. This is the window the opponent reads and reacts to.")]
        private float _startupSeconds = 0.2f;

        [SerializeField]
        [Range(0.02f, 1f)]
        [Tooltip("How long the hitbox stays live. Longer windows are more forgiving to the attacker.")]
        private float _activeSeconds = 0.1f;

        [SerializeField]
        [Range(0.02f, 2f)]
        [Tooltip("Lockout after the hitbox closes. This is the punish window a whiff opens up.")]
        private float _recoverySeconds = 0.3f;

        [Header("Hitbox")]
        [SerializeField]
        [Tooltip("Volume used to test what this attack strikes.")]
        private AttackShape _shape = AttackShape.Arc;

        [SerializeField]
        [Tooltip("Offset from the attacker's origin, in local space.")]
        private Vector3 _offset = new Vector3(0f, 1f, 1f);

        [SerializeField]
        [Tooltip("Reach of the volume. Interpreted as radius for sphere and arc shapes.")]
        private float _range = 2f;

        [SerializeField]
        [Range(10f, 360f)]
        [Tooltip("Angular width of an arc, in degrees. Unused by other shapes.")]
        private float _arcDegrees = 110f;

        [SerializeField]
        [Tooltip("Half-extents of a box volume. Unused by other shapes.")]
        private Vector3 _boxHalfExtents = new Vector3(0.6f, 0.6f, 1f);

        [Header("Cost and Movement")]
        [SerializeField]
        [Tooltip("Stamina consumed on use. Boss attacks leave this at zero.")]
        private float _staminaCost;

        [SerializeField]
        [Tooltip("Forward impulse applied during startup, used for lunges and gap-closers.")]
        private float _lungeSpeed;

        [Header("Feel")]
        [SerializeField]
        [Range(0f, 0.2f)]
        [Tooltip("Frames of freeze on a successful hit. The strongest single lever on impact weight.")]
        private float _hitStopSeconds = 0.06f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Camera trauma added on a successful hit.")]
        private float _cameraTrauma = 0.2f;

        [Header("Ground Hazard")]
        [SerializeField]
        [Tooltip("When true, this attack scars the ground with a lingering hazard when it lands. " +
                 "Boss attacks only; a hazard field must be present for it to appear.")]
        private bool _leavesHazard;

        [SerializeField]
        [Tooltip("Radius of the hazard left behind.")]
        private float _hazardRadius = 2.5f;

        [SerializeField]
        [Tooltip("Damage the hazard deals each tick. Kept low: the threat is being cornered, not the " +
                 "chip itself.")]
        private float _hazardDamagePerTick = 4f;

        [SerializeField]
        [Tooltip("How long the hazard stays dangerous, in seconds.")]
        private float _hazardDurationSeconds = 3f;

        [Header("Tracking")]
        [SerializeField]
        [Tooltip("Fraction of the wind-up during which the attacker may still turn to follow its target. " +
                 "After it the swing is committed, which is what a late dodge beats.")]
        [Range(0f, 1f)]
        private float _trackingWindowFraction = 0.6f;

        [Header("Defence")]
        [SerializeField]
        [Tooltip("When true, a raised guard does not stop this attack — it must be dodged. A dash " +
                 "still avoids it. The 'perilous' attack; pair it with a distinct telegraph.")]
        private bool _unblockable;

        [SerializeField]
        [Tooltip("When true, this attack cannot be deflected. Reserved for a punish the defender " +
                 "has already earned, such as a riposte thrown into a broken guard.")]
        private bool _unparryable;

        [Header("Telegraph")]
        [SerializeField]
        [Tooltip("Colour of the ground telegraph drawn during startup. Boss attacks only.")]
        private Color _telegraphColor = new Color(1f, 0.35f, 0.2f, 0.55f);

        [SerializeField]
        [Tooltip("Whether a ground telegraph is drawn during this attack's startup.")]
        private bool _showTelegraph = true;

        [Header("Combo")]
        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Point within recovery, as a fraction, at which the next combo input is accepted.")]
        private float _comboWindowStart = 0.1f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Point within recovery, as a fraction, after which the combo is dropped.")]
        private float _comboWindowEnd = 0.85f;

        /// <summary>调试工具中显示的名称。</summary>
        public string DisplayName => _displayName;

        [SerializeField]
        [Tooltip("The parts of the body this attack strikes with. None decides it by the authored volume instead.")]
        private StrikerParts _strikers;

        /// <summary>
        /// 此攻击用于命中的身体部位；若为空，则由创作的攻击体积决定。
        /// </summary>
        /// <remarks>
        /// 这是结构信息，不是调参数据：生成器每次运行都会写入它。无论哪种情况，攻击体积均由资源定义，
        /// 因为首领会根据它规划攻击距离，预警也根据它绘制。
        /// </remarks>
        public StrikerParts Strikers => _strikers;

        /// <summary>攻击开始时在攻击者位置生成的可选效果；也可为 null。</summary>
        public GameObject CastVfxPrefab => _castVfxPrefab;

        /// <summary>此攻击落地时是否在地面留下持续危险区域。</summary>
        public bool LeavesHazard => _leavesHazard;

        /// <summary>所留下危险区域的半径。</summary>
        public float HazardRadius => _hazardRadius;

        /// <summary>危险区域每次结算造成的伤害。</summary>
        public float HazardDamagePerTick => _hazardDamagePerTick;

        /// <summary>危险区域保持危险的时间，单位为秒。</summary>
        public float HazardDurationSeconds => _hazardDurationSeconds;

        /// <summary>直接命中时扣除的生命值。</summary>
        public float Damage => _damage;

        /// <summary>此攻击的类别。</summary>
        public DamageType DamageType => _damageType;

        /// <summary>造成的姿态伤害。</summary>
        public float PoiseDamage => _poiseDamage;

        /// <summary>此命中打断目标的力度。</summary>
        public StaggerStrength Stagger => _stagger;

        /// <summary>施加给目标的击退速度。</summary>
        public float KnockbackSpeed => _knockbackSpeed;

        /// <summary>此命中将目标身体推向的位置。</summary>
        public ImpactReaction ImpactReaction => _impactReaction;

        /// <summary>前摇时长，单位为秒。</summary>
        public float StartupSeconds => _startupSeconds;

        /// <summary>命中盒生效的时长，单位为秒。</summary>
        public float ActiveSeconds => _activeSeconds;

        /// <summary>命中盒关闭后无法行动的时长，单位为秒。</summary>
        public float RecoverySeconds => _recoverySeconds;

        /// <summary>从输入到恢复行动的攻击总时长，单位为秒。</summary>
        public float TotalSeconds => _startupSeconds + _activeSeconds + _recoverySeconds;

        /// <summary>从攻击开始到命中盒开启的时间。</summary>
        public float ActiveStartSeconds => _startupSeconds;

        /// <summary>从攻击开始到命中盒关闭的时间。</summary>
        public float ActiveEndSeconds => _startupSeconds + _activeSeconds;

        /// <summary>用于检测此攻击命中对象的体积。</summary>
        public AttackShape Shape => _shape;

        /// <summary>攻击体积相对于攻击者原点的局部空间偏移。</summary>
        public Vector3 Offset => _offset;

        /// <summary>攻击体积的触及距离。</summary>
        public float Range => _range;

        /// <summary>扇形体积的角宽，单位为度。</summary>
        public float ArcDegrees => _arcDegrees;

        /// <summary>箱形体积的半尺寸。</summary>
        public Vector3 BoxHalfExtents => _boxHalfExtents;

        /// <summary>使用时消耗的耐力。</summary>
        public float StaminaCost => _staminaCost;

        /// <summary>前摇期间施加的向前冲量。</summary>
        public float LungeSpeed => _lungeSpeed;

        /// <summary>
        /// 攻击者距离目标多远开始此攻击，仍然能够命中。
        /// </summary>
        /// <remarks>
        /// 命中范围加上突进在前摇期间经过的距离。过去，这个计算被手写在首领的选择、接近、
        /// 连击和攻击逻辑四处。任何对突进移动方式的调整，都必须找到这四处；
        /// 漏改一处就会导致首领选择无法命中的攻击，或者拒绝本可命中的攻击，而没有线索指出原因。
        /// </remarks>
        public float EffectiveReach => _range + _lungeSpeed * StartupSeconds;

        /// <summary>前摇中允许攻击者继续转向追踪目标的时间比例。</summary>
        public float TrackingWindowFraction => _trackingWindowFraction;

        /// <summary>攻击者在挥击的当前时刻是否仍可转向。</summary>
        /// <param name="phase">挥击的当前阶段。</param>
        /// <param name="elapsedSeconds">从挥击开始到现在的时间。</param>
        /// <returns>允许追踪时为 true。</returns>
        public bool CanTrack(AttackPhase phase, float elapsedSeconds) =>
            AttackTracking.CanTrack(phase, elapsedSeconds, _startupSeconds, _trackingWindowFraction);

        /// <summary>成功命中时施加的冻结时长。</summary>
        public float HitStopSeconds => _hitStopSeconds;

        /// <summary>成功命中时增加的摄像机震动强度。</summary>
        public float CameraTrauma => _cameraTrauma;

        /// <summary>前摇预警的颜色。</summary>
        public Color TelegraphColor => _telegraphColor;

        /// <summary>是否绘制前摇预警。</summary>
        public bool ShowTelegraph => _showTelegraph;

        /// <summary>此攻击是否无法被举起的防御挡住，因而必须闪避。</summary>
        public bool Unblockable => _unblockable;

        /// <summary>此攻击是否无法被弹反。</summary>
        /// <remarks>
        /// 它比 <see cref="Unblockable"/> 范围更窄，目的也相反。
        /// 无法格挡的攻击是一种要求防守者以不同方式应对的威胁；
        /// 无法招架的攻击则是攻击者已经付出代价换来的惩罚——先打破防御，
        /// 再施加处决——因此不应再次被刚刚被打破的同一防御拒绝。
        /// </remarks>
        public bool Unparryable => _unparryable;

        /// <summary>连击窗口开启的绝对时间，从攻击开始时计时。</summary>
        public float ComboWindowStartSeconds => ActiveEndSeconds + _recoverySeconds * _comboWindowStart;

        /// <summary>连击窗口关闭的绝对时间，从攻击开始时计时。</summary>
        public float ComboWindowEndSeconds => ActiveEndSeconds + _recoverySeconds * _comboWindowEnd;

        /// <summary>构建此攻击造成的命中描述。</summary>
        /// <param name="sourceTeam">发动攻击的阵营。</param>
        /// <param name="sourceInstanceId">攻击者的实例标识符。</param>
        /// <returns>已填充的命中描述，等待接触点数据。</returns>
        public DamageInfo BuildDamageInfo(CombatantTeam sourceTeam, int sourceInstanceId) => new DamageInfo
        {
            Amount = _damage,
            Type = _damageType,
            SourceTeam = sourceTeam,
            SourceInstanceId = sourceInstanceId,
            PoiseDamage = _poiseDamage,
            Stagger = _stagger,
            KnockbackSpeed = _knockbackSpeed,
            Reaction = _impactReaction,
            HitStopSeconds = _hitStopSeconds,
            Unblockable = _unblockable,
            Unparryable = _unparryable,
            HitDirection = Vector3.forward
        };

#if UNITY_EDITOR
        /// <inheritdoc />
        protected override void OnValidate()
        {
            base.OnValidate();

            // 颠倒的连击窗口会静默禁用连击，而不会报错；仅凭现象排查这种问题会令人困扰。
            if (_comboWindowEnd < _comboWindowStart)
            {
                _comboWindowEnd = _comboWindowStart;
            }
        }
#endif
    }
}
