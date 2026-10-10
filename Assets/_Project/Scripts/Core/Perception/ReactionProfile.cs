using AdaptiveBossArena.Core.ScriptableObjects;
using AdaptiveBossArena.Core.Services;
using UnityEngine;

namespace AdaptiveBossArena.Core.Perception
{
    /// <summary>
    /// 应用于 Boss 每次决策的人类合理性限制。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 仅限制可见信息是必要但不充分的。无延迟且总是准确地响应可见信息，仍然像作弊。
    /// 此资源提供另外两个要素：感知事件后需等待的反应延迟，以及让相同场景不总产生相同响应的变化。
    /// </para>
    /// <para>
    /// 默认值参考人类反应研究：简单视觉刺激约需250 毫秒，需要选择响应时更长。
    /// Boss后续阶段只接近该范围的快端，而不低于它，确保难度升级不会进入超人范围。
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "ReactionProfile",
        menuName = "Adaptive Boss Arena/AI/Reaction Profile",
        order = 0)]
    public sealed class ReactionProfile : IdentifiableSO
    {
        [Header("Perception")]
        [SerializeField]
        [Range(0.05f, 0.5f)]
        [Tooltip("How stale the player observations handed to the AI are. This is what makes feints " +
                 "and baits genuinely work against the boss.")]
        private float _perceptionLatencySeconds = 0.14f;

        [Header("Reaction")]
        [SerializeField]
        [Range(0.1f, 1f)]
        [Tooltip("Fastest the boss may respond to a perceived event.")]
        private float _minReactionSeconds = 0.22f;

        [SerializeField]
        [Range(0.1f, 1.5f)]
        [Tooltip("Slowest the boss will take to respond to a perceived event.")]
        private float _maxReactionSeconds = 0.42f;

        [Header("Fallibility")]
        [SerializeField]
        [Range(0f, 0.5f)]
        [Tooltip("Chance the boss simply fails to act on a perceived opening, as a human would. " +
                 "Set to zero only for a deliberately mechanical boss.")]
        private float _missedOpportunityChance = 0.12f;

        /// <summary>AI 看到玩家观察前，对全部观察施加的延迟。</summary>
        public float PerceptionLatencySeconds => _perceptionLatencySeconds;

        /// <summary>允许的最快反应秒数。</summary>
        public float MinReactionSeconds => _minReactionSeconds;

        /// <summary>允许的最慢反应秒数。</summary>
        public float MaxReactionSeconds => Mathf.Max(_maxReactionSeconds, _minReactionSeconds);

        /// <summary>Boss 忽略一个本可惩罚的空当的概率。</summary>
        public float MissedOpportunityChance => _missedOpportunityChance;

        /// <summary>为单次决策抽取反应延迟。</summary>
        /// <param name="random">可设置种子的随机源，保证测试确定性。</param>
        /// <returns>配置范围内的延迟秒数。</returns>
        public float RollReactionDelay(IRandomProvider random) =>
            random.NextFloat(MinReactionSeconds, MaxReactionSeconds);

        /// <summary>抽取 Boss 此次是否忽略空当。</summary>
        /// <param name="random">可设置种子的随机源。</param>
        /// <returns>应忽略该空当时为 true。</returns>
        public bool RollMissedOpportunity(IRandomProvider random) =>
            random.NextFloat01() < _missedOpportunityChance;

#if UNITY_EDITOR
        /// <inheritdoc />
        protected override void OnValidate()
        {
            base.OnValidate();

            // 最小值大于最大值的配置会无声地反转反应范围。
            if (_maxReactionSeconds < _minReactionSeconds)
            {
                _maxReactionSeconds = _minReactionSeconds;
            }
        }
#endif
    }
}
