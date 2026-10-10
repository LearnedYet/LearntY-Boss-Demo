using System;
using AdaptiveBossArena.Core.Constants;
using AdaptiveBossArena.Core.ScriptableObjects;
using UnityEngine;

namespace AdaptiveBossArena.Learning
{
    /// <summary>反制策略对首领行为的一项调整。</summary>
    [Serializable]
    public struct TuningAdjustment
    {
        [SerializeField]
        [Tooltip("Behaviour to adjust.")]
        private BossTuningParameter _parameter;

        [SerializeField]
        [Tooltip("Value the parameter is eased toward while this strategy is active.")]
        private float _targetValue;

        /// <summary>被调整的行为。</summary>
        public BossTuningParameter Parameter => _parameter;

        /// <summary>参数平滑趋近的目标值。</summary>
        public float TargetValue => _targetValue;
    }

    /// <summary>
    /// 首领针对一项已观察习惯形成的应对。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 策略以资源形式编写，使首领招式库成为设计配置，而非代码分支。
    /// 增加“针对固定节奏闪避的玩家开始佯攻”，只需新增资源，无需在 AI 中增加 <c>if</c>。
    /// </para>
    /// <para>
    /// 选择采用加权概率，而非确定映射。若最强习惯总是对应相同反制，玩家就会学会首领的查找表，
    /// 战斗退化成记忆流程，而这正是此设计要避免的问题。
    /// </para>
    /// <para>
    /// 面向玩家的提示并非装饰。玩家察觉不到的适应与首领作弊难以区分，因此每项策略都必须宣告自身。
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "CounterStrategy",
        menuName = "Adaptive Boss Arena/Learning/Counter Strategy",
        order = 0)]
    public sealed class CounterStrategy : IdentifiableSO
    {
        [Header("Trigger")]
        [SerializeField]
        [Tooltip("Player habit this strategy answers.")]
        private BehaviorFeature _triggerFeature = BehaviorFeature.AttackFrequency;

        [SerializeField]
        [Tooltip("Direction of the comparison against the threshold.")]
        private FeatureComparison _comparison = FeatureComparison.AtOrAbove;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Feature value at which this strategy becomes eligible.")]
        private float _threshold = 0.6f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Confidence the feature must reach before this strategy may be chosen. Guards " +
                 "against the boss reacting to a coincidence.")]
        private float _minimumConfidence = 0.5f;

        [Header("Selection")]
        [SerializeField]
        [Range(0.1f, 5f)]
        [Tooltip("Relative likelihood of being chosen when several strategies are eligible.")]
        private float _selectionWeight = 1f;

        [SerializeField]
        [Range(1, GameplayConstants.BossPhaseCount)]
        [Tooltip("Earliest boss phase in which this strategy may be adopted. The ceiling is the " +
                 "phase count, so a strategy may be reserved for Last Stand.")]
        private int _minimumPhase = 1;

        [SerializeField]
        [Tooltip("Whether this strategy may be adopted more than once per fight.")]
        private bool _repeatable;

        [Header("Effect")]
        [SerializeField]
        [Tooltip("Behaviour adjustments applied while this strategy is active.")]
        private TuningAdjustment[] _adjustments = new TuningAdjustment[0];

        [SerializeField]
        [Range(1f, 20f)]
        [Tooltip("Time over which the adjustments are eased in. Instant changes read as cheating; " +
                 "gradual ones read as the boss working the player out.")]
        private float _easeInSeconds = 6f;

        [Header("Player-Facing Tell")]
        [SerializeField]
        [Tooltip("Short line shown when this strategy is adopted, so the player can perceive that " +
                 "the boss has changed. Example: 'It has stopped falling for your opening.'")]
        private string _tellMessage = string.Empty;

        [SerializeField]
        [Tooltip("Audio cue identifier played alongside the tell.")]
        private string _tellAudioCueId = string.Empty;

        /// <summary>此策略应对的玩家习惯。</summary>
        public BehaviorFeature TriggerFeature => _triggerFeature;

        /// <summary>阈值比较的方向。</summary>
        public FeatureComparison Comparison => _comparison;

        /// <summary>策略获得选择资格所需的特征值。</summary>
        public float Threshold => _threshold;

        /// <summary>选择此策略所需的可信度。</summary>
        public float MinimumConfidence => _minimumConfidence;

        /// <summary>在合格策略中被选中的相对概率。</summary>
        public float SelectionWeight => _selectionWeight;

        /// <summary>最早可以采纳此策略的首领阶段。</summary>
        public int MinimumPhase => _minimumPhase;

        /// <summary>是否允许每场战斗多次采纳此策略。</summary>
        public bool IsRepeatable => _repeatable;

        /// <summary>此策略生效时应用的行为调整。</summary>
        public TuningAdjustment[] Adjustments => _adjustments;

        /// <summary>调整平滑生效的时间。</summary>
        public float EaseInSeconds => _easeInSeconds;

        /// <summary>采纳此策略时向玩家展示的文字。</summary>
        public string TellMessage => _tellMessage;

        /// <summary>采纳此策略时播放的音效提示。</summary>
        public string TellAudioCueId => _tellAudioCueId;

        /// <summary>
        /// 检查观察特征是否满足此策略的触发条件。
        /// </summary>
        /// <param name="featureValue">触发特征的观察值，范围为零至一。</param>
        /// <param name="confidence">该观察的可信度，范围为零至一。</param>
        /// <param name="currentPhase">当前首领阶段。</param>
        /// <returns>策略具有选择资格时为 true。</returns>
        public bool IsEligible(float featureValue, float confidence, int currentPhase)
        {
            if (currentPhase < _minimumPhase || confidence < _minimumConfidence)
            {
                return false;
            }

            return _comparison == FeatureComparison.AtOrAbove
                ? featureValue >= _threshold
                : featureValue <= _threshold;
        }
    }
}
