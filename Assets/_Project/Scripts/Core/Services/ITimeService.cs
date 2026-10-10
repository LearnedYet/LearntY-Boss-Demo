namespace AdaptiveBossArena.Core.Services
{
    /// <summary>
    /// 游戏时间的唯一管理者，涵盖命中停顿、慢动作和暂停。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 项目中其他类型不能写入 <c>Time.timeScale</c>。命中停顿、完美闪避慢动作和暂停菜单都要控制时间；
    /// 若各自直接设置全局倍率，就会出现解除暂停时因停顿仍在进行而恢复错误速度的经典问题。
    /// 所有请求统一经过一个服务，使效果能够组合而非冲突。
    /// </para>
    /// <para>
    /// 调用者应明确选择时钟：战斗逻辑使用 <see cref="DeltaTime"/>，因此命中停顿时冻结；UI、
    /// 相机震动和暂停菜单使用 <see cref="UnscaledDeltaTime"/>，因此继续播放动画。
    /// </para>
    /// </remarks>
    public interface ITimeService
    {
        /// <summary>应用命中停顿、慢动作和暂停后的帧时间差。</summary>
        float DeltaTime { get; }

        /// <summary>不受任何时间操作影响的帧时间差。</summary>
        float UnscaledDeltaTime { get; }

        /// <summary>应用时间操作后的物理步长。</summary>
        float FixedDeltaTime { get; }

        /// <summary>
        /// 战斗开始后经过的缩放时间秒数。
        /// </summary>
        /// <remarks>
        /// 全部玩家观察使用此时钟加时间戳，因此感知延迟与战斗处于相同时间尺度。
        /// 命中停顿也会暂停Boss 感知，而非悄悄提供更新数据。
        /// </remarks>
        float CombatTime { get; }

        /// <summary>当前应用到真实时间的倍率。</summary>
        float TimeScale { get; }

        /// <summary>游戏暂停期间为 true。</summary>
        bool IsPaused { get; }

        /// <summary>
        /// 短暂冻结时间，表现命中的重量感。
        /// </summary>
        /// <remarks>
        /// 请求不会排队。较长请求覆盖正在进行的较短请求，使轻击停顿期间的重击能延长冻结而不被吞掉。
        /// </remarks>
        /// <param name="seconds">冻结持续的真实时间。</param>
        void RequestHitStop(float seconds);

        /// <summary>在固定时长内减慢时间，用于奖励完美闪避。</summary>
        /// <param name="scale">要应用的倍率，一表示正常速度。</param>
        /// <param name="durationSeconds">效果持续的真实时间。</param>
        void RequestSlowMotion(float scale, float durationSeconds);

        /// <summary>取消当前生效的命中停顿或慢动作。</summary>
        void ClearTimeEffects();

        /// <summary>暂停或恢复游戏。</summary>
        /// <param name="paused">为 true 时暂停。</param>
        void SetPaused(bool paused);

        /// <summary>重置战斗时钟，例如重试战斗时。</summary>
        void ResetCombatClock();
    }
}
