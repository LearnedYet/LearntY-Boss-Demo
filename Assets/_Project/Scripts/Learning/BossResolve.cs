using UnityEngine;

namespace AdaptiveBossArena.Learning
{
    /// <summary>
    /// 首领在战斗中积累、并用于发动标志性攻势的资源槽。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 决意是设计资源而非适应：它限制首领<em>何时</em>投入原本已有的招式，而不改变首领的能力。
    /// 资源来自首领能够合理观察的两件事：战斗持续时间，以及自身攻击空挥（玩家躲开挥击）。
    /// 只顾闪避的玩家会让资源更快积满，呈现为首领因攻击被躲开而变得焦躁。这里不接触玩家输入。
    /// </para>
    /// <para>
    /// 与 <see cref="BossTuning"/> 一样属于纯逻辑，积累和阈值可以在编辑模式测试中检验，无需创建首领。
    /// 资源充能的可见提示是首领光环逐渐增强，由表现层负责；这里只决定攻势何时准备好。
    /// </para>
    /// </remarks>
    public sealed class BossResolve
    {
        private readonly float _threshold;
        private readonly float _buildPerSecond;
        private readonly float _buildPerWhiff;

        private float _current;

        /// <summary>创建空资源槽。</summary>
        /// <param name="threshold">准备攻势所需的决意，必须为正。</param>
        /// <param name="buildPerSecond">每秒战斗积累的决意。</param>
        /// <param name="buildPerWhiff">首领每次空挥积累的决意。</param>
        public BossResolve(float threshold, float buildPerSecond, float buildPerWhiff)
        {
            _threshold = Mathf.Max(1f, threshold);
            _buildPerSecond = Mathf.Max(0f, buildPerSecond);
            _buildPerWhiff = Mathf.Max(0f, buildPerWhiff);
        }

        /// <summary>资源槽的填充程度，范围零至一，供充能光效使用。</summary>
        public float Normalized => Mathf.Clamp01(_current / _threshold);

        /// <summary>可以发动攻势时为 true。</summary>
        public bool IsReady => _current >= _threshold;

        /// <summary>随战斗时间经过积累决意。</summary>
        /// <param name="deltaTime">经过缩放的时间增量。</param>
        public void Tick(float deltaTime)
        {
            if (deltaTime > 0f)
            {
                Add(_buildPerSecond * deltaTime);
            }
        }

        /// <summary>首领自己的攻击被躲开时积累决意。</summary>
        public void RegisterBossWhiff() => Add(_buildPerWhiff);

        /// <summary>攻势消耗资源后清空资源槽。</summary>
        public void Spend() => _current = 0f;

        /// <summary>为新一次尝试清空资源槽。</summary>
        public void Reset() => _current = 0f;

        /// <summary>增加决意并限制在阈值，准备完成后保持满槽。</summary>
        private void Add(float amount) => _current = Mathf.Min(_threshold, _current + Mathf.Max(0f, amount));
    }
}
