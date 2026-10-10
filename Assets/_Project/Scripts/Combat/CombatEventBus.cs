using System;
using AdaptiveBossArena.Core.Combat;

namespace AdaptiveBossArena.Combat
{
    /// <summary>
    /// <see cref="ICombatEventBus"/> 的默认实现。
    /// </summary>
    /// <remarks>
    /// 采用直接多播委托，而非 ScriptableObject 通道。战斗事件的触发频率远高于通道所服务的界面信号，
    /// 而且与这些信号不同，它们唯一关键消费者是首领的战斗记忆。
    /// </remarks>
    public sealed class CombatEventBus : ICombatEventBus
    {
        /// <inheritdoc />
        public event Action<CombatEvent> EventRecorded;

        /// <summary>构造以来发布的事件数量。</summary>
        public int PublishedCount { get; private set; }

        /// <inheritdoc />
        public void Publish(in CombatEvent combatEvent)
        {
            PublishedCount++;
            EventRecorded?.Invoke(combatEvent);
        }

        /// <inheritdoc />
        public void Clear()
        {
            EventRecorded = null;
            PublishedCount = 0;
        }
    }
}
