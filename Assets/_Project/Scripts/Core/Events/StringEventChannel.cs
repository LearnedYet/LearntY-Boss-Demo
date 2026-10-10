using UnityEngine;

namespace AdaptiveBossArena.Core.Events
{
    /// <summary>携带字符串的事件通道，例如新适应策略的提示。</summary>
    /// <remarks>
    /// 每个类单独放在同名文件中。原因见 <see cref="VoidEventChannel"/>：
    /// 与其他类型放在一起的具体通道无法保存为正常工作的资源。
    /// </remarks>
    [CreateAssetMenu(
        fileName = "StringEventChannel",
        menuName = "Adaptive Boss Arena/Events/String Event Channel",
        order = 4)]
    public sealed class StringEventChannel : EventChannel<string>
    {
    }
}
