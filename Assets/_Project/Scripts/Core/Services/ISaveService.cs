namespace AdaptiveBossArena.Core.Services
{
    /// <summary>
    /// 跨会话持久化设置和纪录。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 采用抽象接口而非在调用处直接使用 <c>PlayerPrefs</c> 或 <c>File</c>，使存储后端、
    /// 序列化格式和架构版本迁移都集中在一处，测试也能替换为内存实现。
    /// </para>
    /// <para>
    /// 存档系统阶段引入的实现采用原子写入：先写临时文件，再替换原文件。
    /// 否则玩家写入过程中按 Alt+ F4 会留下截断的设置文件，导致游戏无法启动。
    /// </para>
    /// </remarks>
    public interface ISaveService
    {
        /// <summary>指定键下存在记录时为 true。</summary>
        /// <param name="key">记录名称。</param>
        /// <returns>记录是否存在。</returns>
        bool Exists(string key);

        /// <summary>写入记录，覆盖已有记录。</summary>
        /// <typeparam name="TData">可序列化的记录类型。</typeparam>
        /// <param name="key">记录名称。</param>
        /// <param name="data">记录内容。</param>
        void Save<TData>(string key, TData data) where TData : class;

        /// <summary>
        /// 读取记录。
        /// </summary>
        /// <typeparam name="TData">可序列化的记录类型。</typeparam>
        /// <param name="key">记录名称。</param>
        /// <param name="data">接收记录，不存在或无法读取时为 null。</param>
        /// <returns>
        /// 记录缺失或损坏时返回 false。调用者必须回退到默认值，不能把加载失败视为致命错误；
        /// 设置文件损坏的玩家仍应能进入主菜单。
        /// </returns>
        bool TryLoad<TData>(string key, out TData data) where TData : class;

        /// <summary>存在记录时将其删除。</summary>
        /// <param name="key">记录名称。</param>
        void Delete(string key);
    }
}
