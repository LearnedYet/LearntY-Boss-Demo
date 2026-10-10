using AdaptiveBossArena.UI;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 构建设置面板及其即时应用菜单组件。
    /// </summary>
    /// <remarks>
    /// HUD 与标题页共用，确保从各处打开时外观和行为一致，面板只描述一次。
    /// 各控件用持久监听器连接菜单方法，只有这种监听器能从编辑器脚本烘焙进场景并保留。
    /// </remarks>
    internal static class SettingsMenuBuilder
    {
        // 几乎完全不透明。起初 0.85 时，背后的亮标题和按钮透出，使面板像错误而非界面。
        private static readonly Color PanelColor = new Color(0.03f, 0.03f, 0.05f, 0.985f);
        private static readonly Color Accent = new Color(0.45f, 0.85f, 0.95f);
        private static readonly Color ToggleAccent = new Color(0.55f, 0.9f, 0.6f);
        private static readonly Color MutedText = new Color(0.72f, 0.76f, 0.82f);

        private const float LabelX = -150f;
        private const float ControlX = 150f;
        /// <summary>原为 58；添加镜头设置行时收紧，确保面板仍在屏幕边缘内。</summary>
        private const float RowSpacing = 46f;

        /// <summary>在画布下构建面板，返回已连接菜单。</summary>
        /// <param name="root">构建所用画布 Transform。</param>
        /// <param name="actions">用于重绑覆盖的共享输入动作资产。</param>
        /// <returns>面板隐藏的设置菜单。</returns>
        public static SettingsMenu Build(Transform root, InputActionAsset actions)
        {
            GameObject panel = CreateFullScreenPanel(root, "SettingsPanel");

            Text title = UiBuilder.CreateText(panel.transform, "Title", "设置", 56, TextAnchor.MiddleCenter);
            title.rectTransform.anchoredPosition = new Vector2(0f, 460f);
            title.fontStyle = FontStyle.Bold;

            float y = 350f;

            Slider master = LabelledSlider(panel.transform, "Master Volume", "总音量", ref y);
            Slider music = LabelledSlider(panel.transform, "Music Volume", "音乐音量", ref y);
            Slider effects = LabelledSlider(panel.transform, "Effects Volume", "音效音量", ref y);
            Slider shake = LabelledSlider(panel.transform, "Screen Shake", "画面震动", ref y);
            Toggle reducedFlashing = LabelledToggle(panel.transform, "Reduced Flashing", "减少闪烁", ref y);
            Toggle extendedTell = LabelledToggle(panel.transform, "Longer Boss Tells", "延长策略提示时长", ref y);

            // 摄像机转动速度，范围为默认的四分之一到两倍半。
            Slider sensitivity = LabelledSlider(panel.transform, "Camera Sensitivity", "镜头灵敏度", ref y);
            sensitivity.minValue = Core.Services.LookSettings.MinimumSensitivity;
            sensitivity.maxValue = Core.Services.LookSettings.MaximumSensitivity;
            sensitivity.value = Core.Services.LookSettings.DefaultSensitivity;
            Toggle invertLook = LabelledToggle(panel.transform, "Invert Look", "反转视角", ref y);

            // Low、Medium、High 按整数步长，旁边标示当前生效档位。
            float qualityRow = y;
            Slider quality = LabelledSlider(panel.transform, "Graphics Quality", "画质", ref y);
            quality.minValue = 0f;
            quality.maxValue = 2f;
            quality.wholeNumbers = true;
            Text qualityLabel = UiBuilder.CreateText(panel.transform, "GraphicsQualityValue", "中", 20, TextAnchor.MiddleLeft);
            qualityLabel.rectTransform.anchoredPosition = new Vector2(ControlX + 250f, qualityRow);
            qualityLabel.rectTransform.sizeDelta = new Vector2(180f, 30f);
            qualityLabel.color = MutedText;

            // 菜单必须在重绑行之前存在，因为每行持有其引用，滑杆回调也在此连接。
            var menu = root.gameObject.AddComponent<SettingsMenu>();
            menu.Bind(
                panel, master, music, effects, shake, reducedFlashing, extendedTell, actions,
                sensitivity, invertLook, quality, qualityLabel);

            WireSlider(master, menu.SetMasterVolume);
            WireSlider(music, menu.SetMusicVolume);
            WireSlider(effects, menu.SetEffectsVolume);
            WireSlider(shake, menu.SetShakeIntensity);
            WireToggle(reducedFlashing, menu.SetReducedFlashing);
            WireToggle(extendedTell, menu.SetExtendedTell);
            WireSlider(sensitivity, menu.SetLookSensitivity);
            WireToggle(invertLook, menu.SetInvertLook);
            WireSlider(quality, menu.SetGraphicsQuality);

            BuildRebindSection(panel.transform, menu, ref y);

            UiBuilder.CreateButton(panel.transform, "Back", "返回", new Vector2(0f, -470f), menu.Close);

            panel.SetActive(false);
            return menu;
        }

        /// <summary>在同一行创建右对齐文字和滑杆，并推进位置。</summary>
        private static Slider LabelledSlider(Transform parent, string name, string caption, ref float y)
        {
            RowLabel(parent, name, caption, y);
            Slider slider = UiBuilder.CreateSlider(parent, name, new Vector2(ControlX, y), Accent);
            y -= RowSpacing;
            return slider;
        }

        /// <summary>在同一行创建右对齐文字和开关，并推进位置。</summary>
        private static Toggle LabelledToggle(Transform parent, string name, string caption, ref float y)
        {
            RowLabel(parent, name, caption, y);
            Toggle toggle = UiBuilder.CreateToggle(parent, name, new Vector2(ControlX - 150f, y), ToggleAccent);
            y -= RowSpacing;
            return toggle;
        }

        /// <summary>为一行放置右对齐说明文字。</summary>
        private static void RowLabel(Transform parent, string name, string caption, float y)
        {
            Text label = UiBuilder.CreateText(parent, $"{name} Label", caption, 26, TextAnchor.MiddleRight);
            label.rectTransform.anchoredPosition = new Vector2(LabelX, y);
            label.rectTransform.sizeDelta = new Vector2(280f, 40f);
        }

        /// <summary>构建可重绑控制行、固定控制参考和重置按钮。</summary>
        private static void BuildRebindSection(Transform parent, SettingsMenu menu, ref float y)
        {
            y -= 22f;

            Text header = UiBuilder.CreateText(
                parent, "ControlsHeader", "操作设置 — 点击按键重新绑定", 24, TextAnchor.MiddleCenter);
            header.rectTransform.anchoredPosition = new Vector2(0f, y);
            header.fontStyle = FontStyle.Bold;
            header.color = MutedText;
            y -= 46f;

            RebindRow(parent, "Dash", "闪避", "Dash", menu, ref y);
            RebindRow(parent, "Guard / Deflect", "格挡 / 弹反", "Guard", menu, ref y);
            RebindRow(parent, "Special", "特殊攻击", "Special", menu, ref y);
            RebindRow(parent, "Heal", "治疗", "Heal", menu, ref y);
            RebindRow(parent, "Swap Weapon", "切换武器", "SwapWeapon", menu, ref y);

            // 不可重绑的移动、鼠标攻击和摄像机按键也作参考显示，让面板是完整操作列表而不只是重绑菜单。
            Text fixedControls = UiBuilder.CreateText(
                parent, "FixedControls",
                "移动 WASD  轻攻击 鼠标左键  重攻击 Shift + 鼠标左键  格挡 鼠标右键  " +
                "招架 Q  锁定 Tab / 鼠标中键  镜头 C  暂停 Esc",
                19, TextAnchor.MiddleCenter);
            fixedControls.rectTransform.anchoredPosition = new Vector2(0f, y - 4f);
            fixedControls.rectTransform.sizeDelta = new Vector2(1400f, 28f);
            fixedControls.color = MutedText;
            y -= 44f;

            UiBuilder.CreateButton(parent, "ResetControls", "重置按键", new Vector2(0f, y - 4f), menu.ResetControls);
            y -= 44f;
        }

        /// <summary>构建一行重绑定：说明文字和显示当前按键的按钮。</summary>
        private static void RebindRow(
            Transform parent, string name, string caption, string actionName, SettingsMenu menu, ref float y)
        {
            Text label = UiBuilder.CreateText(parent, $"{name} Label", caption, 22, TextAnchor.MiddleRight);
            label.rectTransform.anchoredPosition = new Vector2(LabelX, y);
            label.rectTransform.sizeDelta = new Vector2(280f, 32f);

            var buttonObject = new GameObject($"{name} Rebind", typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(ControlX, y);
            rect.sizeDelta = new Vector2(190f, 32f);

            buttonObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);

            Text key = UiBuilder.CreateText(buttonObject.transform, "Key", "—", 22, TextAnchor.MiddleCenter);
            key.rectTransform.sizeDelta = new Vector2(190f, 32f);

            var rebind = buttonObject.AddComponent<RebindButton>();

            // 绑定索引 0 是键盘，因为生成器先添加键盘，再添加手柄。
            rebind.Configure(actionName, 0, key, menu);

            UnityEventTools.AddPersistentListener(buttonObject.GetComponent<Button>().onClick, rebind.OnClick);

            y -= 44f;
        }

        /// <summary>创建带深色背景的全屏面板。</summary>
        private static GameObject CreateFullScreenPanel(Transform parent, string name)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);

            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            panel.GetComponent<Image>().color = PanelColor;
            return panel;
        }

        private static void WireSlider(Slider slider, UnityEngine.Events.UnityAction<float> onChanged) =>
            UnityEventTools.AddPersistentListener(slider.onValueChanged, onChanged);

        private static void WireToggle(Toggle toggle, UnityEngine.Events.UnityAction<bool> onChanged) =>
            UnityEventTools.AddPersistentListener(toggle.onValueChanged, onChanged);
    }
}
