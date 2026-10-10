using UnityEngine;

namespace AdaptiveBossArena.Core.Events
{
    /// <summary>携带浮点值的事件通道，例如归一化生命比例。</summary>
    /// <remarks>
    /// 放在与类同名的独立文件中，因为 Unity 仅为名称匹配文件的类型创建 <c>MonoScript</c>。
    /// 其他做法的问题见 <see cref="VoidEventChannel"/>：简而言之，资源保存时没有脚本引用，
    /// 加载后成为 null。
    /// </remarks>
    [CreateAssetMenu(
        fileName = "FloatEventChannel",
        menuName = "Adaptive Boss Arena/Events/Float Event Channel",
        order = 1)]
    public sealed class FloatEventChannel : EventChannel<float>
    {
    }
}
