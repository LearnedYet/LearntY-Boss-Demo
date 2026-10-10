using UnityEngine;

namespace AdaptiveBossArena.Utilities.Timing
{
    /// <summary>
    /// 由调用者显式推进、无需引擎时间的倒计时器。
    /// </summary>
    /// <remarks>
    /// 调用者提供delta，让攻击窗口、冷却和适应间隔使用缩放时间，响应打击停顿与慢动作；
    /// UI和暂停使用未缩放时间。依赖时序的系统也能在运行模式外确定性测试。
    /// </remarks>
    public sealed class CountdownTimer
    {
        private float _duration;
        private float _remaining;
        private bool _isRunning;

        /// <summary>创建停止状态计时器。</summary>
        public CountdownTimer()
        {
        }

        /// <summary>创建计时器并立即启动。</summary>
        /// <param name="duration">倒计时时长，单位为秒。</param>
        public CountdownTimer(float duration) => Start(duration);

        /// <summary>正在倒计时且尚未结束时为true。</summary>
        public bool IsRunning => _isRunning;

        /// <summary>结束前剩余秒数。</summary>
        public float Remaining => _remaining;

        /// <summary>启动时设置的总时长。</summary>
        public float Duration => _duration;

        /// <summary>已消耗比例，开始为零，结束为一。</summary>
        public float Progress01 => _duration <= 0f ? 1f : Mathf.Clamp01(1f - _remaining / _duration);

        /// <summary>倒计时到零时触发一次。</summary>
        public event System.Action Completed;

        /// <summary>启动或重新启动倒计时。</summary>
        /// <param name="duration">倒计时时长，单位为秒；非正时长在下次推进时完成。</param>
        public void Start(float duration)
        {
            _duration = Mathf.Max(0f, duration);
            _remaining = _duration;
            _isRunning = true;

            if (_duration <= 0f)
            {
                Complete();
            }
        }

        /// <summary>推进倒计时。</summary>
        /// <param name="deltaTimeSeconds">经过时间，忽略非正值。</param>
        /// <returns>仅在倒计时完成的那一步返回true。</returns>
        public bool Tick(float deltaTimeSeconds)
        {
            if (!_isRunning || deltaTimeSeconds <= 0f)
            {
                return false;
            }

            _remaining -= deltaTimeSeconds;
            if (_remaining > 0f)
            {
                return false;
            }

            Complete();
            return true;
        }

        /// <summary>停止倒计时，不触发<see cref="Completed"/>。</summary>
        public void Cancel()
        {
            _isRunning = false;
            _remaining = 0f;
        }

        private void Complete()
        {
            _remaining = 0f;
            _isRunning = false;
            Completed?.Invoke();
        }
    }
}
