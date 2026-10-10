using System;
using UnityEngine;

namespace AdaptiveBossArena.Core.Events
{
    /// <summary>不携带载荷的事件通道。</summary>
    /// <remarks>
    /// 放在与类同名的独立文件中，因为 Unity 仅为名称匹配文件的类型创建 <c>MonoScript</c>。
    /// 与其他类并列声明的具体 <see cref="ScriptableObject"/>无法保存成正常资源：
    /// 资源写入 <c>m_Script : {fileID: 0}</c>，加载为 null，所有引用也悄悄变为 null，且完全不报错。
    /// 本项目全部通道曾因此损坏，整个信号层失效，控制台却没有异常。
    /// </remarks>
    [CreateAssetMenu(
        fileName = "VoidEventChannel",
        menuName = "Adaptive Boss Arena/Events/Void Event Channel",
        order = 0)]
    public sealed class VoidEventChannel : EventChannelBase
    {
        /// <summary>通道触发时调用。</summary>
        public event Action Raised;

        /// <summary>当前连接的监听者数量。</summary>
        public int ListenerCount => Raised?.GetInvocationList().Length ?? 0;

        /// <summary>触发通道。</summary>
        public void Raise()
        {
            if (LogRaises)
            {
                Debug.Log($"[EventChannel] {name} raised to {ListenerCount} listener(s).", this);
            }

            Raised?.Invoke();
        }

        /// <summary>
        /// 资源卸载时移除全部监听者。
        /// </summary>
        /// <remarks>
        /// 编辑器中 ScriptableObject 资源的生命周期长于运行模式。若不清理，
        /// 来自已销毁场景对象的委托会留到下一次运行，调用失效引用，产生构建版本中无法复现的错误。
        /// </remarks>
        private void OnDisable() => Raised = null;
    }
}
