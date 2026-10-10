using AdaptiveBossArena.Core.Constants;
using AdaptiveBossArena.Core.Services;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AdaptiveBossArena.UI
{
    /// <summary>
    /// 设置面板：音频、辅助功能和重绑定，即时应用并持久保存。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 标题和暂停菜单均可进入，两场景都包含它，因为它还负责场景加载时<em>应用</em>存档设置。
    /// 玩家设置过偏好后，希望每次启动都生效，无需再次打开面板。
    /// </para>
    /// <para>
    /// 通过窄接口访问系统：音频和镜头震动通过服务接口，
    /// 辅助功能通过战斗反馈读取的全局<see cref="FeedbackSettings"/>，
    /// 避免菜单引用每个受击闪光和镜头对象。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SettingsMenu : MonoBehaviour
    {
        /// <summary>启用延长适应提示选项时的停留倍率。</summary>
        private const float ExtendedTellMultiplier = 2f;

        /// <summary>包含可重绑定操作的玩法动作映射名称。</summary>
        private const string GameplayMapName = "Gameplay";

        [Header("Panel")]
        [SerializeField]
        [Tooltip("Root object shown while the settings are open.")]
        private GameObject _panel;

        [Header("Audio")]
        [SerializeField]
        private Slider _masterSlider;

        [SerializeField]
        private Slider _musicSlider;

        [SerializeField]
        private Slider _effectsSlider;

        [Header("Accessibility")]
        [SerializeField]
        private Slider _shakeSlider;

        [SerializeField]
        private Toggle _reducedFlashingToggle;

        [SerializeField]
        private Toggle _extendedTellToggle;

        [Header("Input")]
        [SerializeField]
        [Tooltip("The shared input actions asset, so rebindings reach the controls the game reads.")]
        private InputActionAsset _actions;

        private ISaveService _saveService;
        private SettingsData _settings;

        private Slider _sensitivitySlider;
        private Toggle _invertLookToggle;
        private Slider _qualitySlider;
        private Text _qualityLabel;

        private InputActionMap _gameplayMap;
        private RebindButton[] _rebindRows;
        private InputActionRebindingExtensions.RebindingOperation _activeRebind;

        // 从存档写入控件时设置，避免变化回调将程序同步当作玩家编辑并覆盖存档。
        private bool _isSyncing;

        /// <summary>设置面板打开时为true。</summary>
        public bool IsOpen => _panel != null && _panel.activeSelf;

        private void Start()
        {
            ServiceRegistry.Current.TryGet(out _saveService);

            _settings = Load();

            ApplyInputOverrides();
            ApplyAll();
            SyncControls();

            _gameplayMap = _actions != null ? _actions.FindActionMap(GameplayMapName, throwIfNotFound: false) : null;
            _rebindRows = GetComponentsInChildren<RebindButton>(includeInactive: true);
            RefreshRebindLabels();

            Close();
        }

        private void OnDestroy()
        {
            // 组件销毁后仍运行的操作会持有已销毁回调目标，必须释放，不能直接遗弃。
            _activeRebind?.Dispose();
            _activeRebind = null;
        }

        /// <summary>
        /// 将设置面板显示在来源界面上方。
        /// </summary>
        /// <remarks>
        /// 每次打开移到最后一个同级节点。Unity后面的同级对象覆盖前面的对象，
        /// 而标题按钮和暂停菜单可能生成得更晚；否则它们会穿透设置面板显示。
        /// </remarks>
        public void Open()
        {
            RaiseToTop();

            if (_panel != null)
            {
                _panel.SetActive(true);
            }
        }

        /// <summary>
        /// 将面板移到同级对象上方，避免其他内容穿透。
        /// </summary>
        /// <remarks>
        /// 生成时和每次打开时都调用，保证保存场景和后续动态组装的排序正确。
        /// </remarks>
        public void RaiseToTop()
        {
            if (_panel != null)
            {
                _panel.transform.SetAsLastSibling();
            }
        }

        /// <summary>隐藏设置面板。</summary>
        public void Close()
        {
            if (_panel != null)
            {
                _panel.SetActive(false);
            }
        }

        // --- 控件回调，由界面生成器绑定到滑块和开关。---

        /// <summary>通过滑块设置主音量。</summary>
        /// <param name="value">线性音量，范围零到一。</param>
        public void SetMasterVolume(float value)
        {
            if (_isSyncing) return;
            _settings.MasterVolume = value;
            ApplyVolumes();
            Save();
        }

        /// <summary>通过滑块设置音乐音量。</summary>
        /// <param name="value">线性音量，范围零到一。</param>
        public void SetMusicVolume(float value)
        {
            if (_isSyncing) return;
            _settings.MusicVolume = value;
            ApplyVolumes();
            Save();
        }

        /// <summary>通过滑块设置音效音量。</summary>
        /// <param name="value">线性音量，范围零到一。</param>
        public void SetEffectsVolume(float value)
        {
            if (_isSyncing) return;
            _settings.EffectsVolume = value;
            ApplyVolumes();
            Save();
        }

        /// <summary>通过滑块设置镜头震动强度。</summary>
        /// <param name="value">强度从零（关闭）到一。</param>
        public void SetShakeIntensity(float value)
        {
            if (_isSyncing) return;
            _settings.ScreenShakeIntensity = value;
            ApplyShake();
            Save();
        }

        /// <summary>通过滑块设置镜头转动速度。</summary>
        /// <param name="value">灵敏度倍率。</param>
        public void SetLookSensitivity(float value)
        {
            if (_isSyncing) return;
            _settings.MouseSensitivity = value;
            ApplyLook();
            Save();
        }

        /// <summary>通过滑块设置画质，零为低，一为中，二为高。</summary>
        /// <param name="value">滑块值。</param>
        public void SetGraphicsQuality(float value)
        {
            if (_isSyncing) return;
            _settings.GraphicsQuality = Mathf.Clamp(Mathf.RoundToInt(value), 0, 2);
            ApplyGraphics();
            Save();
        }

        /// <summary>切换反转垂直视角。</summary>
        /// <param name="on">鼠标向上移动时是否向下看。</param>
        public void SetInvertLook(bool on)
        {
            if (_isSyncing) return;
            _settings.InvertLook = on;
            ApplyLook();
            Save();
        }

        /// <summary>切换减少闪烁。</summary>
        /// <param name="on">是否抑制闪光。</param>
        public void SetReducedFlashing(bool on)
        {
            if (_isSyncing) return;
            _settings.ReducedFlashing = on;
            ApplyFlashing();
            Save();
        }

        /// <summary>切换延长适应提示。</summary>
        /// <param name="on">提示是否停留更久。</param>
        public void SetExtendedTell(bool on)
        {
            if (_isSyncing) return;
            _settings.ExtendedTellDuration = on;
            ApplyTell();
            Save();
        }

        // --- 按键重绑定。---

        /// <summary>
        /// 开始监听新的按键，绑定到指定操作行。
        /// </summary>
        /// <remarks>
        /// 期间禁用动作，避免采集按键同时触发原操作。Escape取消，排除鼠标以免移动被误认成新绑定。
        /// 完成后直接把覆盖序列化到设置，像滑块设置一样跨会话保留。
        /// </remarks>
        /// <param name="row">正在重绑定的操作行。</param>
        public void BeginRebind(RebindButton row)
        {
            if (row == null || _gameplayMap == null || _activeRebind != null)
            {
                return;
            }

            InputAction action = _gameplayMap.FindAction(row.ActionName, throwIfNotFound: false);

            if (action == null)
            {
                return;
            }

            row.SetLabel("请按键…");

            bool wasEnabled = action.enabled;
            action.Disable();

            _activeRebind = action.PerformInteractiveRebinding(row.BindingIndex)
                .WithControlsExcluding("<Mouse>")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(op => CompleteRebind(op, action, row, wasEnabled, save: true))
                .OnCancel(op => CompleteRebind(op, action, row, wasEnabled, save: false))
                .Start();
        }

        /// <summary>清除全部重绑定，恢复默认配置。</summary>
        public void ResetControls()
        {
            if (_actions == null)
            {
                return;
            }

            _actions.RemoveAllBindingOverrides();
            _settings.InputRebinds = string.Empty;
            Save();
            RefreshRebindLabels();
        }

        /// <summary>完成重绑定，重新启用动作并保存结果。</summary>
        private void CompleteRebind(
            InputActionRebindingExtensions.RebindingOperation operation,
            InputAction action,
            RebindButton row,
            bool wasEnabled,
            bool save)
        {
            operation.Dispose();
            _activeRebind = null;

            if (wasEnabled)
            {
                action.Enable();
            }

            RefreshLabel(row, action);

            if (save)
            {
                _settings.InputRebinds = _actions.SaveBindingOverridesAsJson();
                Save();
            }
        }

        /// <summary>根据动作当前绑定更新所有重绑定行标签。</summary>
        private void RefreshRebindLabels()
        {
            if (_rebindRows == null || _gameplayMap == null)
            {
                return;
            }

            foreach (RebindButton row in _rebindRows)
            {
                InputAction action = _gameplayMap.FindAction(row.ActionName, throwIfNotFound: false);

                if (action != null)
                {
                    RefreshLabel(row, action);
                }
            }
        }

        /// <summary>根据绑定写入单行标签，去除设备前缀。</summary>
        private static void RefreshLabel(RebindButton row, InputAction action)
        {
            if (row.BindingIndex < 0 || row.BindingIndex >= action.bindings.Count)
            {
                return;
            }

            row.SetLabel(BindingLabel(action.bindings[row.BindingIndex].effectivePath));
        }

        /// <summary>本地化显示的操作名称，不改变输入绑定。</summary>
        private static string BindingLabel(string path)
        {
            string localized = path?.ToLowerInvariant() switch
            {
                "<mouse>/leftbutton" => "鼠标左键",
                "<mouse>/rightbutton" => "鼠标右键",
                "<mouse>/middlebutton" => "鼠标中键",
                "<mouse>/forwardbutton" => "鼠标前进键",
                "<mouse>/backbutton" => "鼠标后退键",
                "<keyboard>/space" => "空格键",
                "<keyboard>/escape" => "Esc 键",
                "<keyboard>/enter" => "回车键",
                "<keyboard>/tab" => "Tab 键",
                "<keyboard>/backspace" => "退格键",
                "<keyboard>/leftshift" => "左 Shift 键",
                "<keyboard>/rightshift" => "右 Shift 键",
                "<keyboard>/leftctrl" => "左 Ctrl 键",
                "<keyboard>/rightctrl" => "右 Ctrl 键",
                "<keyboard>/leftalt" => "左 Alt 键",
                "<keyboard>/rightalt" => "右 Alt 键",
                "<keyboard>/leftmeta" => "左 Win 键",
                "<keyboard>/rightmeta" => "右 Win 键",
                "<keyboard>/capslock" => "大写锁定键",
                "<keyboard>/uparrow" => "↑ 键",
                "<keyboard>/downarrow" => "↓ 键",
                "<keyboard>/leftarrow" => "← 键",
                "<keyboard>/rightarrow" => "→ 键",
                "<keyboard>/insert" => "插入键",
                "<keyboard>/delete" => "删除键",
                "<keyboard>/home" => "Home 键",
                "<keyboard>/end" => "End 键",
                "<keyboard>/pageup" => "向上翻页键",
                "<keyboard>/pagedown" => "向下翻页键",
                "<keyboard>/numlock" => "数字锁定键",
                "<keyboard>/numpad0" => "小键盘 0",
                "<keyboard>/numpad1" => "小键盘 1",
                "<keyboard>/numpad2" => "小键盘 2",
                "<keyboard>/numpad3" => "小键盘 3",
                "<keyboard>/numpad4" => "小键盘 4",
                "<keyboard>/numpad5" => "小键盘 5",
                "<keyboard>/numpad6" => "小键盘 6",
                "<keyboard>/numpad7" => "小键盘 7",
                "<keyboard>/numpad8" => "小键盘 8",
                "<keyboard>/numpad9" => "小键盘 9",
                "<keyboard>/numpadenter" => "小键盘回车",
                "<keyboard>/numpaddivide" => "小键盘 /",
                "<keyboard>/numpadmultiply" => "小键盘 *",
                "<keyboard>/numpadplus" => "小键盘 +",
                "<keyboard>/numpadminus" => "小键盘 -",
                "<keyboard>/numpadperiod" => "小键盘 .",
                "<keyboard>/printscreen" => "截屏键",
                "<keyboard>/scrolllock" => "滚动锁定键",
                "<keyboard>/pause" => "Pause 键",
                "<keyboard>/contextmenu" => "菜单键",
                "<gamepad>/buttonsouth" => "下侧按键",
                "<gamepad>/buttoneast" => "右侧按键",
                "<gamepad>/buttonwest" => "左侧按键",
                "<gamepad>/buttonnorth" => "上侧按键",
                "<gamepad>/leftshoulder" => "左肩键",
                "<gamepad>/rightshoulder" => "右肩键",
                "<gamepad>/lefttrigger" => "左扳机",
                "<gamepad>/righttrigger" => "右扳机",
                "<gamepad>/leftstickpress" => "左摇杆按下",
                "<gamepad>/rightstickpress" => "右摇杆按下",
                "<gamepad>/start" => "开始键",
                "<gamepad>/select" => "选择键",
                "<gamepad>/dpad/up" => "方向键 ↑",
                "<gamepad>/dpad/down" => "方向键 ↓",
                "<gamepad>/dpad/left" => "方向键 ←",
                "<gamepad>/dpad/right" => "方向键 →",
                _ => null
            };

            return localized ?? InputControlPath.ToHumanReadableString(
                path, InputControlPath.HumanReadableStringOptions.OmitDevice);
        }

        /// <summary>指定引用，供界面生成器使用。</summary>
        /// <param name="panel">面板根对象。</param>
        /// <param name="master">主音量滑块。</param>
        /// <param name="music">音乐音量滑块。</param>
        /// <param name="effects">音效音量滑块。</param>
        /// <param name="shake">镜头震动滑块。</param>
        /// <param name="reducedFlashing">减少闪烁开关。</param>
        /// <param name="extendedTell">延长提示开关。</param>
        /// <param name="actions">共享输入动作资源。</param>
        /// <param name="sensitivity">镜头灵敏度滑块。</param>
        /// <param name="invertLook">反转垂直视角开关。</param>
        public void Bind(
            GameObject panel,
            Slider master,
            Slider music,
            Slider effects,
            Slider shake,
            Toggle reducedFlashing,
            Toggle extendedTell,
            InputActionAsset actions,
            Slider sensitivity = null,
            Toggle invertLook = null,
            Slider quality = null,
            Text qualityLabel = null)
        {
            _sensitivitySlider = sensitivity;
            _invertLookToggle = invertLook;
            _qualitySlider = quality;
            _qualityLabel = qualityLabel;
            _panel = panel;
            _masterSlider = master;
            _musicSlider = music;
            _effectsSlider = effects;
            _shakeSlider = shake;
            _reducedFlashingToggle = reducedFlashing;
            _extendedTellToggle = extendedTell;
            _actions = actions;
        }

        private SettingsData Load() =>
            _saveService != null && _saveService.TryLoad(SaveKeys.Settings, out SettingsData loaded)
                ? loaded
                : new SettingsData();

        private void Save() => _saveService?.Save(SaveKeys.Settings, _settings);

        /// <summary>将全部设置应用到对应系统。</summary>
        private void ApplyAll()
        {
            ApplyVolumes();
            ApplyShake();
            ApplyFlashing();
            ApplyTell();
            ApplyLook();
            ApplyGraphics();
        }

        /// <summary>
        /// 应用玩家选择的画质；尚未选择时保留游戏自身的选择。
        /// </summary>
        private void ApplyGraphics()
        {
            if (_settings.GraphicsQuality >= 0)
            {
                GraphicsTier.SetChosen((GraphicsQuality)Mathf.Clamp(_settings.GraphicsQuality, 0, 2));
            }

            if (_qualityLabel != null)
            {
                string quality = GraphicsTier.Current switch
                {
                    GraphicsQuality.Low => "低",
                    GraphicsQuality.Medium => "中",
                    GraphicsQuality.High => "高",
                    _ => GraphicsTier.Current.ToString()
                };

                _qualityLabel.text = quality + (GraphicsTier.IsAutomatic ? "（自动）" : string.Empty);
            }
        }

        private void ApplyLook()
        {
            // 旧存档中尚不存在的设置会读为零；零灵敏度会使视角冻结。
            LookSettings.Sensitivity = _settings.MouseSensitivity > 0f
                ? Mathf.Clamp(_settings.MouseSensitivity, LookSettings.MinimumSensitivity, LookSettings.MaximumSensitivity)
                : LookSettings.DefaultSensitivity;
            LookSettings.InvertY = _settings.InvertLook;
        }

        private void ApplyVolumes()
        {
            if (ServiceRegistry.Current.TryGet(out IAudioService audio))
            {
                audio.SetBusVolume(AudioBus.Master, _settings.MasterVolume);
                audio.SetBusVolume(AudioBus.Music, _settings.MusicVolume);
                audio.SetBusVolume(AudioBus.Effects, _settings.EffectsVolume);
            }
        }

        private void ApplyShake()
        {
            if (ServiceRegistry.Current.TryGet(out IScreenShake shake))
            {
                shake.SetUserIntensity(_settings.ScreenShakeIntensity);
            }
        }

        private void ApplyFlashing() => FeedbackSettings.ReducedFlashing = _settings.ReducedFlashing;

        private void ApplyTell() =>
            FeedbackSettings.TellHoldMultiplier = _settings.ExtendedTellDuration ? ExtendedTellMultiplier : 1f;

        /// <summary>把已保存的重绑定加载到共享输入资源。</summary>
        private void ApplyInputOverrides()
        {
            if (_actions != null && !string.IsNullOrEmpty(_settings.InputRebinds))
            {
                _actions.LoadBindingOverridesFromJson(_settings.InputRebinds);
            }
        }

        /// <summary>将已加载值写入控件，不当作编辑。</summary>
        private void SyncControls()
        {
            _isSyncing = true;

            if (_masterSlider != null) _masterSlider.value = _settings.MasterVolume;
            if (_musicSlider != null) _musicSlider.value = _settings.MusicVolume;
            if (_effectsSlider != null) _effectsSlider.value = _settings.EffectsVolume;
            if (_shakeSlider != null) _shakeSlider.value = _settings.ScreenShakeIntensity;
            if (_reducedFlashingToggle != null) _reducedFlashingToggle.isOn = _settings.ReducedFlashing;
            if (_extendedTellToggle != null) _extendedTellToggle.isOn = _settings.ExtendedTellDuration;
            if (_sensitivitySlider != null) _sensitivitySlider.value = LookSettings.Sensitivity;
            if (_invertLookToggle != null) _invertLookToggle.isOn = _settings.InvertLook;
            if (_qualitySlider != null) _qualitySlider.value = (int)GraphicsTier.Current;

            _isSyncing = false;
        }
    }
}
