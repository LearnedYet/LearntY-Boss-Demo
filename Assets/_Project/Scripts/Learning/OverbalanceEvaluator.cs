using UnityEngine;

namespace AdaptiveBossArena.Learning
{
    /// <summary>
    /// 首领自身投入的挥击落空时，决定是否失衡，以及失衡程度。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是“反向适应”的机制部分。首领识破习惯后投入追击，提高投入相关参数，
    /// 参见<see cref="BossTuning.CommitmentIntensity"/>。玩家改变习惯后，首领投入的攻击开始落空；
    /// 先前投入越大，空挥代价越高：更长的踉跄，以及短暂暴露、容易崩溃的姿态。识破首领对自身习惯的判断，
    /// 才能换来破绽。
    /// </para>
    /// <para>
    /// 它是由行为参数驱动纯函数的 <see langword="readonly"/> 结构体，不依赖引擎或玩家，
    /// 因此整个机制可用编辑模式测试检验。它仅响应首领自身的空挥与投入，不响应玩家状态，
    /// 所以无需感知延迟也能保持公平。
    /// </para>
    /// <para>
    /// 关键是它在首领真正适应前保持<em>休眠</em>：零投入时概率严格为零，
    /// 因此未学到任何习惯的战斗与原来一致。
    /// </para>
    /// </remarks>
    public readonly struct OverbalanceEvaluator
    {
        /// <summary>
        /// 失衡触发后至少保留的完整窗口比例，确保刚超过阈值的踉跄也足够长，能够被惩罚，而非一闪而过。
        /// </summary>
        private const float MinRecoveryFraction = 0.4f;

        private readonly float _maxChance;
        private readonly float _intensityThreshold;
        private readonly float _recoverySecondsAtFull;
        private readonly float _poiseMultiplierAtFull;

        /// <summary>使用行为配置创建评估器。</summary>
        /// <param name="maxChance">完全投入时，投入攻击空挥导致失衡的概率（0..1）。</param>
        /// <param name="intensityThreshold">
        /// 低于此投入强度不会失衡，形成死区，避免微小适应触发失衡。
        /// </param>
        /// <param name="recoverySecondsAtFull">完全投入时的踉跄时长，单位为秒。</param>
        /// <param name="poiseMultiplierAtFull">
        /// 完全失衡时承受的姿态伤害倍率。至少为一，因为踉跄只能让首领更脆弱，不能使其更坚固。
        /// </param>
        public OverbalanceEvaluator(
            float maxChance,
            float intensityThreshold,
            float recoverySecondsAtFull,
            float poiseMultiplierAtFull)
        {
            _maxChance = Mathf.Clamp01(maxChance);
            _intensityThreshold = Mathf.Clamp01(intensityThreshold);
            _recoverySecondsAtFull = Mathf.Max(0f, recoverySecondsAtFull);
            _poiseMultiplierAtFull = Mathf.Max(1f, poiseMultiplierAtFull);
        }

        /// <summary>
        /// 首领投入强度超过死区的程度，范围为 0..1。
        /// </summary>
        /// <param name="commitmentIntensity">首领当前投入强度（0..1）。</param>
        /// <returns>达到或低于阈值时为零，完全投入时为一。</returns>
        public float Severity(float commitmentIntensity)
        {
            if (_intensityThreshold >= 1f)
            {
                return 0f;
            }

            return Mathf.Clamp01((commitmentIntensity - _intensityThreshold) / (1f - _intensityThreshold));
        }

        /// <summary>当前投入程度下，投入攻击空挥引发失衡的概率。</summary>
        /// <param name="commitmentIntensity">首领当前投入强度（0..1）。</param>
        /// <returns>达到或低于阈值时为零，完全投入时升至最大概率。</returns>
        public float Chance(float commitmentIntensity) => _maxChance * Severity(commitmentIntensity);

        /// <summary>判断空挥是否使首领失衡。</summary>
        /// <param name="wasCommittedAttack">
        /// 落空的挥击是否属于投入攻击：突进、连段追击或阶段标志性招式。轻微试探不产生值得惩罚的破绽，
        /// 绝不会失衡。
        /// </param>
        /// <param name="commitmentIntensity">首领当前投入强度（0..1）。</param>
        /// <param name="roll">从注入的随机源取得的 [0, 1) 均匀样本。</param>
        /// <returns>首领应踉跄并暴露破绽时为 true。</returns>
        public bool ShouldOverbalance(bool wasCommittedAttack, float commitmentIntensity, float roll) =>
            wasCommittedAttack && roll < Chance(commitmentIntensity);

        /// <summary>产生的踉跄持续时间。</summary>
        /// <param name="commitmentIntensity">首领当前投入强度（0..1）。</param>
        /// <returns>随投入增大的窗口，不短于可被惩罚的最小值。</returns>
        public float ExtraRecoverySeconds(float commitmentIntensity) =>
            _recoverySecondsAtFull * Mathf.Lerp(MinRecoveryFraction, 1f, Severity(commitmentIntensity));

        /// <summary>踉跄期间承受的姿态伤害增幅。</summary>
        /// <param name="commitmentIntensity">首领当前投入强度（0..1）。</param>
        /// <returns>
        /// 没有投入时为一，投入增加时趋近完整倍率，使窗口内命中的攻击加速姿态崩溃，从而开启处决机会。
        /// </returns>
        public float PoiseVulnerabilityMultiplier(float commitmentIntensity) =>
            Mathf.Lerp(1f, _poiseMultiplierAtFull, Severity(commitmentIntensity));
    }
}
