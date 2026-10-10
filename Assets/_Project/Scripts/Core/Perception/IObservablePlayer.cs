namespace AdaptiveBossArena.Core.Perception
{
    /// <summary>
    /// 由玩家实现，暴露 Boss 唯一允许持有的玩家视图。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 玩家程序集实现此接口，AI 和 Learning消费它，游戏组合根连接两端。
    /// 由于 AI 和Learning 都不引用玩家程序集，此接口不仅是首选路径，也是唯一可达的玩家状态访问路径。
    /// </para>
    /// <para>
    /// 不能添加任何人类对手无法通过观察屏幕感知的信息。每项扩展都可能在隔离屏障上打开缺口。
    /// </para>
    /// </remarks>
    public interface IObservablePlayer
    {
        /// <summary>捕获旁观者此刻可感知的玩家信息。</summary>
        /// <param name="timestamp">用于给快照加时间戳的战斗时钟时间。</param>
        /// <returns>已填充的快照。</returns>
        PlayerObservation CaptureObservation(float timestamp);
    }

    /// <summary>
    /// 向 Boss 提供刻意滞后的玩家观察。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 隔离屏障的第二层。即使只包含可见信息，按当前帧数据行动的 Boss 仍会产生超出人类的即时反应，
    /// 看起来像作弊。因此给 AI的每次观察都会延后一个感知延迟，使Boss 始终响应玩家稍早前的行为。
    /// </para>
    /// <para>
    /// 值得保留的效果：玩家确实可以诱骗 Boss，因为Boss 在延迟窗口内对佯攻作出的承诺无法撤回。
    /// </para>
    /// </remarks>
    public interface IPerceptionSource
    {
        /// <summary>感知数据落后当前时刻的秒数。</summary>
        float LatencySeconds { get; }

        /// <summary>取得 Boss 允许用于行动的最新观察。</summary>
        /// <param name="observation">接收延迟后的快照。</param>
        /// <returns>历史尚未积累到足以满足延迟时返回 false。</returns>
        bool TryGetPerceived(out PlayerObservation observation);

        /// <summary>
        /// 取得更早的观察，用于估计玩家移动的变化。
        /// </summary>
        /// <param name="secondsFurtherBack">标准延迟之外额外回看的时长。</param>
        /// <param name="observation">接收延迟后的快照。</param>
        /// <returns>历史未覆盖那么早的时间时返回 false。</returns>
        bool TryGetPerceivedAt(float secondsFurtherBack, out PlayerObservation observation);
    }
}
