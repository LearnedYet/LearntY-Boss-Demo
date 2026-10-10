using System;
using AdaptiveBossArena.Core.Combat;
using UnityEngine;

namespace AdaptiveBossArena.Combat.Vitals
{
    /// <summary>
    /// 决定命中是否打断目标的隐藏数值池。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 姿态让换血成为决策，而不只是反射动作。由于未受击时会恢复，零散削血不会造成硬直，
    /// 但投入的连击能够做到。因此玩家必须在安全试探和深入投入以打破首领姿态之间作出选择。
    /// </para>
    /// <para>
    /// 破防会清空池并开始恢复期；在此期间不再累积姿态伤害。否则，硬直期间命中的连击会连续破防，
    /// 让目标完全无法参与战斗。
    /// </para>
    /// </remarks>
    public sealed class PoisePool : IPoise
    {
        private readonly float _regenPerSecond;
        private readonly float _breakRecoverySeconds;

        private float _current;
        private float _maximum;
        private float _brokenFor;

        /// <summary>创建满姿态的池。</summary>
        /// <param name="maximum">打破所属者姿态所需的姿态伤害，必须为正数。</param>
        /// <param name="regenPerSecond">未受击时，每秒恢复的姿态。</param>
        /// <param name="breakRecoverySeconds">破防持续多长时间后开始重建姿态。</param>
        /// <exception cref="ArgumentOutOfRangeException">最大值不是正数时抛出。</exception>
        public PoisePool(float maximum, float regenPerSecond, float breakRecoverySeconds)
        {
            if (maximum <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximum), maximum, "Maximum poise must be greater than zero.");
            }

            _maximum = maximum;
            _current = maximum;
            _regenPerSecond = Mathf.Max(0f, regenPerSecond);
            _breakRecoverySeconds = Mathf.Max(0f, breakRecoverySeconds);
        }

        /// <inheritdoc />
        public float Current => _current;

        /// <inheritdoc />
        public float Maximum => _maximum;

        /// <inheritdoc />
        public float Normalized => _maximum <= 0f ? 0f : Mathf.Clamp01(_current / _maximum);

        /// <inheritdoc />
        public bool IsBroken => _brokenFor > 0f;

        /// <inheritdoc />
        public event Action<ResourceChangedArgs> Changed;

        /// <inheritdoc />
        public event Action Broken;

        /// <inheritdoc />
        public bool ApplyPoiseDamage(float amount)
        {
            // 破防期间的姿态伤害会被丢弃，而不是储存。累积它会让命中硬直目标的连击直接衔接第二次破防。
            if (amount <= 0f || IsBroken)
            {
                return false;
            }

            float previous = _current;
            _current = Mathf.Max(0f, _current - amount);

            Changed?.Invoke(ResourceChangedArgs.Create(previous, _current, _maximum));

            if (_current > 0f)
            {
                return false;
            }

            _brokenFor = _breakRecoverySeconds;
            Broken?.Invoke();
            return true;
        }

        /// <summary>先推进破防恢复，再推进姿态恢复。</summary>
        /// <param name="deltaTimeSeconds">经过的缩放时间。</param>
        public void Tick(float deltaTimeSeconds)
        {
            if (deltaTimeSeconds <= 0f)
            {
                return;
            }

            if (_brokenFor > 0f)
            {
                _brokenFor -= deltaTimeSeconds;

                if (_brokenFor <= 0f)
                {
                    // 破防结束时姿态直接回满，而不是从零重建，确保恢复后的战斗参与者不会立刻被一次轻
                    // 击再次破防。
                    _brokenFor = 0f;
                    SetCurrent(_maximum);
                }

                return;
            }

            if (_current < _maximum)
            {
                SetCurrent(Mathf.Min(_maximum, _current + _regenPerSecond * deltaTimeSeconds));
            }
        }

        /// <summary>恢复完整姿态并清除破防状态，用于重新开始战斗。</summary>
        public void ResetToFull()
        {
            _brokenFor = 0f;
            SetCurrent(_maximum);
        }

        private void SetCurrent(float value)
        {
            float previous = _current;
            _current = value;

            if (!Mathf.Approximately(previous, _current))
            {
                Changed?.Invoke(ResourceChangedArgs.Create(previous, _current, _maximum));
            }
        }
    }
}
