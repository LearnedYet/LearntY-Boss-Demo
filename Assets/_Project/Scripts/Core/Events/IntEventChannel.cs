using UnityEngine;

namespace AdaptiveBossArena.Core.Events
{
    /// <summary>携带整数的事件通道，例如 Boss 阶段索引。</summary>
    /// <remarks>
    /// 每个类单独放在同名文件中。原因见 <see cref="VoidEventChannel"/>：
    /// 与其他类型放在一起的具体通道无法保存为正常工作的资源。
    /// </remarks>
    [CreateAssetMenu(
        fileName = "IntEventChannel",
        menuName = "Adaptive Boss Arena/Events/Int Event Channel",
        order = 2)]
    public sealed class IntEventChannel : EventChannel<int>
    {
    }
}
