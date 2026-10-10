using System;
using UnityEngine;

namespace AdaptiveBossArena.Combat
{
    /// <summary>
    /// 按计时器驱动攻击经过前摇、生效和后摇阶段。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是动画事件的具体替代实现。由于推进时显式传入时间增量，可以在编辑模式测试中验证攻击帧数据；
    /// 顿帧只需传入缩放后的时间，就能冻结攻击进度。
    /// </para>
    /// <para>
    /// 每次推进只评估一次阶段转换；如果帧足够长，可能完全跳过某个阶段。这是有意的：
    /// 卡顿绝不能让攻击卡在挥击中途，且命中盒一直开启。这也意味着极长的帧可能跨过短暂的生效窗口，
    /// 从未报告该阶段；因此 <see cref="Tick"/> 会按顺序报告阶段，而不是只宣布最终阶段。
    /// </para>
    /// </remarks>
    public sealed class AttackTimeline : IAttackTimeline
    {
        private AttackDefinition _attack;
        private float _elapsed;
        private AttackPhase _phase = AttackPhase.Inactive;

        /// <inheritdoc />
        public AttackPhase Phase => _phase;

        /// <inheritdoc />
        public bool IsRunning => _phase != AttackPhase.Inactive;

        /// <inheritdoc />
        public float ElapsedSeconds => _elapsed;

        /// <inheritdoc />
        public float NormalizedTime =>
            _attack == null || _attack.TotalSeconds <= 0f
                ? 0f
                : Mathf.Clamp01(_elapsed / _attack.TotalSeconds);

        /// <inheritdoc />
        public AttackDefinition CurrentAttack => _attack;

        /// <inheritdoc />
        public bool IsInComboWindow =>
            _attack != null &&
            _elapsed >= _attack.ComboWindowStartSeconds &&
            _elapsed <= _attack.ComboWindowEndSeconds;

        /// <inheritdoc />
        public event Action<AttackPhase> PhaseChanged;

        /// <inheritdoc />
        public event Action Completed;

        /// <inheritdoc />
        public void Begin(AttackDefinition attack)
        {
            _attack = attack != null ? attack : throw new ArgumentNullException(nameof(attack));
            _elapsed = 0f;

            TransitionTo(AttackPhase.Startup);
        }

        /// <inheritdoc />
        public void Tick(float deltaTime)
        {
            if (!IsRunning || deltaTime <= 0f)
            {
                return;
            }

            _elapsed += deltaTime;

            // 逐个阶段推进，确保长帧不会跳过生效窗口，导致命中盒从未开启。
            while (IsRunning && _phase != ResolvePhase())
            {
                TransitionTo(NextPhaseAfter(_phase));
            }
        }

        /// <inheritdoc />
        public void Cancel()
        {
            if (!IsRunning)
            {
                return;
            }

            _attack = null;
            _elapsed = 0f;
            _phase = AttackPhase.Inactive;

            // 不触发 Completed。被取消的攻击并未完成，任何等待完成后交还控制权的对象都必须区分这两种
            // 情况。
            PhaseChanged?.Invoke(AttackPhase.Inactive);
        }

        /// <summary>当前经过时间所处的阶段。</summary>
        private AttackPhase ResolvePhase()
        {
            if (_attack == null)
            {
                return AttackPhase.Inactive;
            }

            if (_elapsed < _attack.ActiveStartSeconds)
            {
                return AttackPhase.Startup;
            }

            if (_elapsed < _attack.ActiveEndSeconds)
            {
                return AttackPhase.Active;
            }

            return _elapsed < _attack.TotalSeconds ? AttackPhase.Recovery : AttackPhase.Inactive;
        }

        /// <summary>按固定的前摇、生效、后摇、未激活顺序排列的下一阶段。</summary>
        private static AttackPhase NextPhaseAfter(AttackPhase phase)
        {
            switch (phase)
            {
                case AttackPhase.Startup: return AttackPhase.Active;
                case AttackPhase.Active: return AttackPhase.Recovery;
                default: return AttackPhase.Inactive;
            }
        }

        /// <summary>进入一个阶段并通知；攻击结束时发出完成通知。</summary>
        private void TransitionTo(AttackPhase phase)
        {
            _phase = phase;

            if (phase == AttackPhase.Inactive)
            {
                _attack = null;
                _elapsed = 0f;

                PhaseChanged?.Invoke(AttackPhase.Inactive);
                Completed?.Invoke();
                return;
            }

            PhaseChanged?.Invoke(phase);
        }
    }
}
