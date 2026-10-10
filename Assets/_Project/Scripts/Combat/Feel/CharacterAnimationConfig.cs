using AdaptiveBossArena.Core.ScriptableObjects;
using UnityEngine;

namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 角色程序化动画的可调幅度。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 游戏角色采用基础几何体的目的，就是便于通过代码移动，
    /// 因此全部生命感都来自 <see cref="CharacterAnimator"/> 读取这些数值。保存为资源，
    /// 让手感能在运行模式调参并保留——这也是<see cref="AdaptiveBossArena.Player.PlayerConfig"/> 采用
    /// 资源的原因。
    /// </para>
    /// <para>
    /// 这里所有内容都是<em>幅度</em>，绝不是规则。动画仅用于表现：它改变挥击外观，
    /// 绝不改变命中盒开启时机。时序属于攻击自身的帧数据，动画器只读取，绝不修改。
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "CharacterAnimationConfig",
        menuName = "Adaptive Boss Arena/Feel/Character Animation Config",
        order = 10)]
    public sealed class CharacterAnimationConfig : IdentifiableSO
    {
        [Header("Art")]
        [SerializeField]
        [Tooltip("Optional rigged model. Assign one and the prefab generator builds the character " +
                 "around it instead of the generated body, and the skeletal animation bridge takes " +
                 "over the pose. Left empty, the generated body is used.")]
        private GameObject _rigPrefab;

        [SerializeField]
        [Tooltip("Controller the rig plays through. Generated; states are crossfaded to by the bridge.")]
        private RuntimeAnimatorController _animatorController;

        [SerializeField]
        [Tooltip("Controller for the final, weaponless phase, or none for a fighter that has no second form.")]
        private RuntimeAnimatorController _frenzyController;

        [SerializeField]
        [Tooltip("Uniform scale applied to the rig, so one mannequin can be a knight or a brute.")]
        [Min(0.1f)]
        private float _rigScale = 1f;

        [SerializeField]
        [Tooltip("Which attack state each attack plays. Attacks not listed fall back by damage type.")]
        private AttackClipBinding[] _attackClips = System.Array.Empty<AttackClipBinding>();

        [SerializeField]
        [Tooltip("Where in each attack clip, as a fraction of its length, the blade lands.")]
        [Range(0.05f, 0.95f)]
        private float _clipContactFraction = 0.45f;

        [Header("Idle")]
        [SerializeField]
        [Tooltip("Vertical breathing amplitude while standing, in world units.")]
        private float _idleBobAmplitude = 0.04f;

        [SerializeField]
        [Tooltip("Breaths per second while idle.")]
        private float _idleBobFrequency = 1.4f;

        [SerializeField]
        [Tooltip("Gentle side-to-side sway while idle, in degrees.")]
        private float _idleSwayDegrees = 2f;

        [Header("Movement")]
        [SerializeField]
        [Tooltip("Forward lean at full running speed, in degrees. This is most of what makes a slide " +
                 "read as a run.")]
        private float _moveLeanDegrees = 11f;

        [Header("Attack — anticipation (startup)")]
        [SerializeField]
        [Tooltip("How far the body coils back and down while winding up, in world units.")]
        private float _anticipationCrouch = 0.13f;

        [SerializeField]
        [Range(0f, 0.5f)]
        [Tooltip("How much the body squashes (wider, shorter) while winding up.")]
        private float _anticipationSquash = 0.14f;

        [SerializeField]
        [Tooltip("How far the body leans back while winding up, in degrees.")]
        private float _anticipationLeanDegrees = 15f;

        [Header("Attack — strike (active)")]
        [SerializeField]
        [Tooltip("How far the body lunges forward on the live frames, in world units. The snap comes " +
                 "from the easing, not from this value.")]
        private float _strikeLunge = 0.38f;

        [SerializeField]
        [Range(0f, 0.5f)]
        [Tooltip("How much the body stretches (taller, thinner) as it commits to the blow.")]
        private float _strikeStretch = 0.16f;

        [SerializeField]
        [Tooltip("How far the body leans into the blow, in degrees.")]
        private float _strikeLeanDegrees = 19f;

        [SerializeField]
        [Range(1f, 3f)]
        [Tooltip("Multiplier applied to anticipation and strike for heavy attacks and abilities, so " +
                 "weight reads without new poses.")]
        private float _heavyMultiplier = 1.5f;

        [Header("Guard & dash")]
        [SerializeField]
        [Tooltip("How far the body sinks into a braced stance while guarding, in world units.")]
        private float _guardCrouch = 0.1f;

        [SerializeField]
        [Tooltip("Forward brace lean while guarding, in degrees.")]
        private float _guardLeanDegrees = 9f;

        [SerializeField]
        [Tooltip("Lean into the direction of travel during a dash, in degrees.")]
        private float _dashLeanDegrees = 24f;

        [SerializeField]
        [Range(0f, 0.5f)]
        [Tooltip("Stretch along the dash, selling the burst of speed.")]
        private float _dashStretch = 0.2f;

        [Header("Heal")]
        [SerializeField]
        [Tooltip("How far the body drops into a vulnerable crouch while channelling a heal.")]
        private float _healCrouch = 0.18f;

        [Header("Hit reaction")]
        [SerializeField]
        [Tooltip("How far a struck body is thrown along the blow, in world units.")]
        private float _recoilDistance = 0.3f;

        [SerializeField]
        [Range(0.05f, 0.6f)]
        [Tooltip("How long a hit recoil takes to spring back.")]
        private float _recoilDurationSeconds = 0.22f;

        [SerializeField]
        [Range(0f, 0.5f)]
        [Tooltip("Squash flinch at the moment of being hit.")]
        private float _recoilSquash = 0.13f;

        [Header("Death")]
        [SerializeField]
        [Tooltip("How far the body topples when defeated, in degrees.")]
        private float _deathFallDegrees = 82f;

        [SerializeField]
        [Tooltip("How far the body sinks as it falls, in world units.")]
        private float _deathSink = 0.35f;

        [Header("Flourish (phase transition)")]
        [SerializeField]
        [Tooltip("How far the boss rears back during a phase transition, in degrees.")]
        private float _flourishRearDegrees = 26f;

        [SerializeField]
        [Range(0f, 0.6f)]
        [Tooltip("Stretch during a phase-transition rear-up.")]
        private float _flourishStretch = 0.24f;

        [SerializeField]
        [Range(0.1f, 2f)]
        [Tooltip("How long a phase-transition flourish lasts.")]
        private float _flourishDurationSeconds = 0.7f;

        [Header("Easing (half-lives)")]
        [SerializeField]
        [Range(0.01f, 0.4f)]
        [Tooltip("Position easing half-life. Lower is snappier; the boss usually wants this higher " +
                 "than the player to feel heavy.")]
        private float _positionHalfLife = 0.05f;

        [SerializeField]
        [Range(0.01f, 0.4f)]
        [Tooltip("Rotation easing half-life.")]
        private float _rotationHalfLife = 0.05f;

        [SerializeField]
        [Range(0.01f, 0.4f)]
        [Tooltip("Scale (squash & stretch) easing half-life.")]
        private float _scaleHalfLife = 0.06f;

        /// <summary>空闲时垂直呼吸幅度。</summary>
        /// <summary>
        /// 围绕其构建角色的骨架模型；为 null 则使用生成的身体。
        /// </summary>
        /// <remarks>
        /// 放在资源中，而不是手动加入预制体，因为预制体构建器每次都会重写整个层级：
        /// 直接放入预制体的任何对象都会在下次设置项目时被销毁。从这里引用，
        /// 意味着构建器每次会将它放回，让下载的角色在重新生成后保留。
        /// </remarks>
        public GameObject RigPrefab => _rigPrefab;

        /// <summary>骨架播放所使用的控制器。</summary>
        public RuntimeAnimatorController AnimatorController => _animatorController;

        /// <summary>最终阶段的控制器，该阶段战斗者已经丢弃武器。</summary>
        /// <remarks>始终采用相同方式战斗的对象为 null，即除巨兽之外的所有战斗者。</remarks>
        public RuntimeAnimatorController FrenzyController => _frenzyController;

        /// <summary>应用于骨架的统一缩放。</summary>
        public float RigScale => _rigScale;

        /// <summary>各攻击片段中刀刃落下的位置，以片段长度比例表示。</summary>
        public float ClipContactFraction => _clipContactFraction;

        /// <summary>攻击打击在片段中落下的时刻。</summary>
        /// <remarks>
        /// 生成器逐片段测量，因为片段差异太大，无法共用一个数值：一秒的斩击与两秒的跳跃，
        /// 不会在相同进度命中。
        /// </remarks>
        /// <param name="attack">进行中的攻击。</param>
        /// <param name="frenzied">
        /// 是否正在播放狂暴控制器。相同状态在其中播放不同片段——挥拳而不是举刃——打击也在不同位置落下。
        /// </param>
        /// <returns>接触比例；未测量时回退到配置默认值。</returns>
        public float ContactFractionFor(AttackDefinition attack, bool frenzied = false)
        {
            foreach (AttackClipBinding binding in _attackClips)
            {
                if (binding.Attack != attack)
                {
                    continue;
                }

                if (frenzied && binding.FrenzyContactFraction > 0f)
                {
                    return binding.FrenzyContactFraction;
                }

                if (binding.ContactFraction > 0f)
                {
                    return binding.ContactFraction;
                }
            }

            return _clipContactFraction;
        }

        /// <summary>各攻击的片段绑定，供生成器测量使用。</summary>
        public System.Collections.Generic.IReadOnlyList<AttackClipBinding> AttackClips => _attackClips;

        /// <summary>攻击播放的攻击状态。</summary>
        /// <param name="attack">进行中的攻击。</param>
        /// <returns>状态名称；攻击没有绑定时，根据伤害类型回退。</returns>
        public string AttackStateFor(AttackDefinition attack)
        {
            if (attack == null)
            {
                return CharacterAnimatorParameters.DefaultLightState;
            }

            foreach (AttackClipBinding binding in _attackClips)
            {
                if (binding.Attack == attack && !string.IsNullOrEmpty(binding.State))
                {
                    return binding.State;
                }
            }

            switch (attack.DamageType)
            {
                case Core.Combat.DamageType.Heavy:
                    return CharacterAnimatorParameters.DefaultHeavyState;

                case Core.Combat.DamageType.Special:
                    return CharacterAnimatorParameters.DefaultSpecialState;

                default:
                    return CharacterAnimatorParameters.DefaultLightState;
            }
        }

        public float IdleBobAmplitude => _idleBobAmplitude;

        /// <summary>空闲时每秒呼吸次数。</summary>
        public float IdleBobFrequency => _idleBobFrequency;

        /// <summary>空闲摇摆幅度，单位为度。</summary>
        public float IdleSwayDegrees => _idleSwayDegrees;

        /// <summary>全速奔跑时前倾角度，单位为度。</summary>
        public float MoveLeanDegrees => _moveLeanDegrees;

        /// <summary>攻击前摇期间向后蓄势的距离。</summary>
        public float AnticipationCrouch => _anticipationCrouch;

        /// <summary>攻击前摇期间的压缩量。</summary>
        public float AnticipationSquash => _anticipationSquash;

        /// <summary>攻击前摇期间的后倾角度。</summary>
        public float AnticipationLeanDegrees => _anticipationLeanDegrees;

        /// <summary>生效帧的向前突进距离。</summary>
        public float StrikeLunge => _strikeLunge;

        /// <summary>投入打击时的拉伸量。</summary>
        public float StrikeStretch => _strikeStretch;

        /// <summary>生效帧的前倾角度。</summary>
        public float StrikeLeanDegrees => _strikeLeanDegrees;

        /// <summary>应用于重攻击和技能的倍率。</summary>
        public float HeavyMultiplier => _heavyMultiplier;

        /// <summary>防御时支撑姿态的下沉距离。</summary>
        public float GuardCrouch => _guardCrouch;

        /// <summary>防御时支撑姿态的倾斜。</summary>
        public float GuardLeanDegrees => _guardLeanDegrees;

        /// <summary>冲刺期间沿移动方向倾斜。</summary>
        public float DashLeanDegrees => _dashLeanDegrees;

        /// <summary>沿冲刺方向的拉伸。</summary>
        public float DashStretch => _dashStretch;

        /// <summary>治疗时易受攻击的蹲伏深度。</summary>
        public float HealCrouch => _healCrouch;

        /// <summary>受击反作用的推开距离。</summary>
        public float RecoilDistance => _recoilDistance;

        /// <summary>受击反作用的回弹时长。</summary>
        public float RecoilDurationSeconds => _recoilDurationSeconds;

        /// <summary>受击时的压缩动作。</summary>
        public float RecoilSquash => _recoilSquash;

        /// <summary>战败时的倾倒角度。</summary>
        public float DeathFallDegrees => _deathFallDegrees;

        /// <summary>战败时的下沉距离。</summary>
        public float DeathSink => _deathSink;

        /// <summary>阶段转换时的后仰角度。</summary>
        public float FlourishRearDegrees => _flourishRearDegrees;

        /// <summary>阶段转换期间的拉伸。</summary>
        public float FlourishStretch => _flourishStretch;

        /// <summary>阶段转换表现动作的时长。</summary>
        public float FlourishDurationSeconds => _flourishDurationSeconds;

        /// <summary>位置缓动半衰期。</summary>
        public float PositionHalfLife => _positionHalfLife;

        /// <summary>旋转缓动半衰期。</summary>
        public float RotationHalfLife => _rotationHalfLife;

        /// <summary>缩放缓动半衰期。</summary>
        public float ScaleHalfLife => _scaleHalfLife;
    }

    /// <summary>某次攻击播放的攻击状态。</summary>
    [System.Serializable]
    public struct AttackClipBinding
    {
        [SerializeField]
        [Tooltip("The attack.")]
        private AttackDefinition _attack;

        [SerializeField]
        [Tooltip("The Animator state it plays.")]
        private string _state;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Fraction of the clip at which the blow lands, measured from the striking limb. Zero uses the config's default.")]
        private float _contactFraction;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("The same, for the clip the frenzy plays in this state. Zero falls back to the armed fraction.")]
        private float _frenzyContactFraction;

        /// <summary>攻击。</summary>
        public AttackDefinition Attack => _attack;

        /// <summary>播放的 Animator 状态。</summary>
        public string State => _state;

        /// <summary>打击落下时的片段比例，未测量时为零。</summary>
        public float ContactFraction => _contactFraction;

        /// <summary>打击在狂暴片段中落下时的比例，未测量时为零。</summary>
        public float FrenzyContactFraction => _frenzyContactFraction;
    }
}
