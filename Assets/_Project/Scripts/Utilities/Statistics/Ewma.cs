using System;

namespace AdaptiveBossArena.Utilities.Statistics
{
    /// <summary>
    /// 具有可配置半衰期、基于经过时间的指数加权移动平均。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 同时服务于<em>缓慢适应</em>和<em>遗忘过时行为</em>两个设计目标。
    /// 半衰期表示旧观察影响减半所需时间，短半衰期反应敏感，长半衰期更固执；适应手感主要通过它调整。
    /// </para>
    /// <para>
    /// 权重按经过时间计算，不按样本数计算，因此不定期的闪避、
    /// 命中和固定频率的距离采样具有一致的实际衰减速度。
    /// </para>
    /// </remarks>
    public sealed class Ewma
    {
        private const float MinimumHalfLifeSeconds = 0.0001f;

        private readonly float _halfLifeSeconds;
        private float _value;
        private int _sampleCount;

        /// <summary>创建每隔<paramref name="halfLifeSeconds"/>将旧样本权重减半的平均器。</summary>
        /// <param name="halfLifeSeconds">观察影响减半的时间，必须为正数。</param>
        /// <exception cref="ArgumentOutOfRangeException">半衰期不是正数时抛出。</exception>
        public Ewma(float halfLifeSeconds)
        {
            if (halfLifeSeconds < MinimumHalfLifeSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(halfLifeSeconds), halfLifeSeconds, "Half-life must be positive.");
            }

            _halfLifeSeconds = halfLifeSeconds;
        }

        /// <summary>当前平滑值，首样本到来前为零。</summary>
        public float Value => _value;

        /// <summary>构造或上次重置以来输入的样本总数。</summary>
        public int SampleCount => _sampleCount;

        /// <summary>至少记录一个样本后为true。</summary>
        public bool HasSamples => _sampleCount > 0;

        /// <summary>配置的半衰期，单位为秒。</summary>
        public float HalfLifeSeconds => _halfLifeSeconds;

        /// <summary>
        /// 将新观察并入平均值，按经过时间加权。
        /// </summary>
        /// <param name="sample">观察值。</param>
        /// <param name="deltaTimeSeconds">距上次采样经过的时间，负值钳制为零。</param>
        public void AddSample(float sample, float deltaTimeSeconds)
        {
            // 首样本直接定义基线，避免与隐式零值混合后，让战斗开局的所有特征偏向零。
            if (_sampleCount == 0)
            {
                _value = sample;
                _sampleCount = 1;
                return;
            }

            if (deltaTimeSeconds < 0f)
            {
                deltaTimeSeconds = 0f;
            }

            float alpha = AlphaForElapsed(deltaTimeSeconds);
            _value += (sample - _value) * alpha;
            _sampleCount++;
        }

        /// <summary>
        /// 不记录新观察，将平均值向<paramref name="restingValue"/>衰减。
        /// </summary>
        /// <remarks>
        /// 用于某行为不再发生时；缺少证据应削弱特征，避免Boss持续反制玩家早已放弃的战术。
        /// </remarks>
        /// <param name="restingValue">平均值回归的目标。</param>
        /// <param name="deltaTimeSeconds">距上次更新经过的时间。</param>
        public void DecayToward(float restingValue, float deltaTimeSeconds)
        {
            if (_sampleCount == 0 || deltaTimeSeconds <= 0f)
            {
                return;
            }

            _value += (restingValue - _value) * AlphaForElapsed(deltaTimeSeconds);
        }

        /// <summary>清空平均值，恢复未观察状态。</summary>
        public void Reset()
        {
            _value = 0f;
            _sampleCount = 0;
        }

        /// <summary>覆盖当前值但不改变样本数。</summary>
        /// <param name="value">要强制设置的值。</param>
        public void SetValue(float value) => _value = value;

        /// <summary>指定时间之后观察到的样本混合权重。</summary>
        private float AlphaForElapsed(float deltaTimeSeconds)
        {
            // 由0.5^(dt/halfLife)推导，即旧值保留的比例。
            double retained = Math.Pow(0.5d, deltaTimeSeconds / _halfLifeSeconds);
            return (float)(1d - retained);
        }
    }
}
