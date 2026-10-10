using System;
using AdaptiveBossArena.Core.Combat;
using UnityEngine;

namespace AdaptiveBossArena.Combat.Vitals
{
    /// <summary>
    /// 带有变化和死亡通知的生命值池。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 采用普通类而不是组件，并刻意放在 Combat 程序集内，让玩家与首领共用实现。
    /// 双方生命值行为的细微差异属于只会在同时致命命中等边界情况出现的缺陷。
    /// </para>
    /// <para>
    /// 池不关心无敌帧、完美闪避和抗性。这些决定命中是否应当生效，
    /// 属于所属对象的<see cref="IDamageable"/> 实现。此类型只改变数值。
    /// </para>
    /// </remarks>
    public sealed class HealthPool : IHealth
    {
        private readonly CombatantTeam _team;
        private float _current;
        private float _maximum;

        /// <summary>创建已填充到最大值的池。</summary>
        /// <param name="maximum">池的容量，必须为正数。</param>
        /// <param name="team">所属对象的阵营，会随死亡事件报告。</param>
        /// <exception cref="ArgumentOutOfRangeException">最大值不是正数时抛出。</exception>
        public HealthPool(float maximum, CombatantTeam team)
        {
            if (maximum <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximum), maximum, "Maximum health must be greater than zero.");
            }

            _maximum = maximum;
            _current = maximum;
            _team = team;
        }

        /// <inheritdoc />
        public float Current => _current;

        /// <inheritdoc />
        public float Maximum => _maximum;

        /// <inheritdoc />
        public float Normalized => _maximum <= 0f ? 0f : Mathf.Clamp01(_current / _maximum);

        /// <inheritdoc />
        public bool IsAlive => _current > 0f;

        /// <inheritdoc />
        public event Action<ResourceChangedArgs> Changed;

        /// <inheritdoc />
        public event Action<DeathArgs> Died;

        /// <summary>
        /// 扣除生命值，并报告实际扣除的数量。
        /// </summary>
        /// <param name="amount">要扣除的生命值，忽略非正数。</param>
        /// <param name="cause">造成伤害的命中，转发到死亡事件用于归因。</param>
        /// <returns>限制最低为零后，实际扣除的生命值。</returns>
        public float Reduce(float amount, in DamageInfo cause)
        {
            if (amount <= 0f || !IsAlive)
            {
                return 0f;
            }

            float previous = _current;
            _current = Mathf.Max(0f, _current - amount);
            float applied = previous - _current;

            Changed?.Invoke(ResourceChangedArgs.Create(previous, _current, _maximum));

            // 死亡在变化通知后触发，确保死亡界面响应时，生命条已经更新为零。
            if (_current <= 0f)
            {
                Died?.Invoke(new DeathArgs
                {
                    Team = _team,
                    KillerInstanceId = cause.SourceInstanceId,
                    FinalBlow = cause
                });
            }

            return applied;
        }

        /// <inheritdoc />
        public float Heal(float amount)
        {
            // 治疗死者会悄然复活已经宣布死亡的战斗参与者，让监听者处于不一致状态。
            if (amount <= 0f || !IsAlive)
            {
                return 0f;
            }

            float previous = _current;
            _current = Mathf.Min(_maximum, _current + amount);
            float restored = _current - previous;

            if (restored > 0f)
            {
                Changed?.Invoke(ResourceChangedArgs.Create(previous, _current, _maximum));
            }

            return restored;
        }

        /// <summary>填满池，例如重新开始战斗时。</summary>
        public void ResetToFull()
        {
            float previous = _current;
            _current = _maximum;
            Changed?.Invoke(ResourceChangedArgs.Create(previous, _current, _maximum));
        }

        /// <summary>改变容量，并按比例调整当前值以保留填充比例。</summary>
        /// <param name="maximum">新容量，必须为正数。</param>
        public void SetMaximum(float maximum)
        {
            if (maximum <= 0f)
            {
                return;
            }

            float fraction = Normalized;
            float previous = _current;

            _maximum = maximum;
            _current = _maximum * fraction;

            Changed?.Invoke(ResourceChangedArgs.Create(previous, _current, _maximum));
        }
    }
}
