using UnityEngine;

namespace AdaptiveBossArena.Core.Events
{
    /// <summary>携带布尔标志的事件通道，例如游戏是否暂停。</summary>
    /// <remarks>
    /// 每个类单独放在同名文件中。原因见 <see cref="VoidEventChannel"/>：
    /// 与其他类型放在一起的具体通道无法保存为正常工作的资源。
    /// </remarks>
    [CreateAssetMenu(
        fileName = "BoolEventChannel",
        menuName = "Adaptive Boss Arena/Events/Bool Event Channel",
        order = 3)]
    public sealed class BoolEventChannel : EventChannel<bool>
    {
    }
}
