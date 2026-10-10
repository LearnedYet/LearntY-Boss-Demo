using UnityEngine;

namespace AdaptiveBossArena.Utilities.Feel
{
    /// <summary>
    /// 基于冲击强度的程序化镜头震动。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 调用者添加冲击强度，震动幅度为其幂次。平方使轻击震动接近不可察觉、重击震动明显，
    /// 比按伤害线性缩放更自然。
    /// </para>
    /// <para>
    /// 各轴在独立行采样Perlin噪声，而不直接使用随机数。连续噪声让镜头平滑运动，且不依赖帧率。
    /// </para>
    /// </remarks>
    public sealed class TraumaShake
    {
        /// <summary>冲击强度的指数，二次方使弱震动迅速减小。</summary>
        private const float TraumaExponent = 2f;

        /// <summary>互不相同的噪声行，避免三个轴同步运动。</summary>
        private const float NoiseRowPositionX = 0f;
        private const float NoiseRowPositionY = 137f;
        private const float NoiseRowRoll = 311f;

        private readonly float _decayPerSecond;
        private readonly float _frequency;

        private float _trauma;
        private float _noiseTime;

        /// <summary>创建震动源。</summary>
        /// <param name="decayPerSecond">每秒衰减的冲击强度，越大震动越短。</param>
        /// <param name="frequency">噪声采样速度，越高越尖锐、剧烈。</param>
        public TraumaShake(float decayPerSecond = 1.6f, float frequency = 22f)
        {
            _decayPerSecond = Mathf.Max(0f, decayPerSecond);
            _frequency = Mathf.Max(0f, frequency);
        }

        /// <summary>当前冲击强度，范围零到一。</summary>
        public float Trauma => _trauma;

        /// <summary>镜头仍有位移时为true。</summary>
        public bool IsShaking => _trauma > 0f;

        /// <summary>
        /// 累加冲击强度，上限为一。
        /// </summary>
        /// <remarks>
        /// 使用累加让同时命中的多次攻击形成一次更大震动；钳制避免连续攻击把镜头震出竞技场。
        /// </remarks>
        /// <param name="amount">要增加的强度，通常轻击为0.1、阶段切换为0.6。</param>
        public void AddTrauma(float amount) => _trauma = Mathf.Clamp01(_trauma + Mathf.Max(0f, amount));

        /// <summary>推进强度衰减和噪声采样游标。</summary>
        /// <param name="deltaTimeSeconds">
        /// 经过时间，传入未缩放时间，使打击停顿期间震动仍正确显示。
        /// </param>
        public void Tick(float deltaTimeSeconds)
        {
            if (deltaTimeSeconds <= 0f)
            {
                return;
            }

            _noiseTime += deltaTimeSeconds * _frequency;
            _trauma = Mathf.Max(0f, _trauma - _decayPerSecond * deltaTimeSeconds);
        }

        /// <summary>
        /// 采样当前位移。
        /// </summary>
        /// <param name="maxTranslation">满强度时世界单位的位置偏移峰值。</param>
        /// <param name="maxRollDegrees">满强度时镜头滚转角度峰值。</param>
        /// <param name="translation">接收要加到镜头的位置偏移。</param>
        /// <param name="rollDegrees">接收要加到镜头的滚转角。</param>
        public void Sample(float maxTranslation, float maxRollDegrees, out Vector2 translation, out float rollDegrees)
        {
            if (_trauma <= 0f)
            {
                translation = Vector2.zero;
                rollDegrees = 0f;
                return;
            }

            float magnitude = Mathf.Pow(_trauma, TraumaExponent);

            translation = new Vector2(
                SignedNoise(NoiseRowPositionX) * maxTranslation * magnitude,
                SignedNoise(NoiseRowPositionY) * maxTranslation * magnitude);

            rollDegrees = SignedNoise(NoiseRowRoll) * maxRollDegrees * magnitude;
        }

        /// <summary>立即停止震动。</summary>
        public void Reset()
        {
            _trauma = 0f;
        }

        /// <summary>在指定行采样Perlin噪声，并映射到负一至一。</summary>
        private float SignedNoise(float row) => Mathf.PerlinNoise(row, _noiseTime) * 2f - 1f;
    }
}
