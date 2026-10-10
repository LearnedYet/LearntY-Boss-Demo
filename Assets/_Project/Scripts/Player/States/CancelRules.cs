using AdaptiveBossArena.Combat;

namespace AdaptiveBossArena.Player.States
{
    /// <summary>
    /// 骑士何时可以退出当前动作。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 玩家对旧方案的评价是“无法真正控制角色”：防御必须维持固定最短时间，翻滚只能以攻击结束，
    /// 治疗无法放弃，攻击永远不能通过防御退出。投入仍然重要：攻击前摇和生效帧无法逃脱，
    /// 放弃治疗也会损失一次治疗；但现在每个动作在付出其投入代价后都有退出途径。
    /// </para>
    /// <para>
    /// 保持为纯逻辑，使规则表可脱离场景测试；集中在一处，使窗口可统一审阅。
    /// </para>
    /// </remarks>
    public static class CancelRules
    {
        /// <summary>攻击后摇进行到何种比例时可以举起防御。</summary>
        public const float AttackGuardRecoveryFraction = 0.5f;

        /// <summary>治疗进行到何种比例前可以通过闪避放弃。</summary>
        public const float HealDodgeFraction = 0.4f;

        /// <summary>攻击是否可以通过闪避退出。</summary>
        /// <remarks>攻击结束后即可：整个后摇阶段都允许。</remarks>
        /// <param name="phase">攻击当前阶段。</param>
        /// <returns>闪避可以打断时为 true。</returns>
        public static bool AttackCanDodge(AttackPhase phase) => phase == AttackPhase.Recovery;

        /// <summary>攻击是否可以通过防御退出。</summary>
        /// <remarks>比闪避更晚：防御比翻滚安全，因此必须承担更多后摇。</remarks>
        /// <param name="phase">攻击当前阶段。</param>
        /// <param name="elapsedSeconds">攻击开始后经过的时间。</param>
        /// <param name="attack">正在进行的攻击。</param>
        /// <returns>防御可以打断时为 true。</returns>
        public static bool AttackCanGuard(AttackPhase phase, float elapsedSeconds, AttackDefinition attack)
        {
            if (phase != AttackPhase.Recovery || attack == null)
            {
                return false;
            }

            float intoRecovery = elapsedSeconds - attack.StartupSeconds - attack.ActiveSeconds;
            return intoRecovery >= attack.RecoverySeconds * AttackGuardRecoveryFraction;
        }

        /// <summary>翻滚是否已减速到可以退出并执行其它动作。</summary>
        /// <param name="timeInRollSeconds">翻滚开始后经过的时间。</param>
        /// <param name="rollSeconds">翻滚完整持续时间。</param>
        /// <param name="cancelFraction">翻滚进行到何种比例时可以退出。</param>
        /// <returns>攻击、防御或另一翻滚可以打断时为 true。</returns>
        public static bool RollCanBeLeft(float timeInRollSeconds, float rollSeconds, float cancelFraction) =>
            timeInRollSeconds >= rollSeconds * cancelFraction;

        /// <summary>治疗是否仍可通过闪避放弃。</summary>
        /// <param name="timeInHealSeconds">治疗开始后经过的时间。</param>
        /// <param name="healSeconds">治疗的完整引导时长。</param>
        /// <returns>引导早期、尚未投入药水时为 true。</returns>
        public static bool HealCanDodge(float timeInHealSeconds, float healSeconds) =>
            timeInHealSeconds <= healSeconds * HealDodgeFraction;
    }
}
