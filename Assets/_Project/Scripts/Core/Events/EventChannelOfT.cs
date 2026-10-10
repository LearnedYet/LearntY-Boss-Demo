using System;
using UnityEngine;

namespace AdaptiveBossArena.Core.Events
{
    /// <summary>
    /// 携带载荷的资源事件通道基类。
    /// </summary>
    /// <remarks>
    /// 具体通道是封闭泛型参数的一行子类，因为 Unity 不能把开放泛型类型序列化为资源。
    /// 子类仅用于给封闭类型一个可附加<c>CreateAssetMenu</c> 的名称。
    /// </remarks>
    /// <typeparam name="TPayload">随信号传递的数据。</typeparam>
    public abstract class EventChannel<TPayload> : EventChannelBase
    {
        /// <summary>通道触发时调用，并携带载荷。</summary>
        public event Action<TPayload> Raised;

        /// <summary>当前连接的监听者数量。</summary>
        public int ListenerCount => Raised?.GetInvocationList().Length ?? 0;

        /// <summary>携带载荷触发通道。</summary>
        /// <param name="payload">要投递给监听者的数据。</param>
        public void Raise(TPayload payload)
        {
            if (LogRaises)
            {
                Debug.Log($"[EventChannel] {name} raised with '{payload}' to {ListenerCount} listener(s).", this);
            }

            Raised?.Invoke(payload);
        }

        /// <summary>资源卸载时移除全部监听者，避免跨会话泄漏。</summary>
        private void OnDisable() => Raised = null;
    }
}
