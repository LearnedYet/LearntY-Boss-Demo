using AdaptiveBossArena.Core.ScriptableObjects;
using UnityEngine;

namespace AdaptiveBossArena.Combat
{
    /// <summary>武器应对来袭攻击的方式。</summary>
    /// <remarks>
    /// 每种武器有意采用不同的防御方式。如果三种武器的弹反完全相同，选择就只会影响外观，
    /// 首领也无法从玩家偏爱的武器中读出有意义的信息用于适应。
    /// </remarks>
    public enum DefenceStyle
    {
        /// <summary>严格的弹反窗口，以反击作为奖励，要求高且效果明确。</summary>
        Deflect = 0,

        /// <summary>
        /// 无法招架，改为让攻击在受到来袭命中后继续进行。
        /// </summary>
        /// <remarks>
        /// 以安全换取确定性：这把武器能赢得其投入的交锋，但在投入决策错误时会严重受挫。
        /// </remarks>
        HyperArmour = 1,

        /// <summary>可持续维持、消耗耐力的防御，无需精确把握时机。</summary>
        SustainedGuard = 2
    }

    /// <summary>
    /// 完整武器：招式、防御方式以及它如何改变战斗。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 招式配置放在这里，而不是角色上，因此切换武器真正改变玩家的战斗方式，而不是只给相同攻击换个外观。
    /// </para>
    /// <para>
    /// 三把武器是刻意设定的上限，而不是起点。首领通过几分钟战斗内的重复行为建立玩家画像；
    /// 如果将相同的观察预算分散到十几把武器，每种习惯都会低于置信度阈值，首领便会悄然完全停止适应。
    /// 三把足以让选择有意义，又少到每把仍能被频繁观察并从中学习。
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "WeaponDefinition",
        menuName = "Adaptive Boss Arena/Combat/Weapon Definition",
        order = 1)]
    public sealed class WeaponDefinition : IdentifiableSO
    {
        [Header("Identity")]
        [SerializeField]
        [Tooltip("Name shown when the weapon is drawn.")]
        private string _displayName = "Unnamed Weapon";

        [SerializeField]
        [Tooltip("Colour used for this weapon's attack overlays and trail.")]
        private Color _signatureColor = new Color(0.55f, 0.85f, 1f, 0.6f);

        [SerializeField]
        [Tooltip("Audio cue played when this weapon swings. Leaving it empty falls back to the " +
                 "generic swing, so a weapon without one still makes a sound.")]
        private string _swingCueId = string.Empty;

        [SerializeField]
        [Tooltip("Optional model shown in the character's hand while this weapon is drawn. Left empty " +
                 "the weapon reads as its colour and trail only; drop a mesh prefab in to make it " +
                 "appear, with no other change.")]
        private GameObject _modelPrefab;

        [Header("Moveset")]
        [SerializeField]
        [Tooltip("Light attack chain, executed in order while the combo window is met.")]
        private AttackDefinition[] _lightComboChain = new AttackDefinition[0];

        [SerializeField]
        [Tooltip("Heavy attack.")]
        private AttackDefinition _heavyAttack;

        [SerializeField]
        [Tooltip("Special ability attack.")]
        private AttackDefinition _specialAttack;

        [SerializeField]
        [Tooltip("Stronger special used when the focus meter is full. Optional; falls back to the " +
                 "normal special so a full meter is never wasted.")]
        private AttackDefinition _empoweredSpecialAttack;

        [SerializeField]
        [Tooltip("Strike used to punish a broken guard.")]
        private AttackDefinition _riposteAttack;

        [Header("Defence")]
        [SerializeField]
        [Tooltip("How this weapon answers an incoming attack.")]
        private DefenceStyle _defence = DefenceStyle.Deflect;

        [SerializeField]
        [Range(0.05f, 0.4f)]
        [Tooltip("Deflect window. Shorter is more demanding and, by design, more rewarding.")]
        private float _deflectWindowSeconds = 0.15f;

        [SerializeField]
        [Tooltip("Posture dealt to the attacker by a clean deflect with this weapon.")]
        private float _deflectPostureDamage = 24f;

        [Header("Handling")]
        [SerializeField]
        [Range(0.5f, 1.5f)]
        [Tooltip("Multiplier on movement speed while this weapon is drawn. Heavier weapons slow you.")]
        private float _moveSpeedMultiplier = 1f;

        [SerializeField]
        [Range(0.5f, 2f)]
        [Tooltip("Multiplier on stamina costs. Heavier weapons cost more per swing.")]
        private float _staminaCostMultiplier = 1f;

        /// <summary>拔出武器时显示的名称。</summary>
        public string DisplayName => _displayName;

        /// <summary>此武器攻击叠加效果使用的颜色。</summary>
        public Color SignatureColor => _signatureColor;

        /// <summary>
        /// 此武器挥击时播放的音效标识；为空则使用通用音效。
        /// </summary>
        /// <remarks>
        /// 采用普通字符串而不是片段引用，因为 Combat 在程序集依赖图中位于音频服务下层，
        /// 不能依赖音频服务。危险区域自身的音效标识也采用相同的安排。
        /// </remarks>
        public string SwingCueId => _swingCueId;

        /// <summary>拔出时显示在手中的可选模型；为 null 则不显示。</summary>
        public GameObject ModelPrefab => _modelPrefab;

        /// <summary>按执行顺序排列的轻攻击连段。</summary>
        public AttackDefinition[] LightComboChain => _lightComboChain;

        /// <summary>重攻击。</summary>
        public AttackDefinition HeavyAttack => _heavyAttack;

        /// <summary>特殊技能攻击。</summary>
        public AttackDefinition SpecialAttack => _specialAttack;

        /// <summary>专注值满时使用的强化特殊攻击；为 null 则复用普通特殊攻击。</summary>
        public AttackDefinition EmpoweredSpecialAttack => _empoweredSpecialAttack;

        /// <summary>用于惩罚防御破坏的打击。</summary>
        public AttackDefinition RiposteAttack => _riposteAttack;

        /// <summary>此武器应对来袭攻击的方式。</summary>
        public DefenceStyle Defence => _defence;

        /// <summary>弹反窗口，单位为秒。</summary>
        public float DeflectWindowSeconds => _deflectWindowSeconds;

        /// <summary>成功弹反造成的姿态伤害。</summary>
        public float DeflectPostureDamage => _deflectPostureDamage;

        /// <summary>拔出时的移动速度倍率。</summary>
        public float MoveSpeedMultiplier => _moveSpeedMultiplier;

        /// <summary>拔出时的耐力消耗倍率。</summary>
        public float StaminaCostMultiplier => _staminaCostMultiplier;

        /// <summary>此武器能够弹反时为 true。</summary>
        public bool CanDeflect => _defence != DefenceStyle.HyperArmour;
    }
}
