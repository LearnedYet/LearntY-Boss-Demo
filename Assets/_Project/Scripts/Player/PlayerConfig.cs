using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Core.ScriptableObjects;
using UnityEngine;

namespace AdaptiveBossArena.Player
{
    /// <summary>
    /// 描述玩家角色操控的全部可调值。
    /// </summary>
    /// <remarks>
    /// 保存为资源，使运行时可调角色手感并保留结果，这是获得良好移动参数的实际方式。
    /// Boss 不读取这里任何字段；AI 没有此类型或其程序集的引用。
    /// </remarks>
    [CreateAssetMenu(
        fileName = "PlayerConfig",
        menuName = "Adaptive Boss Arena/Player/Player Config",
        order = 0)]
    public sealed class PlayerConfig : IdentifiableSO
    {
        [Header("Vitals")]
        [SerializeField]
        [Tooltip("Maximum health.")]
        private float _maxHealth = 100f;

        [SerializeField]
        [Tooltip("Maximum stamina, the shared budget for dashing and heavy attacks.")]
        private float _maxStamina = 100f;

        [SerializeField]
        [Tooltip("Stamina restored per second once regeneration resumes.")]
        private float _staminaRegenPerSecond = 28f;

        [SerializeField]
        [Tooltip("Pause after spending stamina before regeneration resumes. This is what stops " +
                 "dash-spamming without ever printing a refusal message at the player.")]
        private float _staminaRegenDelaySeconds = 0.6f;

        [Header("Movement")]
        [SerializeField]
        [Tooltip("Top movement speed in world units per second.")]
        private float _moveSpeed = 7f;

        [SerializeField]
        [Tooltip("Time to reach top speed from rest. Small values feel snappy; zero feels robotic.")]
        private float _accelerationSeconds = 0.06f;

        [SerializeField]
        [Tooltip("Time to come to rest from top speed.")]
        private float _decelerationSeconds = 0.08f;

        [SerializeField]
        [Tooltip("How long a shove such as knockback takes to lose half its speed. Longer carries the " +
                 "player further on the same blow.")]
        [Range(0.02f, 0.6f)]
        private float _impulseHalfLifeSeconds = 0.12f;

        [Header("Knockdowns and launches")]
        [SerializeField]
        [Tooltip("Upward speed a launch throws the player at. With gravity 25, 9 m/s is about 0.7 s in the air.")]
        [Range(1f, 20f)]
        private float _launchUpwardSpeed = 9f;

        [SerializeField]
        [Tooltip("Longest the player can stay airborne before landing is assumed. A hard ceiling on airtime, " +
                 "so a body caught on geometry cannot hang in the air.")]
        [Range(0.3f, 3f)]
        private float _maximumAirborneSeconds = 1.2f;

        [SerializeField]
        [Tooltip("Time spent on the floor after a knockdown or a landing. The player cannot be hurt here.")]
        [Range(0.1f, 3f)]
        private float _knockdownSeconds = 0.8f;

        [SerializeField]
        [Tooltip("Time spent rising. Fully invulnerable, and posture refills when it ends.")]
        [Range(0.1f, 2f)]
        private float _getUpSeconds = 0.6f;

        [SerializeField]
        [Tooltip("After standing, knockdowns and launches arrive as knockback for this long, so none can chain.")]
        [Range(0f, 10f)]
        private float _knockdownImmunitySeconds = 2.5f;

        [SerializeField]
        [Tooltip("How much harder a knockback reaction shoves than the hit's ordinary knockback.")]
        [Range(1f, 4f)]
        private float _knockbackReactionMultiplier = 1.6f;

        [SerializeField]
        [Tooltip("Shove speed into a wall at or above which the impact staggers the player.")]
        [Range(1f, 30f)]
        private float _wallImpactSpeed = 7f;

        [SerializeField]
        [Tooltip("How long a hard wall impact staggers the player.")]
        [Range(0.05f, 1.5f)]
        private float _wallImpactStaggerSeconds = 0.3f;

        [SerializeField]
        [Tooltip("Fraction of top speed kept when reversing direction outright at a sprint. Lower " +
                 "makes the knight heavier to turn.")]
        [Range(0f, 1f)]
        private float _turnSpeedFloor = 0.35f;

        [SerializeField]
        [Tooltip("Relative mass in body contact. Compared against the boss's to decide who gives ground.")]
        [Min(0.01f)]
        private float _bodyMass = 1f;

        [SerializeField]
        [Tooltip("Turn rate in degrees per second.")]
        private float _turnSpeedDegreesPerSecond = 1080f;

        [Header("Dash")]
        [SerializeField]
        [Tooltip("Distance covered by a single dash.")]
        private float _dashDistance = 4.5f;

        [SerializeField]
        [Tooltip("Duration of the dash movement.")]
        private float _dashDurationSeconds = 0.18f;

        [SerializeField]
        [Tooltip("Lockout after a dash ends before another may begin.")]
        private float _dashCooldownSeconds = 0.25f;

        [SerializeField]
        [Tooltip("Stamina consumed per dash.")]
        private float _dashStaminaCost = 22f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Seconds of invulnerability from the start of a dodge. Absolute rather than a fraction " +
                 "of the roll, so lengthening the roll can never lengthen the invincibility with it.")]
        private float _invulnerabilitySeconds = 0.135f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Fraction of the roll, from its start, during which the stick can still steer it.")]
        private float _dashSteerFraction = 0.2f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Fraction of the roll after which a buffered attack may cancel the rest of it.")]
        private float _dashCancelFraction = 0.7f;

        [Header("Weapons")]
        [SerializeField]
        [Tooltip("Weapons the player can switch between. The first is drawn at the start of a fight.")]
        private WeaponDefinition[] _weapons = new WeaponDefinition[0];

        [Header("Attacks (fallback)")]
        [SerializeField]
        [Tooltip("Used only when no weapon is equipped, so the character is never left unable to act.")]
        private AttackDefinition[] _lightComboChain = new AttackDefinition[0];

        [SerializeField]
        [Tooltip("Heavy attack. Slow and committed, with a recovery long enough to be punished.")]
        private AttackDefinition _heavyAttack;

        [SerializeField]
        [Tooltip("Special ability attack.")]
        private AttackDefinition _specialAttack;

        [SerializeField]
        [Tooltip("Cooldown on the special ability.")]
        private float _specialCooldownSeconds = 8f;

        [SerializeField]
        [Tooltip("Stronger special used when the focus meter is full. Falls back to the normal " +
                 "special when empty, so a full meter is never wasted.")]
        private AttackDefinition _empoweredSpecialAttack;

        [Header("Focus")]
        [SerializeField]
        [Tooltip("Focus needed to empower a special. Earned by defending well, spent on the payoff.")]
        private float _maxFocus = 100f;

        [SerializeField]
        [Tooltip("Focus gained from a clean deflect.")]
        private float _focusPerDeflect = 34f;

        [SerializeField]
        [Tooltip("Focus gained from a perfect dodge.")]
        private float _focusPerPerfectDodge = 26f;

        [SerializeField]
        [Tooltip("Strike used to punish a broken guard. Falls back to the heavy attack when empty.")]
        private AttackDefinition _riposteAttack;

        [SerializeField]
        [Tooltip("The execution: a devastating multi-hit riposte thrown into a broken guard. Falls " +
                 "back to the ordinary riposte when empty.")]
        private AttackDefinition _executionAttack;

        [Header("Posture")]
        [SerializeField]
        [Tooltip("Posture capacity. Blocking late drains this; running out breaks the guard and " +
                 "leaves the player open to being riposted in turn.")]
        private float _maxPosture = 100f;

        [SerializeField]
        [Tooltip("Posture recovered per second while not blocking.")]
        private float _postureRegenPerSecond = 22f;

        [SerializeField]
        [Tooltip("Posture cost of blocking a hit late rather than deflecting it.")]
        private float _blockPostureCost = 22f;

        [SerializeField]
        [Range(0f, 0.5f)]
        [Tooltip("Fraction of damage that gets through a late block as chip damage.")]
        private float _blockChipFraction = 0.15f;

        [SerializeField]
        [Tooltip("Posture dealt to the attacker by a clean deflect. The core of the deflect economy: " +
                 "standing your ground has to make progress, not merely survive.")]
        private float _deflectPostureDamage = 28f;

        [Header("Healing")]
        [SerializeField]
        [Tooltip("Health restored per use.")]
        private float _healAmount = 35f;

        [SerializeField]
        [Tooltip("Uses available per attempt at the boss.")]
        private int _healCharges = 3;

        [SerializeField]
        [Tooltip("Channel time before the heal lands. Long enough that the boss can legitimately " +
                 "punish a heal it saw start.")]
        private float _healChannelSeconds = 0.9f;

        /// <summary>最大生命值。</summary>
        public float MaxHealth => _maxHealth;

        /// <summary>最大体力。</summary>
        public float MaxStamina => _maxStamina;

        /// <summary>每秒恢复的体力。</summary>
        public float StaminaRegenPerSecond => _staminaRegenPerSecond;

        /// <summary>消耗体力后恢复再生前的延迟。</summary>
        public float StaminaRegenDelaySeconds => _staminaRegenDelaySeconds;

        /// <summary>最高移动速度。</summary>
        public float MoveSpeed => _moveSpeed;

        /// <summary>达到最高速度的时长。</summary>
        public float AccelerationSeconds => _accelerationSeconds;

        /// <summary>减速到静止的时长。</summary>
        public float DecelerationSeconds => _decelerationSeconds;

        /// <summary>击退等外加冲量的半衰期。</summary>
        public float ImpulseHalfLifeSeconds => _impulseHalfLifeSeconds;

        /// <summary>击飞的向上速度。</summary>
        public float LaunchUpwardSpeed => _launchUpwardSpeed;

        /// <summary>空中停留时长上限。</summary>
        public float MaximumAirborneSeconds => _maximumAirborneSeconds;

        /// <summary>倒地停留时长。</summary>
        public float KnockdownSeconds => _knockdownSeconds;

        /// <summary>从地面起身的时长。</summary>
        public float GetUpSeconds => _getUpSeconds;

        /// <summary>站起后击倒和击飞被降级的保护时长。</summary>
        public float KnockdownImmunitySeconds => _knockdownImmunitySeconds;

        /// <summary>反应类型为击退时的击退倍率。</summary>
        public float KnockbackReactionMultiplier => _knockbackReactionMultiplier;

        /// <summary>外力推动撞墙导致硬直的速度阈值。</summary>
        public float WallImpactSpeed => _wallImpactSpeed;

        /// <summary>猛烈撞墙造成的硬直。</summary>
        public float WallImpactStaggerSeconds => _wallImpactStaggerSeconds;

        /// <summary>全速反向时保留的最高速度比例。</summary>
        public float TurnSpeedFloor => _turnSpeedFloor;

        /// <summary>身体接触时的相对质量。</summary>
        public float BodyMass => _bodyMass;

        /// <summary>每秒转向角度。</summary>
        public float TurnSpeedDegreesPerSecond => _turnSpeedDegreesPerSecond;

        /// <summary>一次闪避覆盖的距离。</summary>
        public float DashDistance => _dashDistance;

        /// <summary>一次闪避持续的时长。</summary>
        public float DashDurationSeconds => _dashDurationSeconds;

        /// <summary>闪避结束后再次闪避前的锁定时间。</summary>
        public float DashCooldownSeconds => _dashCooldownSeconds;

        /// <summary>每次闪避消耗的体力。</summary>
        public float DashStaminaCost => _dashStaminaCost;

        /// <summary>闪避无敌时长，从翻滚开始计秒。</summary>
        /// <remarks>
        /// 不超过翻滚本身时长，因此将翻滚调短于无敌时间时，也不会在结束后继续无敌。
        /// </remarks>
        public float InvulnerabilitySeconds => Mathf.Min(_invulnerabilitySeconds, _dashDurationSeconds);

        /// <summary>翻滚中仍允许摇杆调整方向的比例。</summary>
        public float DashSteerFraction => _dashSteerFraction;

        /// <summary>翻滚达到此比例后，缓存攻击可取消翻滚。</summary>
        public float DashCancelFraction => _dashCancelFraction;


        /// <summary>玩家可切换的武器。</summary>
        public WeaponDefinition[] Weapons => _weapons;

        /// <summary>按执行顺序排列的轻击连招，未装备武器时使用。</summary>
        public AttackDefinition[] LightComboChain => _lightComboChain;

        /// <summary>重击。</summary>
        public AttackDefinition HeavyAttack => _heavyAttack;

        /// <summary>特殊技能攻击。</summary>
        public AttackDefinition SpecialAttack => _specialAttack;

        /// <summary>特殊技能冷却。</summary>
        public float SpecialCooldownSeconds => _specialCooldownSeconds;

        /// <summary>专注充满时使用的强化特殊技能，null 表示复用普通技能。</summary>
        public AttackDefinition EmpoweredSpecialAttack => _empoweredSpecialAttack;

        /// <summary>强化特殊技能所需专注。</summary>
        public float MaxFocus => _maxFocus;

        /// <summary>精准弹反获得的专注。</summary>
        public float FocusPerDeflect => _focusPerDeflect;

        /// <summary>完美闪避获得的专注。</summary>
        public float FocusPerPerfectDodge => _focusPerPerfectDodge;

        /// <summary>惩罚被击破防线的攻击。</summary>
        public AttackDefinition RiposteAttack => _riposteAttack;

        /// <summary>惩罚被击破防线的处决，null 表示复用反击。</summary>
        public AttackDefinition ExecutionAttack => _executionAttack;

        /// <summary>架势容量。</summary>
        public float MaxPosture => _maxPosture;

        /// <summary>每秒恢复的架势。</summary>
        public float PostureRegenPerSecond => _postureRegenPerSecond;

        /// <summary>较晚格挡消耗的架势。</summary>
        public float BlockPostureCost => _blockPostureCost;

        /// <summary>较晚格挡仍穿透的伤害比例。</summary>
        public float BlockChipFraction => _blockChipFraction;

        /// <summary>精准弹反对攻击者造成的架势伤害。</summary>
        public float DeflectPostureDamage => _deflectPostureDamage;

        /// <summary>每次治疗恢复的生命值。</summary>
        public float HealAmount => _healAmount;

        /// <summary>每次挑战可用的治疗次数。</summary>
        public int HealCharges => _healCharges;

        /// <summary>治疗生效前的引导时间。</summary>
        public float HealChannelSeconds => _healChannelSeconds;
    }
}
