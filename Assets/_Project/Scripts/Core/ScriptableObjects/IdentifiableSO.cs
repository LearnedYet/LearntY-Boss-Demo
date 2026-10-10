using UnityEngine;

namespace AdaptiveBossArena.Core.ScriptableObjects
{
    /// <summary>
    /// 需要稳定标识和设计者备注的配置资源基类。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 标识在重命名和移动文件后仍保持稳定，因为存档和分析按标识引用策略与攻击。
    /// Unity 资源 GUID也能胜任，但在存档或调试面板中可读性差。
    /// </para>
    /// <para>
    /// 设计者备注不包含在构建中；它们用于让调参理由与值保存在一起，而非只存在于某个人的记忆中。
    /// </para>
    /// </remarks>
    public abstract class IdentifiableSO : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        [Tooltip("Stable identifier used by save data and debug tooling. Must be unique and must not " +
                 "change once the asset ships, or saved data will fail to resolve it.")]
        private string _id = string.Empty;

        [SerializeField]
        [TextArea(2, 6)]
        [Tooltip("Editor-only rationale for this asset's values. Not included in builds.")]
        private string _designerNotes = string.Empty;

        /// <summary>此资源的稳定标识。</summary>
        public string Id => _id;

        /// <summary>资源已设置标识时为 true。</summary>
        public bool HasValidId => !string.IsNullOrWhiteSpace(_id);

#if UNITY_EDITOR
        /// <summary>仅供编辑器使用的设计理由。</summary>
        public string DesignerNotes => _designerNotes;

        /// <summary>设置标识。仅供编辑器工具使用。</summary>
        /// <param name="id">要设置的标识。</param>
        public void EditorSetId(string id) => _id = id;

        /// <summary>标识为空时默认使用资源文件名。</summary>
        protected virtual void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_id))
            {
                _id = name;
            }
        }
#endif
    }
}
