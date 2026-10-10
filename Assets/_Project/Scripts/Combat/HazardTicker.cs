using UnityEngine;

namespace AdaptiveBossArena.Combat
{
    /// <summary>
    /// 地面危险区域生命周期：预警、伤害生效及消退。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 从 <see cref="HazardZone"/> 分离为纯逻辑，让两项公平性要求——伤害前预警、
    /// 按固定节奏而非逐帧造成伤害——无需场景就能在编辑模式测试中固定。任一项出错，
    /// “管理空间”的危险区域就会变成不可读、依赖帧率的陷阱。
    /// </para>
    /// <para>
    /// 生效阶段打开瞬间触发第一次伤害，使区域在静止玩家脚下形成时立即伤害，而非免费多给一个间隔。
    /// </para>
    /// </remarks>
    public sealed class HazardTicker
    {
        /// <summary>危险区域所处的生命周期阶段。</summary>
        public enum Stage
        {
            /// <summary>已预警，但尚未危险。</summary>
            Warning,

            /// <summary>已生效，按自身节奏造成伤害。</summary>
            Active,

            /// <summary>正在消散，不再危险。</summary>
            Fading,

            /// <summary>已结束，可以回收。</summary>
            Expired
        }

        private readonly float _warningSeconds;
        private readonly float _activeSeconds;
        private readonly float _fadeSeconds;
        private readonly float _damageIntervalSeconds;

        private float _elapsed;
        private float _sinceDamage;

        /// <summary>为一个危险区域实例创建计时器。</summary>
        /// <param name="warningSeconds">区域变危险前的预警时间。</param>
        /// <param name="activeSeconds">区域造成伤害的时长。</param>
        /// <param name="fadeSeconds">生效阶段后的无害消散时间。</param>
        /// <param name="damageIntervalSeconds">生效时伤害跳之间的秒数。</param>
        public HazardTicker(
            float warningSeconds, float activeSeconds, float fadeSeconds, float damageIntervalSeconds)
        {
            _warningSeconds = Mathf.Max(0f, warningSeconds);
            _activeSeconds = Mathf.Max(0f, activeSeconds);
            _fadeSeconds = Mathf.Max(0f, fadeSeconds);
            _damageIntervalSeconds = Mathf.Max(0.01f, damageIntervalSeconds);

            // 预先准备，使生效窗口内首次推进立即触发伤害跳。
            _sinceDamage = _damageIntervalSeconds;
        }

        /// <summary>区域开始后经过的秒数。</summary>
        public float Elapsed => _elapsed;

        /// <summary>区域的总生命周期。</summary>
        public float TotalSeconds => _warningSeconds + _activeSeconds + _fadeSeconds;

        /// <summary>区域当前所处阶段。</summary>
        public Stage CurrentStage =>
            _elapsed >= TotalSeconds ? Stage.Expired
            : _elapsed < _warningSeconds ? Stage.Warning
            : _elapsed < _warningSeconds + _activeSeconds ? Stage.Active
            : Stage.Fading;

        /// <summary>区域结束且可以回收时为 true。</summary>
        public bool IsExpired => CurrentStage == Stage.Expired;

        /// <summary>
        /// 供视觉使用的零至一存在程度：预警期间上升，生效时满值，消退期间下降。
        /// </summary>
        public float VisualPresence
        {
            get
            {
                switch (CurrentStage)
                {
                    case Stage.Warning:
                        return _warningSeconds <= 0f ? 1f : Mathf.Clamp01(_elapsed / _warningSeconds);
                    case Stage.Active:
                        return 1f;
                    case Stage.Fading:
                        float into = _elapsed - _warningSeconds - _activeSeconds;
                        return _fadeSeconds <= 0f ? 0f : Mathf.Clamp01(1f - into / _fadeSeconds);
                    default:
                        return 0f;
                }
            }
        }

        /// <summary>
        /// 推进区域并报告本步是否应触发伤害跳。
        /// </summary>
        /// <param name="deltaTime">经过缩放的时间增量，使危险区域在顿帧期间冻结。</param>
        /// <returns>调用者应应用一跳伤害时为 true。</returns>
        public bool Advance(float deltaTime)
        {
            _elapsed += Mathf.Max(0f, deltaTime);

            // 只有生效窗口造成伤害，累加器也只在此时运行，避免预警和消退阶段累计跳数，
            // 在区域生效或结束瞬间集中触发。
            if (CurrentStage != Stage.Active)
            {
                return false;
            }

            _sinceDamage += deltaTime;

            if (_sinceDamage >= _damageIntervalSeconds)
            {
                _sinceDamage -= _damageIntervalSeconds;
                return true;
            }

            return false;
        }
    }
}
