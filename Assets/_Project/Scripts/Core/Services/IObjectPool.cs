namespace AdaptiveBossArena.Core.Services
{
    /// <summary>
    /// 对战斗中生成的对象复用实例，而非反复分配。
    /// </summary>
    /// <remarks>
    /// 命中特效、伤害数字和投射物往往在最需要稳定帧耗时的时候集中生成。
    /// 连招中实例化会在玩家判断操作响应的关键瞬间引发垃圾回收峰值。
    /// </remarks>
    /// <typeparam name="T">池中对象的类型。</typeparam>
    public interface IObjectPool<T> where T : class
    {
        /// <summary>当前可复用的实例数。</summary>
        int AvailableCount { get; }

        /// <summary>当前已借出的实例数。</summary>
        int ActiveCount { get; }

        /// <summary>从池取出实例，没有空闲对象时新建。</summary>
        /// <returns>可直接使用的实例。</returns>
        T Rent();

        /// <summary>归还实例以供复用。</summary>
        /// <param name="instance">要归还的实例，必须来自此池。</param>
        void Return(T instance);

        /// <summary>提前创建实例，避免首次使用时卡顿。</summary>
        /// <param name="count">要创建的实例数。</param>
        void Prewarm(int count);

        /// <summary>销毁池中全部实例。</summary>
        void Clear();
    }
}
