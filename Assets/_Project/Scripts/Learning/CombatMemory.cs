using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Utilities.Collections;

namespace AdaptiveBossArena.Learning
{
    /// <summary>
    /// 首领对自己观察到的玩家行为的记忆。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 使用有界环形缓冲保存观察事件，并维护少量累计计数。
    /// 限制容量是为了让任意长度的战斗具有相同的内存和逐帧开销，也因为首领若对整场战斗记忆一视同仁，
    /// 就永远不会忘记玩家在五分钟前已经放弃的习惯。
    /// </para>
    /// <para>
    /// 此类型只记录，不做推断；推断是<see cref="PatternRecognizer"/> 的职责。将两者分开，
    /// 记录便可逐帧进行，而解释每隔几秒才执行一次；正是这种分离避免首领针对单个动作立即反应。
    /// </para>
    /// </remarks>
    public sealed class CombatMemory
    {
        /// <summary>大致保留战斗中最近数百个值得记录的事件。</summary>
        public const int DefaultCapacity = 256;

        private readonly CircularBuffer<CombatEvent> _events;

        /// <summary>创建具有固定回忆深度的记忆。</summary>
        /// <param name="capacity">保留的事件数量。</param>
        public CombatMemory(int capacity = DefaultCapacity) =>
            _events = new CircularBuffer<CombatEvent>(capacity);

        /// <summary>当前保留的事件，按从旧到新排序。</summary>
        public CircularBuffer<CombatEvent> Events => _events;

        /// <summary>保留的事件数量。</summary>
        public int Count => _events.Count;

        /// <summary>自战斗开始以来观察到的玩家攻击总次数。</summary>
        public int PlayerAttacksThrown { get; private set; }

        /// <summary>玩家发动的重攻击次数。</summary>
        public int PlayerHeavyAttacksThrown { get; private set; }

        /// <summary>玩家未命中任何目标的攻击次数。</summary>
        public int PlayerAttacksWhiffed { get; private set; }

        /// <summary>观察到的闪避次数。</summary>
        public int PlayerDodges { get; private set; }

        /// <summary>时机足够精准、完全规避攻击的闪避次数。</summary>
        public int PlayerPerfectDodges { get; private set; }

        /// <summary>玩家开始治疗的次数。</summary>
        public int PlayerHealsStarted { get; private set; }

        /// <summary>玩家举起防御的次数。</summary>
        public int PlayerGuardsRaised { get; private set; }

        /// <summary>玩家精准弹反的攻击次数。</summary>
        public int PlayerDeflects { get; private set; }

        /// <summary>玩家较晚格挡、承受削血和姿态伤害的攻击次数。</summary>
        public int PlayerBlocks { get; private set; }

        /// <summary>玩家对崩溃防御实施反击的次数。</summary>
        public int PlayerRipostes { get; private set; }

        /// <summary>首领命中的攻击次数。</summary>
        public int BossAttacksLanded { get; private set; }

        /// <summary>被玩家躲开的首领攻击次数。</summary>
        public int BossAttacksEvaded { get; private set; }

        /// <summary>首领未命中任何目标的攻击次数。</summary>
        public int BossAttacksWhiffed { get; private set; }

        /// <summary>首领精准招架的玩家挥击次数。</summary>
        /// <remarks>
        /// 这里只做统计，与本类其他内容一致。首领行为不得直接依赖它：总线会立即送达事件，
        /// 直接接入的行为会绕过维持公平性的感知延迟。如果首领要更频繁地招架自己已反复正确识破的玩家，
        /// 必须在低频适应更新中逐渐调整行为参数。
        /// </remarks>
        public int BossParries { get; private set; }

        /// <summary>记录事件并更新累计计数。</summary>
        /// <param name="combatEvent">要记住的事件。</param>
        public void Record(in CombatEvent combatEvent)
        {
            _events.Add(combatEvent);

            if (combatEvent.Actor == CombatantTeam.Player)
            {
                RecordPlayerEvent(combatEvent);
            }
            else if (combatEvent.Actor == CombatantTeam.Boss)
            {
                RecordBossEvent(combatEvent);
            }
        }

        private void RecordPlayerEvent(in CombatEvent combatEvent)
        {
            switch (combatEvent.Kind)
            {
                case CombatEventKind.AttackStarted:
                    PlayerAttacksThrown++;

                    if (combatEvent.DamageType == DamageType.Heavy)
                    {
                        PlayerHeavyAttacksThrown++;
                    }

                    break;

                case CombatEventKind.AttackWhiffed:
                    PlayerAttacksWhiffed++;
                    break;

                case CombatEventKind.DodgePerformed:
                    PlayerDodges++;
                    break;

                case CombatEventKind.PerfectDodge:
                    PlayerPerfectDodges++;
                    break;

                case CombatEventKind.HealStarted:
                    PlayerHealsStarted++;
                    break;

                // 战斗的防御部分。这四类事件从一开始就发布到总线，却未被任何计数器统计，
                // 导致首领无法学习设计视为核心机制的弹反系统。
                case CombatEventKind.GuardRaised:
                    PlayerGuardsRaised++;
                    break;

                case CombatEventKind.Deflected:
                    PlayerDeflects++;
                    break;

                case CombatEventKind.Blocked:
                    PlayerBlocks++;
                    break;

                case CombatEventKind.Riposte:
                    PlayerRipostes++;
                    break;
            }
        }

        private void RecordBossEvent(in CombatEvent combatEvent)
        {
            switch (combatEvent.Kind)
            {
                case CombatEventKind.AttackLanded:
                    BossAttacksLanded++;
                    break;

                case CombatEventKind.AttackEvaded:
                    BossAttacksEvaded++;
                    break;

                case CombatEventKind.AttackWhiffed:
                    BossAttacksWhiffed++;
                    break;

                // 由攻击者的执行器发布，但行为者是防御方，所以被首领招架的挥击进入这里，
                // 而不是玩家计数。若弄反，就会把被招架的玩家错误记为成功弹反，
                // 并给弹反高手画像提供根本没有发生过的数值。
                case CombatEventKind.Parried:
                    BossParries++;
                    break;
            }
        }

        /// <summary>
        /// 查找指定行为者最近发生的某类事件。
        /// </summary>
        /// <param name="kind">要查找的事件类型。</param>
        /// <param name="actor">要查找谁的行为。</param>
        /// <param name="found">存在匹配事件时接收该事件。</param>
        /// <returns>保留了匹配事件时返回 true。</returns>
        public bool TryFindMostRecent(CombatEventKind kind, CombatantTeam actor, out CombatEvent found)
        {
            for (int i = _events.Count - 1; i >= 0; i--)
            {
                CombatEvent candidate = _events[i];

                if (candidate.Kind == kind && candidate.Actor == actor)
                {
                    found = candidate;
                    return true;
                }
            }

            found = default;
            return false;
        }

        /// <summary>清除全部记忆，供战斗重试使用。</summary>
        public void Clear()
        {
            _events.Clear();

            PlayerAttacksThrown = 0;
            PlayerHeavyAttacksThrown = 0;
            PlayerAttacksWhiffed = 0;
            PlayerDodges = 0;
            PlayerPerfectDodges = 0;
            PlayerHealsStarted = 0;
            PlayerGuardsRaised = 0;
            PlayerDeflects = 0;
            PlayerBlocks = 0;
            PlayerRipostes = 0;
            BossAttacksLanded = 0;
            BossAttacksEvaded = 0;
            BossAttacksWhiffed = 0;
            BossParries = 0;
        }
    }
}
