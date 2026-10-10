using System;
using AdaptiveBossArena.Utilities;
using UnityEngine;

namespace AdaptiveBossArena.Learning
{
    /// <summary>一项测得的习惯，以及首领应信任该测量的程度。</summary>
    /// <remarks>
    /// 可信度并非装饰。观察两次与观察三十次的习惯可能得到相同数值；若同等对待，
    /// 首领就会针对偶然行为反制。每项策略都用它缩放权重，因此弱证据略微影响决策，强证据主导决策。
    /// </remarks>
    public readonly struct FeatureReading
    {
        /// <summary>测得的习惯强度，范围为零至一。</summary>
        public float Value { get; init; }

        /// <summary>支持该测量的证据充分程度，范围为零至一。</summary>
        public float Confidence { get; init; }

        /// <summary>该测量依据的观察次数。</summary>
        public int SampleCount { get; init; }

        /// <summary>按可信度加权的值，用于比较习惯的强弱。</summary>
        public float Weighted => Value * Confidence;
    }

    /// <summary>
    /// 首领当前对玩家战斗方式的画像。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 按固定间隔重新计算，而非连续计算，供反制策略选择器读取。它是趋势快照，而非实时数据流，
    /// 因此首领不会针对玩家的某个单独动作立即反应。
    /// </para>
    /// <para>
    /// 所有数据都来自首领能够合理观察到的事件。人类对手认真观察一分钟，也能推断出画像中的这些信息。
    /// </para>
    /// </remarks>
    public sealed class BehaviorProfile
    {
        private static readonly int FeatureCount = Enum.GetValues(typeof(BehaviorFeature)).Length;

        private readonly FeatureReading[] _readings;

        /// <summary>创建空画像。</summary>
        public BehaviorProfile() => _readings = new FeatureReading[FeatureCount];

        /// <summary>上次重新计算画像时的战斗时钟时间。</summary>
        public float LastUpdatedAt { get; private set; }

        /// <summary>读取一项习惯。</summary>
        /// <param name="feature">要读取的习惯。</param>
        /// <returns>该习惯最近的测量值。</returns>
        public FeatureReading Get(BehaviorFeature feature) => _readings[(int)feature];

        /// <summary>记录一次测量。</summary>
        /// <param name="feature">测量的习惯。</param>
        /// <param name="value">习惯强度，限制在零至一之间。</param>
        /// <param name="sampleCount">该测量依据的观察次数。</param>
        /// <param name="halfConfidenceSampleCount">
        /// 可信度达到一半时所需的观察次数。需要更多证据才能采取行动的习惯，这里应使用更大的数值。
        /// </param>
        public void Set(
            BehaviorFeature feature,
            float value,
            int sampleCount,
            int halfConfidenceSampleCount)
        {
            _readings[(int)feature] = new FeatureReading
            {
                Value = Mathf.Clamp01(value),
                Confidence = MathUtil.ConfidenceFromSampleCount(sampleCount, halfConfidenceSampleCount),
                SampleCount = sampleCount
            };
        }

        /// <summary>标记画像刚刚完成重新计算。</summary>
        /// <param name="timestamp">重新计算时的战斗时钟时间。</param>
        public void MarkUpdated(float timestamp) => LastUpdatedAt = timestamp;

        /// <summary>
        /// 找出首领掌握证据最充分的习惯。
        /// </summary>
        /// <remarks>调试叠加层用它概括首领认为自己知道的信息。</remarks>
        /// <returns>按可信度加权后数值最高的习惯。</returns>
        public BehaviorFeature StrongestFeature()
        {
            var strongest = BehaviorFeature.AttackFrequency;
            float best = -1f;

            for (int i = 0; i < FeatureCount; i++)
            {
                float weighted = _readings[i].Weighted;

                if (weighted > best)
                {
                    best = weighted;
                    strongest = (BehaviorFeature)i;
                }
            }

            return strongest;
        }

        /// <summary>清除全部测量值，供战斗重试使用。</summary>
        public void Clear()
        {
            Array.Clear(_readings, 0, _readings.Length);
            LastUpdatedAt = 0f;
        }
    }
}
