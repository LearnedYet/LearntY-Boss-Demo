using System;
using AdaptiveBossArena.Utilities;
using UnityEngine;

namespace AdaptiveBossArena.Learning
{
    /// <summary>
    /// 描述首领当前战斗方式的可变数值。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 学习通过这些参数改变行为。状态机读取数值，适应系统写入数值，两者互不了解，
    /// 因此反制策略无需理解首领状态，状态也无需理解玩家习惯。
    /// </para>
    /// <para>
    /// 数值平滑趋近目标，而非直接赋值。瞬间变化像是首领突然被告知信息；
    /// 经过数秒的变化则像首领逐渐得出结论。平滑决定了机制看起来聪明还是像作弊，而实现只需一行。
    /// </para>
    /// <para>
    /// 未被强化时，每项参数也会向基线衰减。这让改变战术成为应对反制的有效办法，
    /// 而非因为曾有某个习惯就受到永久惩罚。
    /// </para>
    /// </remarks>
    public sealed class BossTuning
    {
        private static readonly int ParameterCount = Enum.GetValues(typeof(BossTuningParameter)).Length;

        /// <summary>
        /// 投入相关参数超过基线的总偏离达到此值时，首领视为完全投入。
        /// 该值使采纳进攻型反制策略的首领接近一，而没有学到任何习惯的战斗保持为零。
        /// </summary>
        private const float CommitmentReferenceSum = 1.6f;

        private readonly float[] _current;
        private readonly float[] _target;
        private readonly float[] _baseline;

        /// <summary>创建处于基线值的行为参数集。</summary>
        /// <param name="baselinePreferredRange">首领尚未学习时保持的距离。</param>
        /// <param name="baselineAggression">首领尚未学习时的进攻意愿。</param>
        /// <param name="baselineParryChance">
        /// 首领尚未学习时，以招架应对重攻击的倾向。较小但非零，确保双向攻防从开场就存在，
        /// 而非成为多数战斗都看不到的机制；面对依赖重攻击的玩家，学习循环仍会显著提高该值，
        /// 使适应保持可见。
        /// </param>
        public BossTuning(
            float baselinePreferredRange, float baselineAggression, float baselineParryChance = 0f)
        {
            _current = new float[ParameterCount];
            _target = new float[ParameterCount];
            _baseline = new float[ParameterCount];

            SetBaseline(BossTuningParameter.PreferredRange, baselinePreferredRange);
            SetBaseline(BossTuningParameter.Aggression, baselineAggression);
            SetBaseline(BossTuningParameter.ParryChance, baselineParryChance);

            // 其他参数均从零开始：首领开场时尚未启用其余反制行为，必须逐一通过学习获得。
            for (int i = 0; i < ParameterCount; i++)
            {
                _current[i] = _baseline[i];
                _target[i] = _baseline[i];
            }
        }

        /// <summary>
        /// 首领当前投入追击玩家的强度，范围为 0..1。
        /// </summary>
        /// <remarks>
        /// 三个投入参数超过基线的平滑偏离量之和，经过归一化：
        /// 进攻意愿（<see cref="BossTuningParameter.Aggression"/>）、
        /// 突进倾向（<see cref="BossTuningParameter.GapCloserWeight"/>）和延长连段倾向（
        /// <see cref="BossTuningParameter.ComboExtensionChance"/>）；这些值均高于各自基线。
        /// <see cref="OverbalanceEvaluator"/> 读取此量：首领越是坚信某种判断，空挥的代价越高。
        /// 未发生任何适应时它为零，因此不会影响基础战斗。
        /// </remarks>
        public float CommitmentIntensity
        {
            get
            {
                float committed =
                    PositiveDeviation(BossTuningParameter.Aggression) +
                    PositiveDeviation(BossTuningParameter.GapCloserWeight) +
                    PositiveDeviation(BossTuningParameter.ComboExtensionChance);

                return Mathf.Clamp01(committed / CommitmentReferenceSum);
            }
        }

        /// <summary>读取参数当前值。</summary>
        /// <param name="parameter">要读取的参数。</param>
        /// <returns>正在向目标靠近的当前值。</returns>
        public float Get(BossTuningParameter parameter) => _current[(int)parameter];

        /// <summary>读取参数当前平滑趋近的目标值。</summary>
        /// <param name="parameter">要读取的参数。</param>
        /// <returns>目标值。</returns>
        public float GetTarget(BossTuningParameter parameter) => _target[(int)parameter];

        /// <summary>读取参数尚未学习时的基准值。</summary>
        /// <param name="parameter">要读取的参数。</param>
        /// <returns>基线值。</returns>
        public float GetBaseline(BossTuningParameter parameter) => _baseline[(int)parameter];

        /// <summary>设置参数应平滑趋近的目标值。</summary>
        /// <param name="parameter">要调整的参数。</param>
        /// <param name="value">要趋近的值。</param>
        public void SetTarget(BossTuningParameter parameter, float value) =>
            _target[(int)parameter] = value;

        /// <summary>定义参数的基准值。</summary>
        /// <param name="parameter">要配置的参数。</param>
        /// <param name="value">尚未学习时的值。</param>
        public void SetBaseline(BossTuningParameter parameter, float value) =>
            _baseline[(int)parameter] = value;

        /// <summary>
        /// 让每个参数平滑趋近目标，并让每个目标回归基线。
        /// </summary>
        /// <param name="deltaTime">经过缩放的时间增量。</param>
        /// <param name="easeHalfLifeSeconds">
        /// 与目标的差距缩小一半所需的时间。更大的数值使适应更渐进，从而更像首领行为逐渐变化，
        /// 而非突然跳变。
        /// </param>
        /// <param name="forgetHalfLifeSeconds">
        /// 未被强化的目标回到基线一半距离所需的时间。
        /// </param>
        public void Tick(float deltaTime, float easeHalfLifeSeconds, float forgetHalfLifeSeconds)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            for (int i = 0; i < ParameterCount; i++)
            {
                _target[i] = MathUtil.Damp(_target[i], _baseline[i], forgetHalfLifeSeconds, deltaTime);
                _current[i] = MathUtil.Damp(_current[i], _target[i], easeHalfLifeSeconds, deltaTime);
            }
        }

        /// <summary>立即将全部参数恢复到基线，供战斗重试使用。</summary>
        public void ResetToBaseline()
        {
            for (int i = 0; i < ParameterCount; i++)
            {
                _current[i] = _baseline[i];
                _target[i] = _baseline[i];
            }
        }

        /// <summary>
        /// 报告参数偏离基线的距离，供调试叠加层使用。
        /// </summary>
        /// <param name="parameter">要检查的参数。</param>
        /// <returns>与基线之间的绝对距离。</returns>
        public float DeviationFromBaseline(BossTuningParameter parameter) =>
            Mathf.Abs(_current[(int)parameter] - _baseline[(int)parameter]);

        /// <summary>参数平滑到基线<em>之上</em>的距离，不会为负。</summary>
        /// <param name="parameter">要检查的参数。</param>
        /// <returns>高于基线的距离；等于或低于基线时为零。</returns>
        private float PositiveDeviation(BossTuningParameter parameter) =>
            Mathf.Max(0f, _current[(int)parameter] - _baseline[(int)parameter]);
    }
}
