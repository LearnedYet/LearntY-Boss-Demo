using UnityEngine;
using UnityEngine.UI;

namespace AdaptiveBossArena.UI
{
    /// <summary>
    /// 一行可重绑定操作，按钮显示当前按键，点击后监听新按键。
    /// </summary>
    /// <remarks>
    /// 每行用组件而非lambda，因为点击事件无参数，需要从回调找到所属行。组件保存动作、
    /// 绑定索引和结果标签，并交给拥有重绑定操作和输入资源的菜单。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RebindButton : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Name of the action this row rebinds.")]
        private string _actionName;

        [SerializeField]
        [Tooltip("Index of the binding within the action to rebind (the keyboard one).")]
        private int _bindingIndex;

        [SerializeField]
        [Tooltip("Label showing the current key, updated after a rebind.")]
        private Text _label;

        [SerializeField]
        [Tooltip("The menu that owns the input asset and the rebind operation.")]
        private SettingsMenu _menu;

        /// <summary>本行重绑定的动作。</summary>
        public string ActionName => _actionName;

        /// <summary>动作中的绑定索引。</summary>
        public int BindingIndex => _bindingIndex;

        /// <summary>开始本行重绑定，绑定本行按钮。</summary>
        public void OnClick() => _menu?.BeginRebind(this);

        /// <summary>写入显示的按键文字。</summary>
        /// <param name="text">可读绑定名称，监听时显示提示。</param>
        public void SetLabel(string text)
        {
            if (_label != null)
            {
                _label.text = text;
            }
        }

        /// <summary>指定本行信息，供界面生成器使用。</summary>
        /// <param name="actionName">要重绑定的动作。</param>
        /// <param name="bindingIndex">动作中的绑定索引。</param>
        /// <param name="label">显示当前按键的标签。</param>
        /// <param name="menu">设置菜单。</param>
        public void Configure(string actionName, int bindingIndex, Text label, SettingsMenu menu)
        {
            _actionName = actionName;
            _bindingIndex = bindingIndex;
            _label = label;
            _menu = menu;
        }
    }
}
