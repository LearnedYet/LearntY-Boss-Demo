using System;
using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Core.Perception;
using AdaptiveBossArena.Core.ScriptableObjects;
using AdaptiveBossArena.Learning;
using UnityEngine;

namespace AdaptiveBossArena.AI
{
    /// <summary>Boss 进入新阶段时改变的全部内容。</summary>
    /// <remarks>
    /// 阶段升级通过扩展招式和提高适应速度实现，绝不提供原本没有的信息。第三阶段 Boss 反应更快、
    /// 投入更深，但仍只能看见第一阶段可见的信息。
    /// </remarks>
    [Serializable]
    public struct BossPhaseDefinition
    {
        [SerializeField]
        [Tooltip("Name shown in debug tooling.")]
        private string _name;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Health fraction at or below which this phase begins.")]
        private float _healthThreshold;

        [SerializeField]
        [Tooltip("Attacks available during this phase.")]
        private AttackDefinition[] _attacks;

        [SerializeField]
        [Tooltip("The phase's signature attack, weighted up in selection so each phase has a " +
                 "recognisable, readable threat. Optional; leave empty for no signature.")]
        private AttackDefinition _signatureAttack;

        [SerializeField]
        [Range(0.5f, 2f)]
        [Tooltip("Multiplier on movement speed for this phase.")]
        private float _moveSpeedMultiplier;

        [SerializeField]
        [Range(0f, 3f)]
        [Tooltip("Pause between attacks. Lower values make the phase more relentless.")]
        private float _attackCooldownSeconds;

        [SerializeField]
        [Range(0.5f, 3f)]
        [Tooltip("Multiplier on how quickly the boss adopts new counter-strategies in this phase.")]
        private float _adaptationRateMultiplier;

        /// <summary>调试工具显示的名称。</summary>
        public string Name => _name;

        /// <summary>生命比例低于或等于此值时进入阶段。</summary>
        public float HealthThreshold => _healthThreshold;

        /// <summary>本阶段可用攻击。</summary>
        public AttackDefinition[] Attacks => _attacks;

        /// <summary>选择时提高权重的阶段标志攻击，无则为 null。</summary>
        public AttackDefinition SignatureAttack => _signatureAttack;

        /// <summary>本阶段移动速度倍率。</summary>
        public float MoveSpeedMultiplier => _moveSpeedMultiplier;

        /// <summary>本阶段攻击之间的暂停时长。</summary>
        public float AttackCooldownSeconds => _attackCooldownSeconds;

        /// <summary>本阶段适应速度倍率。</summary>
        public float AdaptationRateMultiplier => _adaptationRateMultiplier;
    }

    /// <summary>
    /// 描述 Boss、阶段和学习能力的全部可调值。
    /// </summary>
    /// <remarks>
    /// 基础调参值描述 Boss 尚未学到任何东西时的行为。适应系统只会逐渐向新目标调整，
    /// 因此此资源也定义了所反制习惯消失后 Boss 回归的值。
    /// </remarks>
    [CreateAssetMenu(
        fileName = "BossConfig",
        menuName = "Adaptive Boss Arena/AI/Boss Config",
        order = 1)]
    public sealed class BossConfig : IdentifiableSO
    {
        [Header("Vitals")]
        [SerializeField]
        [Tooltip("Maximum health.")]
        private float _maxHealth = 650f;

        [SerializeField]
        [Tooltip("Poise capacity. Enough accumulated poise damage staggers the boss and opens a punish.")]
        private float _maxPoise = 100f;

        [SerializeField]
        [Tooltip("Poise restored per second while not being hit.")]
        private float _poiseRegenPerSecond = 12f;

        [Header("Parry")]
        [SerializeField]
        [Tooltip("How readily the boss answers a heavy swing with a parry before it has learned " +
                 "anything. Small but non-zero so the duel exists from the opening; the learning " +
                 "loop raises it against a player who leans on heavies.")]
        [Range(0f, 1f)]
        private float _baselineParryChance = 0.12f;

        [SerializeField]
        [Tooltip("How long after committing to the stance a hit is refused outright. Deliberately " +
                 "tighter than the player's deflect window: the player chooses when to swing, the " +
                 "boss only guesses.")]
        [Range(0.05f, 0.4f)]
        private float _parryWindowSeconds = 0.18f;

        [SerializeField]
        [Tooltip("How long the boss remains committed after the window closes. This is the punish " +
                 "for a stance thrown at the wrong moment; hits landing here connect in full.")]
        [Range(0.1f, 1f)]
        private float _parryTailSeconds = 0.45f;

        [SerializeField]
        [Tooltip("Posture the boss takes off the player for a successful parry. The mirror of what " +
                 "a clean deflect costs the boss.")]
        private float _parryPostureDamage = 24f;

        [SerializeField]
        [Tooltip("How long before the boss may attempt another parry. Held separately from the " +
                 "attack cooldown, which a successful parry clears.")]
        private float _parryCooldownSeconds = 1.2f;

        [Header("Movement")]
        [SerializeField]
        [Tooltip("Base movement speed in world units per second.")]
        private float _moveSpeed = 5f;

        [SerializeField]
        [Tooltip("Turn rate in degrees per second. The main lever on how easily the boss is circled.")]
        private float _turnSpeedDegreesPerSecond = 220f;

        [SerializeField]
        [Tooltip("Fraction of top speed the boss walks at while circling and measuring the player. Low, so it " +
                 "stalks with weight instead of running laps.")]
        [Range(0.1f, 1f)]
        private float _stalkSpeedFraction = 0.35f;

        [SerializeField]
        [Tooltip("Metres beyond its chosen attack's reach at and past which the boss breaks into a run. Closer " +
                 "than this it walks in.")]
        [Min(0f)]
        private float _approachRunDistance = 6f;

        [SerializeField]
        [Tooltip("Fraction of top speed the boss walks at when its attack is almost in reach.")]
        [Range(0.1f, 1f)]
        private float _approachWalkFraction = 0.45f;

        [SerializeField]
        [Tooltip("Seconds the boss's turning takes to ease up to speed. A heavy body winds into a turn instead " +
                 "of snapping round at a constant rate.")]
        [Range(0f, 1f)]
        private float _turnSmoothingSeconds = 0.25f;

        [SerializeField]
        [Tooltip("How long a shove such as knockback takes to lose half its speed. Shorter than the " +
                 "player's: the boss is heavy, and sheds a blow faster.")]
        [Range(0.02f, 0.6f)]
        private float _impulseHalfLifeSeconds = 0.08f;

        [SerializeField]
        [Tooltip("Seconds the boss takes to reach or shed full speed. Blunter than the player's: a heavy " +
                 "body commits to where it is going.")]
        [Min(0f)]
        private float _accelerationSeconds = 0.25f;

        [SerializeField]
        [Tooltip("Speed a lunge loses per second once its wind-up ends. At 120 the Charge's 16 m/s " +
                 "stops in about a metre instead of sliding through its strike.")]
        [Min(0f)]
        private float _lungeBrakeDeceleration = 120f;

        [SerializeField]
        [Tooltip("Speed into a wall, mid-lunge, at or above which the boss hurts itself. Above its " +
                 "walking speed, so only a committed charge baited into a wall counts.")]
        [Min(0f)]
        private float _wallImpactSpeed = 9f;

        [SerializeField]
        [Tooltip("Poise a lunge into a wall costs the boss.")]
        [Min(0f)]
        private float _wallImpactPoiseDamage = 45f;

        [SerializeField]
        [Tooltip("Fraction of a blow's knockback the boss shrugs off with its stance intact.")]
        [Range(0f, 1f)]
        private float _knockbackResistanceAtFullPoise = 0.85f;

        [SerializeField]
        [Tooltip("Fraction of a blow's knockback the boss shrugs off while its stance is broken.")]
        [Range(0f, 1f)]
        private float _knockbackResistanceWhenBroken;

        [SerializeField]
        [Tooltip("Relative mass in body contact. Compared against the player's to decide who gives ground.")]
        [Min(0.01f)]
        private float _bodyMass = 4f;

        [Header("Perception")]
        [SerializeField]
        [Tooltip("Reaction and perception limits. Required; without it the boss would act on the " +
                 "current frame and read as a cheat.")]
        private ReactionProfile _reactionProfile;

        [Header("Phases")]
        [SerializeField]
        [Tooltip("Phase definitions in order, from full health downward.")]
        private BossPhaseDefinition[] _phases = new BossPhaseDefinition[0];

        [Header("Set Pieces")]
        [SerializeField]
        [Tooltip("The telegraphed unblockable shockwave the boss unleashes on a phase transition and " +
                 "as its Resolve gambit. Optional; without it those beats are skipped.")]
        private AttackDefinition _phaseShockwave;

        [Header("Learning")]
        [SerializeField]
        [Tooltip("Counter-strategies the boss may develop over the course of a fight.")]
        private CounterStrategy[] _counterStrategies = new CounterStrategy[0];

        [SerializeField]
        [Range(2f, 60f)]
        [Tooltip("How often the behaviour profile is recomputed. Coarse on purpose, so the boss " +
                 "responds to trends rather than to individual actions.")]
        private float _analysisIntervalSeconds = 2.5f;

        [SerializeField]
        [Range(5f, 60f)]
        [Tooltip("Minimum gap between adaptations taking effect, giving the player time to notice " +
                 "and respond to each one.")]
        private float _minSecondsBetweenAdaptations = 12f;

        [SerializeField]
        [Range(10f, 180f)]
        [Tooltip("Half-life of the boss's memory. Habits the player abandons fade over this period, " +
                 "which is what makes switching tactics a viable answer to being countered.")]
        private float _memoryHalfLifeSeconds = 45f;

        [Header("Resolve (Gambit)")]
        [SerializeField]
        [Tooltip("Resolve the boss must build before it unleashes its gambit set-piece.")]
        private float _resolveThreshold = 100f;

        [SerializeField]
        [Tooltip("Resolve gained per second of fighting.")]
        private float _resolveBuildPerSecond = 3.2f;

        [SerializeField]
        [Tooltip("Resolve gained each time one of the boss's own attacks whiffs — an evasive player " +
                 "winds the gambit up faster.")]
        private float _resolveBuildPerWhiff = 14f;

        [Header("Overbalance (Reads & Punishes)")]
        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Chance a committed swing that whiffs overbalances the boss, at full commitment. " +
                 "Scales down to zero as the boss's commitment falls to its baseline.")]
        private float _overbalanceMaxChance = 0.6f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Commitment below which the boss never overbalances, so minor adaptation does not " +
                 "open it up.")]
        private float _overbalanceIntensityThreshold = 0.15f;

        [SerializeField]
        [Range(0.2f, 3f)]
        [Tooltip("How long the stumble lasts at full commitment, in seconds.")]
        private float _overbalanceRecoverySeconds = 1.1f;

        [SerializeField]
        [Range(1f, 4f)]
        [Tooltip("How much incoming poise damage is multiplied while the boss is overbalanced, so a " +
                 "hit landed in the window rushes the posture break that opens the execution.")]
        private float _overbalancePoiseMultiplier = 2.4f;

        [Header("Baseline Tuning")]
        [SerializeField]
        [Tooltip("Distance the boss prefers to hold before it has learned anything.")]
        private float _baselinePreferredRange = 3f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Willingness to press the attack before it has learned anything.")]
        private float _baselineAggression = 0.5f;

        /// <summary>最大生命值。</summary>
        public float MaxHealth => _maxHealth;

        /// <summary>架势容量。</summary>
        public float MaxPoise => _maxPoise;

        /// <summary>每秒恢复的架势。</summary>
        public float PoiseRegenPerSecond => _poiseRegenPerSecond;

        /// <summary>Boss 尚未学习时的招架意愿。</summary>
        public float BaselineParryChance => _baselineParryChance;

        /// <summary>招架站姿完全拒绝命中的时长。</summary>
        public float ParryWindowSeconds => _parryWindowSeconds;

        /// <summary>招架窗口关闭后 Boss 仍承担动作投入的时长。</summary>
        public float ParryTailSeconds => _parryTailSeconds;

        /// <summary>Boss 成功招架对玩家造成的架势伤害。</summary>
        public float ParryPostureDamage => _parryPostureDamage;

        /// <summary>Boss 再次尝试招架前需等待的时长。</summary>
        public float ParryCooldownSeconds => _parryCooldownSeconds;

        /// <summary>基础移动速度。</summary>
        public float MoveSpeed => _moveSpeed;

        /// <summary>每秒转向角度。</summary>
        public float TurnSpeedDegreesPerSecond => _turnSpeedDegreesPerSecond;

        /// <summary>击退等外加冲量的半衰期。</summary>
        public float ImpulseHalfLifeSeconds => _impulseHalfLifeSeconds;

        /// <summary>Boss 加速到全速或减速到静止所需秒数。</summary>
        public float AccelerationSeconds => _accelerationSeconds;

        /// <summary>绕玩家移动时，Boss 行走速度占最高速度的比例。</summary>
        public float StalkSpeedFraction => _stalkSpeedFraction;

        /// <summary>超出攻击范围的距离，达到或超过时 Boss 跑动接近。</summary>
        public float ApproachRunDistance => _approachRunDistance;

        /// <summary>攻击即将够到目标时，Boss 行走速度占最高速度的比例。</summary>
        public float ApproachWalkFraction => _approachWalkFraction;

        /// <summary>Boss 转向平滑提升到全速所需秒数。</summary>
        public float TurnSmoothingSeconds => _turnSmoothingSeconds;

        /// <summary>前摇结束后突进每秒损失的速度。</summary>
        public float LungeBrakeDeceleration => _lungeBrakeDeceleration;

        /// <summary>突进撞墙使 Boss 受伤的速度阈值。</summary>
        public float WallImpactSpeed => _wallImpactSpeed;

        /// <summary>突进撞墙损失的架势。</summary>
        public float WallImpactPoiseDamage => _wallImpactPoiseDamage;

        /// <summary>架势完整时抵消的击退比例。</summary>
        public float KnockbackResistanceAtFullPoise => _knockbackResistanceAtFullPoise;

        /// <summary>架势被击破时抵消的击退比例。</summary>
        public float KnockbackResistanceWhenBroken => _knockbackResistanceWhenBroken;

        /// <summary>身体接触时的相对质量。</summary>
        public float BodyMass => _bodyMass;

        /// <summary>反应和感知限制。</summary>
        public ReactionProfile ReactionProfile => _reactionProfile;

        /// <summary>按顺序排列的阶段定义。</summary>
        public BossPhaseDefinition[] Phases => _phases;

        /// <summary>阶段变化和孤注一掷使用的带预警不可格挡冲击波，可为 null。</summary>
        public AttackDefinition PhaseShockwave => _phaseShockwave;

        /// <summary>Boss 可用的反制策略。</summary>
        public CounterStrategy[] CounterStrategies => _counterStrategies;

        /// <summary>重算行为画像的频率。</summary>
        public float AnalysisIntervalSeconds => _analysisIntervalSeconds;

        /// <summary>适应之间的最短间隔。</summary>
        public float MinSecondsBetweenAdaptations => _minSecondsBetweenAdaptations;

        /// <summary>Boss 行为记忆的半衰期。</summary>
        public float MemoryHalfLifeSeconds => _memoryHalfLifeSeconds;

        /// <summary>孤注一掷准备完成所需的决心。</summary>
        public float ResolveThreshold => _resolveThreshold;

        /// <summary>战斗每秒获得的决心。</summary>
        public float ResolveBuildPerSecond => _resolveBuildPerSecond;

        /// <summary>Boss 每次攻击挥空获得的决心。</summary>
        public float ResolveBuildPerWhiff => _resolveBuildPerWhiff;

        /// <summary>全投入时，高投入挥空使 Boss 失衡的概率。</summary>
        public float OverbalanceMaxChance => _overbalanceMaxChance;

        /// <summary>低于此投入程度时 Boss 永不失衡。</summary>
        public float OverbalanceIntensityThreshold => _overbalanceIntensityThreshold;

        /// <summary>全投入时失衡踉跄持续的秒数。</summary>
        public float OverbalanceRecoverySeconds => _overbalanceRecoverySeconds;

        /// <summary>Boss 失衡期间承受架势伤害的倍率。</summary>
        public float OverbalancePoiseMultiplier => _overbalancePoiseMultiplier;

        /// <summary>适应前偏好的距离。</summary>
        public float BaselinePreferredRange => _baselinePreferredRange;

        /// <summary>适应前的进攻性。</summary>
        public float BaselineAggression => _baselineAggression;

        /// <summary>
        /// 根据指定生命比例确定适用阶段。
        /// </summary>
        /// <param name="normalizedHealth">Boss 生命占最大值的比例。</param>
        /// <returns>从零开始的阶段索引，限制在配置范围内。</returns>
        public int ResolvePhaseIndex(float normalizedHealth)
        {
            if (_phases == null || _phases.Length == 0)
            {
                return 0;
            }

            // 阶段按生命值从高到低排列，因此最后一个已经越过阈值的阶段就是当前阶段。
            int index = 0;
            for (int i = 0; i < _phases.Length; i++)
            {
                if (normalizedHealth <= _phases[i].HealthThreshold)
                {
                    index = i;
                }
            }

            return index;
        }
    }
}
