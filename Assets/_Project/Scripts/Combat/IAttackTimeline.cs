using System;

namespace AdaptiveBossArena.Combat
{
    /// <summary>进行中攻击已到达的阶段。</summary>
    public enum AttackPhase
    {
        /// <summary>没有攻击运行。</summary>
        Inactive = 0,

        /// <summary>前摇：预警可见，攻击判定关闭。</summary>
        Startup = 1,

        /// <summary>攻击判定生效并检查接触。</summary>
        Active = 2,

        /// <summary>攻击判定已关闭，攻击者仍无法行动。</summary>
        Recovery = 3
    }

    /// <summary>
    /// 驱动攻击经过各阶段，并宣告每次切换。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是替代动画事件的接口接缝。战斗代码订阅<see cref="PhaseChanged"/>，不关心计时如何产生，
    /// 因此当前秒表驱动实现与未来动画驱动实现可以互换。
    /// </para>
    /// <para>
    /// 抽象的价值不限于占位美术。动画事件难以测试，低帧率时触发不稳定，片段重新导入后还会悄悄丢失。
    /// 把战斗逻辑隔在接口后，使帧数据无论使用哪种驱动，都可在编辑模式测试中验证。
    /// </para>
    /// </remarks>
    public interface IAttackTimeline
    {
        /// <summary>当前攻击所到达的阶段。</summary>
        AttackPhase Phase { get; }

        /// <summary>攻击正在运行时为 true。</summary>
        bool IsRunning { get; }

        /// <summary>攻击开始后经过的秒数。</summary>
        float ElapsedSeconds { get; }

        /// <summary>整个攻击的进度，范围为零至一。</summary>
        float NormalizedTime { get; }

        /// <summary>当前驱动的攻击，未激活时为 null。</summary>
        AttackDefinition CurrentAttack { get; }

        /// <summary>攻击处于连段接收窗口时为 true。</summary>
        bool IsInComboWindow { get; }

        /// <summary>攻击进入新阶段时触发。</summary>
        event Action<AttackPhase> PhaseChanged;

        /// <summary>攻击结束且攻击者可行动时触发一次。</summary>
        event Action Completed;

        /// <summary>开始驱动攻击，替换任何进行中的攻击。</summary>
        /// <param name="attack">要执行的攻击。</param>
        void Begin(AttackDefinition attack);

        /// <summary>推进时间线。</summary>
        /// <param name="deltaTime">经过缩放的时间增量，使顿帧正确冻结攻击进度。</param>
        void Tick(float deltaTime);

        /// <summary>
        /// 立即停止攻击，但不正常完成它。
        /// </summary>
        /// <remarks>攻击者挥击中被硬直打断时使用。</remarks>
        void Cancel();
    }
}
