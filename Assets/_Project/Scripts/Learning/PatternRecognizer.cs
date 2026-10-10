using UnityEngine;

namespace AdaptiveBossArena.Learning
{
    /// <summary>
    /// 将跟踪器的原始统计转为首领据以推理的习惯。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 按较慢间隔运行，而非逐帧运行。这种节奏是设计约束，而非性能优化：
    /// 持续重新推导画像的首领可能会针对单个动作作出反应，而整个遭遇战要求它响应趋势。
    /// </para>
    /// <para>
    /// 每项习惯都归一化到零至一，并附带由观察次数决定的可信度。
    /// 下面的半可信度样本数表达了各类结论所需证据量的判断：闪避方向偏好需要观察较多闪避才有意义，
    /// 而偏好距离通过少量采样就能看出。
    /// </para>
    /// <para>
    /// 此类刻意保持为纯逻辑：读取跟踪器与记忆，写入画像，不依赖任何引擎对象，
    /// 因此可以输入模拟战斗来检验首领的推断。
    /// </para>
    /// </remarks>
    public sealed class PatternRecognizer
    {
        private const int AttackSamplesForConfidence = 8;
        private const int DodgeSamplesForConfidence = 10;
        private const int DirectionSamplesForConfidence = 12;
        private const int DistanceSamplesForConfidence = 25;
        private const int EvasionSamplesForConfidence = 6;
        private const int HealSamplesForConfidence = 2;

        /// <summary>
        /// 信任防御画像之前所需的防御交锋次数。
        /// </summary>
        /// <remarks>
        /// 比闪避所需次数更高，因为防御应答是刻意、重复的选择，而非一次性反应；
        /// 同时它会引发不可格挡攻击和佯攻等最严厉的反制。首领应先确认判断。
        /// </remarks>
        private const int GuardSamplesForConfidence = 8;

        /// <summary>达到此治疗次数后，治疗被视为玩家的显著习惯。</summary>
        private const float HealFrequencyCeiling = 4f;

        /// <summary>
        /// 根据当前统计重新计算全部习惯。
        /// </summary>
        /// <param name="tracker">累计统计跟踪器。</param>
        /// <param name="memory">观察事件及其累计计数。</param>
        /// <param name="profile">接收推断结果的画像。</param>
        /// <param name="timeNow">记录到画像上的战斗时钟时间。</param>
        public void Analyse(
            BehaviorTracker tracker,
            CombatMemory memory,
            BehaviorProfile profile,
            float timeNow)
        {
            AnalyseOffence(tracker, memory, profile);
            AnalyseMovement(tracker, profile);
            AnalyseEvasion(tracker, memory, profile);
            AnalyseDefence(memory, profile);
            AnalyseSustain(memory, profile);

            profile.MarkUpdated(timeNow);
        }

        /// <summary>推导玩家的攻击方式。</summary>
        private static void AnalyseOffence(
            BehaviorTracker tracker,
            CombatMemory memory,
            BehaviorProfile profile)
        {
            profile.Set(
                BehaviorFeature.AttackFrequency,
                tracker.NormalizedAttackFrequency(),
                memory.PlayerAttacksThrown,
                AttackSamplesForConfidence);

            profile.Set(
                BehaviorFeature.HeavyAttackRatio,
                Ratio(memory.PlayerHeavyAttacksThrown, memory.PlayerAttacksThrown),
                memory.PlayerAttacksThrown,
                AttackSamplesForConfidence);

            // 较高空挥率表示玩家攻击的是首领先前而非当前的位置，这暴露了值得追击的破绽，
            // 而非需要尊重的威胁。
            profile.Set(
                BehaviorFeature.WhiffRatio,
                Ratio(memory.PlayerAttacksWhiffed, memory.PlayerAttacksThrown),
                memory.PlayerAttacksThrown,
                AttackSamplesForConfidence);

            // 设计明确指出的习惯：闪避刚结束就本能地攻击。
            profile.Set(
                BehaviorFeature.PostDodgeAggression,
                Ratio(tracker.PostDodgeAttacks, tracker.DodgeOpportunities),
                tracker.DodgeOpportunities,
                DodgeSamplesForConfidence);
        }

        /// <summary>推导玩家的移动方式和偏好站位。</summary>
        private static void AnalyseMovement(BehaviorTracker tracker, BehaviorProfile profile)
        {
            profile.Set(
                BehaviorFeature.PreferredDistance,
                tracker.NormalizedPreferredDistance(),
                tracker.DistanceSampleCount,
                DistanceSamplesForConfidence);

            profile.Set(
                BehaviorFeature.Aggression,
                tracker.NormalizedAggression(),
                tracker.DistanceSampleCount,
                DistanceSamplesForConfidence);

            // 可预测性已经是方向直方图的熵度量，因此可以直接作为习惯强度，无需进一步归一化。
            profile.Set(
                BehaviorFeature.DashDirectionBias,
                tracker.DashDirections.Predictability,
                Mathf.RoundToInt(tracker.DashDirections.TotalWeight),
                DirectionSamplesForConfidence);
        }

        /// <summary>推导玩家躲避首领攻击的水平。</summary>
        private static void AnalyseEvasion(
            BehaviorTracker tracker,
            CombatMemory memory,
            BehaviorProfile profile)
        {
            int bossAttacksResolved =
                memory.BossAttacksLanded + memory.BossAttacksEvaded + memory.BossAttacksWhiffed;

            // 首领对玩家难缠程度的总体判断。较高时最终会推动首领选择覆盖地面的攻击，
            // 而非精确瞄准的攻击。
            profile.Set(
                BehaviorFeature.EvasionSuccess,
                Ratio(memory.BossAttacksEvaded + memory.BossAttacksWhiffed, bossAttacksResolved),
                bossAttacksResolved,
                EvasionSamplesForConfidence);

            profile.Set(
                BehaviorFeature.DodgeTimingConsistency,
                Ratio(memory.PlayerPerfectDodges, tracker.DodgeOpportunities),
                tracker.DodgeOpportunities,
                DodgeSamplesForConfidence);
        }

        /// <summary>
        /// 推导玩家的防御方式：是否依靠防御站住原地，以及防御时机有多精准。
        /// </summary>
        /// <remarks>
        /// 这两项刻意分开判断。依赖程度以首领全部攻击为分母，回答“玩家是否站住格挡”；
        /// 技巧只以真正被防御接下的攻击为分母，回答“玩家是否擅长防御”。谨慎的新手前者高、后者低，
        /// 两类习惯应该分别用不可格挡攻击应对龟缩，用佯攻扰乱精准节奏。
        /// </remarks>
        private static void AnalyseDefence(CombatMemory memory, BehaviorProfile profile)
        {
            int guardedAnswers = memory.PlayerDeflects + memory.PlayerBlocks;

            int bossAttacksResolved =
                memory.BossAttacksLanded + memory.BossAttacksEvaded + memory.BossAttacksWhiffed +
                guardedAnswers;

            profile.Set(
                BehaviorFeature.GuardReliance,
                Ratio(guardedAnswers, bossAttacksResolved),
                bossAttacksResolved,
                GuardSamplesForConfidence);

            // 只依据已被防御接下的攻击，因此即便玩家接下两次攻击且全部弹反，也必须反复做到足够多次，
            // 才会被判断为高手。
            profile.Set(
                BehaviorFeature.DeflectSkill,
                Ratio(memory.PlayerDeflects, guardedAnswers),
                guardedAnswers,
                GuardSamplesForConfidence);
        }

        /// <summary>推导玩家主动脱离战斗恢复的倾向。</summary>
        private static void AnalyseSustain(CombatMemory memory, BehaviorProfile profile) =>
            profile.Set(
                BehaviorFeature.HealFrequency,
                Mathf.Clamp01(memory.PlayerHealsStarted / HealFrequencyCeiling),
                memory.PlayerHealsStarted,
                HealSamplesForConfidence);

        /// <summary>安全除法：无观察数据时返回零，不把数据缺失误当成习惯。</summary>
        private static float Ratio(int numerator, int denominator) =>
            denominator <= 0 ? 0f : Mathf.Clamp01((float)numerator / denominator);
    }
}
