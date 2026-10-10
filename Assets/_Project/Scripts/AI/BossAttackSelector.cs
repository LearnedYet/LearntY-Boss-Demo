using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Core.Services;
using AdaptiveBossArena.Learning;
using UnityEngine;

namespace AdaptiveBossArena.AI
{
    /// <summary>
    /// 选择 Boss 下一次使用的攻击。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 选择采用加权随机，而非规则链。若 Boss总是在指定距离使用指定攻击，它就成了查找表，
    /// 记住表的玩家便无需再思考。权重使距离强烈影响选择，却从不完全决定选择。
    /// </para>
    /// <para>
    /// 这里也让适应体现在 Boss 做什么，而不只是做得多快。学会玩家放风筝的 Boss 会提高突进攻击权重；
    /// 学会玩家总是闪避的 Boss则更偏向覆盖地面的攻击，而非精确瞄准。
    /// </para>
    /// </remarks>
    public sealed class BossAttackSelector
    {
        /// <summary>权重下限，防止当前阶段攻击目录中的任何攻击完全不可能选中。</summary>
        private const float MinimumWeight = 0.05f;

        /// <summary>攻击范围恰好匹配当前距离时的权重。</summary>
        private const float IdealRangeWeight = 1f;

        /// <summary>攻击范围与距离不匹配时，权重下降的幅度。</summary>
        private const float RangeFalloff = 0.6f;

        /// <summary>阶段标志攻击的额外权重，使各阶段拥有独特面貌。</summary>
        /// <remarks>
        /// 这是加成而非强制覆盖：标志攻击明显更容易出现，但绝不保证，因此能形成阶段威胁，
        /// 却不会成为玩家可按节拍器预测的固定节奏。独特预警让提高频率表现为“它又来了”。
        /// </remarks>
        private const float SignatureWeightBonus = 1.1f;

        private readonly IRandomProvider _random;
        private float[] _weights = new float[8];

        /// <summary>创建选择器。</summary>
        /// <param name="random">可设置种子的随机源，使种子能够复现同一战斗。</param>
        public BossAttackSelector(IRandomProvider random) => _random = random;

        /// <summary>
        /// 从当前阶段攻击目录中选择攻击。
        /// </summary>
        /// <param name="context">提供距离、调参和当前阶段的 Boss 上下文。</param>
        /// <returns>选中的攻击，阶段没有可用攻击时为 null。</returns>
        public AttackDefinition Select(BossContext context)
        {
            AttackDefinition[] catalogue = context.CurrentPhase.Attacks;

            if (catalogue == null || catalogue.Length == 0)
            {
                return null;
            }

            EnsureWeightCapacity(catalogue.Length);

            float totalWeight = ScoreCatalogue(context, catalogue);

            return totalWeight <= 0f
                ? catalogue[_random.NextInt(0, catalogue.Length)]
                : PickWeighted(catalogue, totalWeight);
        }

        /// <summary>为目录中全部攻击评分，并返回权重总和。</summary>
        private float ScoreCatalogue(BossContext context, AttackDefinition[] catalogue)
        {
            float distance = context.DistanceToPlayer;
            float gapCloserBias = context.Tuning.Get(BossTuningParameter.GapCloserWeight);
            float areaDenialBias = context.Tuning.Get(BossTuningParameter.AreaDenialWeight);
            float unblockableBias = context.Tuning.Get(BossTuningParameter.UnblockableWeight);
            AttackDefinition signature = context.CurrentPhase.SignatureAttack;

            float total = 0f;

            for (int i = 0; i < catalogue.Length; i++)
            {
                AttackDefinition attack = catalogue[i];

                if (attack == null)
                {
                    _weights[i] = 0f;
                    continue;
                }

                float weight = RangeSuitability(attack, distance);
                weight += gapCloserBias * GapCloserAffinity(attack);
                weight += areaDenialBias * AreaCoverageAffinity(attack);
                weight += unblockableBias * UnblockableAffinity(attack);

                if (signature != null && attack == signature)
                {
                    weight += SignatureWeightBonus;
                }

                _weights[i] = Mathf.Max(MinimumWeight, weight);
                total += _weights[i];
            }

            return total;
        }

        /// <summary>
        /// 攻击范围与当前距离的匹配程度。
        /// </summary>
        /// <remarks>
        /// 突进攻击把突进距离计入有效范围，因此同样射程的静止攻击评分很低时，
        /// 接近距离的突进攻击仍会获得良好评分。
        /// </remarks>
        private static float RangeSuitability(AttackDefinition attack, float distance)
        {
            float effectiveReach = attack.EffectiveReach;
            float mismatch = Mathf.Abs(effectiveReach - distance);

            return Mathf.Max(0f, IdealRangeWeight - mismatch * RangeFalloff);
        }

        /// <summary>攻击具有突进特征的程度。</summary>
        private static float GapCloserAffinity(AttackDefinition attack) =>
            attack.LungeSpeed > 0f ? 1f : 0f;

        /// <summary>
        /// 攻击是否能直接穿过举起的防御。
        /// </summary>
        /// <remarks>
        /// 与突进倾向一样采用二元判断：攻击要么必须闪避，要么不必。面对总以防御应对的玩家，
        /// 这会让习惯成为弱点；它仍公平，因为危险攻击是 Boss 预警最明显的攻击。
        /// </remarks>
        private static float UnblockableAffinity(AttackDefinition attack) =>
            attack.Unblockable ? 1f : 0f;

        /// <summary>攻击覆盖地面的程度，偏向宽弧和较长距离。</summary>
        private static float AreaCoverageAffinity(AttackDefinition attack)
        {
            float arcFraction = Mathf.Clamp01(attack.ArcDegrees / 360f);
            float reachFraction = Mathf.Clamp01(attack.Range / 8f);

            return (arcFraction + reachFraction) * 0.5f;
        }

        /// <summary>按评分权重比例抽取攻击。</summary>
        private AttackDefinition PickWeighted(AttackDefinition[] catalogue, float totalWeight)
        {
            float roll = _random.NextFloat(0f, totalWeight);
            float cumulative = 0f;

            for (int i = 0; i < catalogue.Length; i++)
            {
                cumulative += _weights[i];

                if (roll <= cumulative && catalogue[i] != null)
                {
                    return catalogue[i];
                }
            }

            // 浮点累加可能使随机值略超总和。回退到最后一个有效项是正确且无额外成本的处理。
            for (int i = catalogue.Length - 1; i >= 0; i--)
            {
                if (catalogue[i] != null)
                {
                    return catalogue[i];
                }
            }

            return null;
        }

        /// <summary>阶段攻击数量增加时扩展可复用权重缓冲区。</summary>
        private void EnsureWeightCapacity(int required)
        {
            if (_weights.Length < required)
            {
                _weights = new float[required];
            }
        }
    }
}
