using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 项目共用生成器，生成每个界面需要的两种 UI 基础元素。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 抽出共用实现，让 HUD 和标题页生成一致文字与按钮，不各自维护近似副本。
    /// 两者由代码构建的理由与场景相同：手写画布是一堆在差异中难以理解的锚点数值。
    /// </para>
    /// <para>
    /// 文字使用随附动态 Noto Sans SC，确保编辑器和构建中显示中文，不依赖玩家设备安装字体。
    /// </para>
    /// </remarks>
    internal static class UiBuilder
    {
        private const string FontAssetPath =
            "Assets/_Project/Art/ThirdParty/NotoSansSC/NotoSansSC-Regular.otf";

        /// <summary>用随附中文字体创建居中文字元素。</summary>
        /// <param name="parent">文字所挂载的父 Transform。</param>
        /// <param name="name">对象名称。</param>
        /// <param name="content">初始文字。</param>
        /// <param name="fontSize">以参考像素计的字号。</param>
        /// <param name="alignment">文字对齐方式。</param>
        /// <returns>创建的文字组件。</returns>
        public static Text CreateText(
            Transform parent,
            string name,
            string content,
            int fontSize,
            TextAnchor alignment)
        {
            var textObject = new GameObject(name, typeof(Text));
            textObject.transform.SetParent(parent, false);

            var text = textObject.GetComponent<Text>();
            text.text = content;
            text.font = AssetDatabase.LoadAssetAtPath<Font>(FontAssetPath);
            if (text.font == null)
            {
                throw new System.InvalidOperationException($"UI font is missing: {FontAssetPath}");
            }
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1000f, 80f);

            return text;
        }

        /// <summary>创建带标签并绑定回调的按钮。</summary>
        /// <param name="parent">按钮所挂载的父 Transform。</param>
        /// <param name="name">对象名称。</param>
        /// <param name="label">按钮文字。</param>
        /// <param name="anchoredPosition">相对中心的位置。</param>
        /// <param name="onClick">点击调用的方法，必须是序列化对象上的公开方法。</param>
        /// <returns>创建的按钮，供需要后续引用的调用者使用。</returns>
        public static Button CreateButton(
            Transform parent,
            string name,
            string label,
            Vector2 anchoredPosition,
            UnityEngine.Events.UnityAction onClick)
        {
            var buttonObject = new GameObject(name, typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(320f, 56f);

            // 暗铁色配细骨白边，悬停时变暖。半透明白块在所有带按钮界面都像占位。
            buttonObject.GetComponent<Image>().color = Color.white;
            buttonObject.AddComponent<Outline>().effectColor = new Color(0.62f, 0.52f, 0.4f, 0.55f);

            Text text = CreateText(buttonObject.transform, "Label", label, 26, TextAnchor.MiddleCenter);
            text.rectTransform.sizeDelta = new Vector2(320f, 56f);
            text.color = new Color(0.86f, 0.8f, 0.7f);

            // 使用 AddPersistentListener 而非 AddListener。此代码在编辑时将场景写入磁盘，
            // 普通AddListener 仅注册运行时回调，不序列化；重载后所有按钮 onClick 为空，
            // 无论代码连接看似多正确。持久监听器存于场景，等同将方法拖入 Inspector。
            var button = buttonObject.GetComponent<Button>();
            button.colors = new ColorBlock
            {
                normalColor = new Color(0.07f, 0.05f, 0.05f, 0.82f),
                highlightedColor = new Color(0.32f, 0.1f, 0.06f, 0.9f),
                pressedColor = new Color(0.5f, 0.14f, 0.07f, 1f),
                selectedColor = new Color(0.24f, 0.08f, 0.05f, 0.9f),
                disabledColor = new Color(0.05f, 0.05f, 0.05f, 0.5f),
                colorMultiplier = 1f,
                fadeDuration = 0.12f
            };
            UnityEventTools.AddPersistentListener(button.onClick, onClick);

            return button;
        }

        /// <summary>
        /// 创建纯色、无精灵风格的水平滑杆。
        /// </summary>
        /// <remarks>
        /// 通过以下方式构建： <see cref="DefaultControls"/> ，不手工组合。
        /// 滑杆填充和手柄有复杂嵌套锚点，由组件运行时驱动；任一错误都会使控件看似正确却不动。
        /// 让 Unity 建立已知正确层级，再只改色，可消除此类错误。
        /// </remarks>
        /// <param name="parent">要挂载到的父 Transform。</param>
        /// <param name="name">对象名称。</param>
        /// <param name="anchoredPosition">相对中心的位置。</param>
        /// <param name="accent">填充部分颜色。</param>
        /// <param name="width">滑杆宽度。</param>
        /// <returns>零到一范围的滑杆。</returns>
        public static Slider CreateSlider(
            Transform parent, string name, Vector2 anchoredPosition, Color accent, float width = 340f)
        {
            GameObject sliderObject = DefaultControls.CreateSlider(new DefaultControls.Resources());
            sliderObject.name = name;
            sliderObject.transform.SetParent(parent, false);

            var rect = (RectTransform)sliderObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(width, 22f);

            var slider = sliderObject.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;

            Recolour(sliderObject.transform, "Fill Area/Fill", accent);
            Recolour(sliderObject.transform, "Background", new Color(1f, 1f, 1f, 0.16f));
            Recolour(sliderObject.transform, "Handle Slide Area/Handle", Color.white);

            return slider;
        }

        /// <summary>
        /// 创建纯色、无精灵风格、不带内置文字的复选开关。
        /// </summary>
        /// <param name="parent">要挂载到的父 Transform。</param>
        /// <param name="name">对象名称。</param>
        /// <param name="anchoredPosition">相对中心的位置。</param>
        /// <param name="accent">开启时勾选标记的颜色。</param>
        /// <returns>开关。</returns>
        public static Toggle CreateToggle(
            Transform parent, string name, Vector2 anchoredPosition, Color accent)
        {
            GameObject toggleObject = DefaultControls.CreateToggle(new DefaultControls.Resources());
            toggleObject.name = name;
            toggleObject.transform.SetParent(parent, false);

            var rect = (RectTransform)toggleObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(30f, 30f);

            // 默认开关自带 Text 标签，设置行有独立标签，因此禁用默认标签，避免在真实文字旁显示空框。
            Transform label = toggleObject.transform.Find("Label");
            if (label != null)
            {
                label.gameObject.SetActive(false);
            }

            Recolour(toggleObject.transform, "Background", new Color(1f, 1f, 1f, 0.2f));
            Recolour(toggleObject.transform, "Background/Checkmark", accent);

            return toggleObject.GetComponent<Toggle>();
        }

        /// <summary>为指定名称的后代 Image 改色，允许对象缺失。</summary>
        private static void Recolour(Transform root, string path, Color color)
        {
            Transform target = root.Find(path);
            var image = target != null ? target.GetComponent<Image>() : null;

            if (image != null)
            {
                image.color = color;
            }
        }
    }
}
