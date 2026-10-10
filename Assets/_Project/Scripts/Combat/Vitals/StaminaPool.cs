using System;
using AdaptiveBossArena.Core.Combat;
using UnityEngine;

namespace AdaptiveBossArena.Combat.Vitals
{
    /// <summary>
    /// 每次消耗后暂停一段时间，再恢复的耐力池。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 恢复延迟是此类型的全部设计。缺少它时，耐力只会减慢连续冲刺；有了它，消耗耐力就意味着投入，
    /// 因为现在的一次冲刺会让随后一秒无法再冲刺。这让回避成为资源决策，而不只是反射动作。
    /// </para>
    /// <para>
    /// 它也不会向玩家显示失败消息。耐力不足的冲刺直接不发生；反馈是角色没有移动，而不是一条消息。
    /// </para>
    /// </remarks>
    public sealed class StaminaPool : IStaminaPool
    {
        private readonly float _regenPerSecond;
        private readonly float _regenDelaySeconds;

        private float _current;
        private float _maximum;
        private float _regenBlockedFor;

        /// <summary>创建已填充到最大值的池。</summary>
        /// <param name="maximum">池的容量，必须为正数。</param>
        /// <param name="regenPerSecond">恢复重新开始后，每秒恢复的数量。</param>
        /// <param name="regenDelaySeconds">消耗之后、恢复重新开始之前的暂停时间。</param>
        /// <exception cref="ArgumentOutOfRangeException">最大值不是正数时抛出。</exception>
        public StaminaPool(float maximum, float regenPerSecond, float regenDelaySeconds)
        {
            if (maximum <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximum), maximum, "Maximum stamina must be greater than zero.");
            }

            _maximum = maximum;
            _current = maximum;
            _regenPerSecond = Mathf.Max(0f, regenPerSecond);
            _regenDelaySeconds = Mathf.Max(0f, regenDelaySeconds);
        }

        /// <inheritdoc />
        public float Current => _current;

        /// <inheritdoc />
        public float Maximum => _maximum;

        /// <inheritdoc />
        public float Normalized => _maximum <= 0f ? 0f : Mathf.Clamp01(_current / _maximum);

        /// <summary>消耗后的暂停仍在进行时为 true。</summary>
        public bool IsRegenerationBlocked => _regenBlockedFor > 0f;

        /// <inheritdoc />
        public event Action<ResourceChangedArgs> Changed;

        /// <inheritdoc />
        public bool CanSpend(float amount) => amount <= 0f || _current >= amount;

        /// <inheritdoc />
        public bool TrySpend(float amount)
        {
            if (amount <= 0f)
            {
                return true;
            }

            if (_current < amount)
            {
                return false;
            }

            float previous = _current;
            _current -= amount;
            _regenBlockedFor = _regenDelaySeconds;

            Changed?.Invoke(ResourceChangedArgs.Create(previous, _current, _maximum));
            return true;
        }

        /// <inheritdoc />
        public void Restore(float amount)
        {
            if (amount <= 0f || _current >= _maximum)
            {
                return;
            }

            float previous = _current;
            _current = Mathf.Min(_maximum, _current + amount);

            Changed?.Invoke(ResourceChangedArgs.Create(previous, _current, _maximum));
        }

        /// <summary>
        /// 先推进恢复暂停，再推进恢复本身。
        /// </summary>
        /// <param name="deltaTimeSeconds">
        /// 经过的缩放时间，确保耐力不会在顿帧期间悄然回满。
        /// </param>
        public void Tick(float deltaTimeSeconds)
        {
            if (deltaTimeSeconds <= 0f)
            {
                return;
            }

            if (_regenBlockedFor > 0f)
            {
                _regenBlockedFor -= deltaTimeSeconds;

                // 暂停和它控制的恢复不会在同一步中进行，确保延迟真实有效，而不是让长帧同时消耗两者。
                return;
            }

            Restore(_regenPerSecond * deltaTimeSeconds);
        }

        /// <summary>填满池，并清除尚未结束的恢复暂停。</summary>
        public void ResetToFull()
        {
            float previous = _current;
            _current = _maximum;
            _regenBlockedFor = 0f;

            Changed?.Invoke(ResourceChangedArgs.Create(previous, _current, _maximum));
        }
    }
}
