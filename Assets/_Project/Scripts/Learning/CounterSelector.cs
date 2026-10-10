using System.Collections.Generic;
using AdaptiveBossArena.Core.Services;

namespace AdaptiveBossArena.Learning
{
    /// <summary>
    /// 选择首领针对已观察习惯形成的应对。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 对合格策略进行加权随机选择，而非“最强习惯优先”。若习惯与反制之间是确定映射，就会变成查找表，
    /// 玩家掌握后可以刻意引导首领。加权让强烈习惯很可能得到应对，但不会确定采用哪一种。
    /// </para>
    /// <para>
    /// 每个候选的权重是设计权重乘以触发习惯的可信度。这可以防止首领针对偶然行为反制：
    /// 只观察两次便触发的策略，与观察三十次才触发的策略相比，参与竞争的权重仅为后者的一部分。
    /// </para>
    /// <para>
    /// 随机性来自注入的提供者，因此种子可以精确复现首领的决策；即使系统具有概率性，也依然可测试。
    /// </para>
    /// </remarks>
    public sealed class CounterSelector
    {
        private readonly IRandomProvider _random;
        private readonly List<CounterStrategy> _candidates = new List<CounterStrategy>();
        private readonly List<float> _weights = new List<float>();

        /// <summary>创建选择器。</summary>
        /// <param name="random">可设置种子的随机源，使选择结果可复现。</param>
        public CounterSelector(IRandomProvider random) => _random = random;

        /// <summary>最近一次选择时符合条件的策略，公开给调试叠加层。</summary>
        public IReadOnlyList<CounterStrategy> LastCandidates => _candidates;

        /// <summary>
        /// 选择要采纳的策略；没有值得采纳的策略时返回 null。
        /// </summary>
        /// <param name="catalogue">首领能够形成的全部策略。</param>
        /// <param name="profile">首领当前的玩家画像。</param>
        /// <param name="currentPhase">当前阶段，从一开始编号。</param>
        /// <param name="alreadyAdopted">已经生效的策略，除非允许重复，否则跳过。</param>
        /// <returns>选中的策略，或 null。</returns>
        public CounterStrategy Select(
            IReadOnlyList<CounterStrategy> catalogue,
            BehaviorProfile profile,
            int currentPhase,
            ICollection<CounterStrategy> alreadyAdopted)
        {
            GatherCandidates(catalogue, profile, currentPhase, alreadyAdopted);

            if (_candidates.Count == 0)
            {
                return null;
            }

            return PickWeighted();
        }

        /// <summary>收集当前满足触发条件的全部策略。</summary>
        private void GatherCandidates(
            IReadOnlyList<CounterStrategy> catalogue,
            BehaviorProfile profile,
            int currentPhase,
            ICollection<CounterStrategy> alreadyAdopted)
        {
            _candidates.Clear();
            _weights.Clear();

            if (catalogue == null)
            {
                return;
            }

            for (int i = 0; i < catalogue.Count; i++)
            {
                CounterStrategy strategy = catalogue[i];

                if (strategy == null)
                {
                    continue;
                }

                if (!strategy.IsRepeatable && alreadyAdopted != null && alreadyAdopted.Contains(strategy))
                {
                    continue;
                }

                FeatureReading reading = profile.Get(strategy.TriggerFeature);

                if (!strategy.IsEligible(reading.Value, reading.Confidence, currentPhase))
                {
                    continue;
                }

                _candidates.Add(strategy);

                // 可信度不只作为门槛，还会缩放权重，因此多个应对都合格时，
                // 首领更倾向于证据最充分的一个。
                _weights.Add(strategy.SelectionWeight * reading.Confidence);
            }
        }

        /// <summary>按权重比例抽取一个候选。</summary>
        private CounterStrategy PickWeighted()
        {
            float total = 0f;
            for (int i = 0; i < _weights.Count; i++)
            {
                total += _weights[i];
            }

            if (total <= 0f)
            {
                return _candidates[_random.NextInt(0, _candidates.Count)];
            }

            float roll = _random.NextFloat(0f, total);
            float cumulative = 0f;

            for (int i = 0; i < _candidates.Count; i++)
            {
                cumulative += _weights[i];

                if (roll <= cumulative)
                {
                    return _candidates[i];
                }
            }

            // 浮点累加误差可能使抽样值略微超过总权重。
            return _candidates[_candidates.Count - 1];
        }
    }
}
