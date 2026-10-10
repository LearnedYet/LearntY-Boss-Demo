using UnityEngine;

namespace AdaptiveBossArena.Learning
{
    /// <summary>
    /// 读取当前局势，与长期玩家习惯区分开。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 刻意与 <see cref="BehaviorProfile"/> 分开，因为两者回答不同问题：画像描述玩家通常怎么做，
    /// 这里描述战斗当前进行得如何。只有画像的首领可能因玩家通常被动而在劣势交锋中仍继续追击；
    /// 只有局势判断的首领则永远无法学习。
    /// </para>
    /// <para>
    /// 读取的信息全部可见：两条生命条、交战距离，以及玩家表面上正在做的动作。不查询隐藏状态。
    /// </para>
    /// </remarks>
    public sealed class ThreatEvaluator
    {
        /// <summary>首领生命低于此比例时，视为自身处境危险。</summary>
        private const float DesperationThreshold = 0.3f;

        /// <summary>归一化时视为竞技场最远距离的数值。</summary>
        private const float MaxMeaningfulDistance = 14f;

        /// <summary>
        /// 评分当前交锋对首领的有利程度，范围为零至一。
        /// </summary>
        /// <param name="bossHealthNormalized">首领生命占最大值的比例。</param>
        /// <param name="playerHealthNormalized">从生命条读取的玩家生命比例。</param>
        /// <returns>严重落后时为零，明显领先时为一。</returns>
        public float EvaluateAdvantage(float bossHealthNormalized, float playerHealthNormalized) =>
            Mathf.Clamp01((bossHealthNormalized - playerHealthNormalized) * 0.5f + 0.5f);

        /// <summary>
        /// 评分首领应有多希望缩短距离，范围为零至一。
        /// </summary>
        /// <remarks>
        /// 结合原始距离与学习得到的进攻性，因此识破玩家保持距离牵制习惯的首领，
        /// 会比未识破时更急于缩短相同距离。
        /// </remarks>
        /// <param name="distance">当前间距。</param>
        /// <param name="learnedAggression">进攻性行为参数。</param>
        /// <returns>缩短距离的紧迫程度，范围为零至一。</returns>
        public float EvaluateClosingUrgency(float distance, float learnedAggression)
        {
            float distanceFactor = Mathf.Clamp01(distance / MaxMeaningfulDistance);
            return Mathf.Clamp01(distanceFactor * Mathf.Lerp(0.5f, 1.5f, Mathf.Clamp01(learnedAggression)));
        }

        /// <summary>
        /// 首领生命足够低、应以破釜沉舟的方式战斗时为 true。
        /// </summary>
        /// <param name="bossHealthNormalized">首领生命占最大值的比例。</param>
        /// <returns>首领是否处于绝境。</returns>
        public bool IsDesperate(float bossHealthNormalized) =>
            bossHealthNormalized <= DesperationThreshold;

        /// <summary>
        /// 玩家看起来投入某项动作、难以轻易应对反击时为 true。
        /// </summary>
        /// <remarks>
        /// 这是首领直接采取行动的唯一局势判断，与其他信息一样来自延迟观察。
        /// 它注意到重攻击或治疗已持续一段时间，这也是认真观察的对手能够发现的信息。
        /// </remarks>
        /// <param name="observation">延迟后的玩家快照。</param>
        /// <param name="minimumCommitmentSeconds">动作必须已经持续的时间。</param>
        /// <returns>是否看起来存在可利用的破绽。</returns>
        public bool LooksCommitted(
            Core.Perception.PlayerObservation observation,
            float minimumCommitmentSeconds = 0.15f)
        {
            if (!observation.IsValid)
            {
                return false;
            }

            // 腾空也算：被抛起的身体无法闪避或防御，任何观察者都能看到。倒地则不算，虽然同样无法行动，
            // 但此时无法受到伤害；若将其视为破绽，首领就会依据规则攻击，而非通过空挥发现自己无法获利。
            bool isCommittedAction =
                observation.ActionState == Core.Perception.ObservableActionState.HeavyAttacking ||
                observation.ActionState == Core.Perception.ObservableActionState.Healing ||
                observation.ActionState == Core.Perception.ObservableActionState.Staggered ||
                observation.ActionState == Core.Perception.ObservableActionState.Airborne;

            return isCommittedAction && observation.TimeInActionState >= minimumCommitmentSeconds;
        }
    }
}
