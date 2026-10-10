using UnityEngine;

namespace AdaptiveBossArena.Core.Events
{
    /// <summary>
    /// 基于资源的事件通道基类，用于解耦广播者与监听者。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 通道是一个资源，因此广播者和监听者各自引用通道，而不互相引用。
    /// 这使 HUD 无需 UI程序集了解 Boss 也能显示其生命，使胜利界面能够响应从未直接订阅的死亡事件。
    /// </para>
    /// <para>
    /// 通道用于跨系统和 UI 的低频信号。攻击区域重叠结果等每帧战斗数据应通过接口上的直接 C # 事件传递
    /// ，额外间接层在这里不值得其成本。
    /// </para>
    /// </remarks>
    public abstract class EventChannelBase : ScriptableObject
    {
        [SerializeField]
        [TextArea(2, 5)]
        [Tooltip("What this channel signals and who is expected to listen. Editor-only documentation.")]
        private string _description = string.Empty;

        [SerializeField]
        [Tooltip("Log every raise to the console. Useful when a signal appears not to arrive.")]
        private bool _logRaises;

        /// <summary>是否将事件触发写入控制台。</summary>
        protected bool LogRaises => _logRaises;

#if UNITY_EDITOR
        /// <summary>仅供编辑器使用的通道用途描述。</summary>
        public string Description => _description;
#endif
    }
}
