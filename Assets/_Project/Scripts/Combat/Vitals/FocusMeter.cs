using System;
using AdaptiveBossArena.Core.Combat;
using UnityEngine;

namespace AdaptiveBossArena.Combat.Vitals
{
    /// <summary>
    /// 玩家防守得当时积累、用于强化特殊技能的数值槽。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 它奖励游戏的核心动作——化解首领攻击——而不是造成伤害：成功弹反和完美闪避都会积累专注，
    /// 因此识破首领的回报是自己更强的应对。玩家进入硬直时它会完全清空，因此专注代表压力下保持的镇定，
    /// 而不是只增不减的条。
    /// </para>
    /// <para>
    /// 和其他状态数值（<see cref="StaminaPool"/>、<see cref="PoisePool"/>）一样采用纯逻辑，让积累、
    /// 消耗和损失都能由编辑模式测试固定验证；它从<em>空值</em>开始，因为专注要在战斗中获得，
    /// 而不是开战时赠予。
    /// </para>
    /// </remarks>
    public sealed class FocusMeter
    {
        private readonly float _maximum;
        private readonly float _gainPerDeflect;
        private readonly float _gainPerPerfectDodge;

        private float _current;

        /// <summary>创建空资源槽。</summary>
        /// <param name="maximum">强化特殊技能所需的专注，必须为正数。</param>
        /// <param name="gainPerDeflect">成功弹反获得的专注。</param>
        /// <param name="gainPerPerfectDodge">完美闪避获得的专注。</param>
        /// <exception cref="ArgumentOutOfRangeException">最大值不是正数时抛出。</exception>
        public FocusMeter(float maximum, float gainPerDeflect, float gainPerPerfectDodge)
        {
            if (maximum <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximum), maximum, "Maximum focus must be greater than zero.");
            }

            _maximum = maximum;
            _gainPerDeflect = Mathf.Max(0f, gainPerDeflect);
            _gainPerPerfectDodge = Mathf.Max(0f, gainPerPerfectDodge);
            _current = 0f;
        }

        /// <summary>当前专注。</summary>
        public float Current => _current;

        /// <summary>强化特殊技能所需的专注。</summary>
        public float Maximum => _maximum;

        /// <summary>当前专注占最大值的比例。</summary>
        public float Normalized => _maximum <= 0f ? 0f : Mathf.Clamp01(_current / _maximum);

        /// <summary>可以强化特殊技能时为 true。</summary>
        public bool IsFull => _current >= _maximum;

        /// <summary>专注变化时触发，供状态界面使用。</summary>
        public event Action<ResourceChangedArgs> Changed;

        /// <summary>添加弹反奖励。</summary>
        public void AddFromDeflect() => Add(_gainPerDeflect);

        /// <summary>添加完美闪避奖励。</summary>
        public void AddFromPerfectDodge() => Add(_gainPerPerfectDodge);

        /// <summary>
        /// 消耗完整专注槽，并报告是否存在可消耗的满槽。
        /// </summary>
        /// <returns>原先满槽且现在已经清空时为 true。</returns>
        public bool TryConsumeFull()
        {
            if (_current < _maximum)
            {
                return false;
            }

            Set(0f);
            return true;
        }

        /// <summary>清空数值槽，用于硬直或重新尝试。</summary>
        public void Reset() => Set(0f);

        /// <summary>添加专注并限制到最大值，触发变化通知。</summary>
        private void Add(float amount)
        {
            if (amount <= 0f || _current >= _maximum)
            {
                return;
            }

            Set(Mathf.Min(_maximum, _current + amount));
        }

        /// <summary>设置数值，仅在实际改变时报告变化。</summary>
        private void Set(float value)
        {
            if (Mathf.Approximately(value, _current))
            {
                return;
            }

            float previous = _current;
            _current = value;

            Changed?.Invoke(ResourceChangedArgs.Create(previous, _current, _maximum));
        }
    }
}
