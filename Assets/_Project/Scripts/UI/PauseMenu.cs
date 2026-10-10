using System;
using AdaptiveBossArena.Core.Events;
using AdaptiveBossArena.Core.Services;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace AdaptiveBossArena.UI
{
    /// <summary>
    /// 暂停战斗并显示暂停面板。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 通过<see cref="ITimeService"/>暂停，不直接写<c>Time.timeScale</c>，
    /// 避免打击停顿中解除暂停后恢复错误速度，导致永久慢动作。
    /// </para>
    /// <para>
    /// 面板使用未缩放时间，世界冻结时仍能动画和响应。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PauseMenu : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField]
        [Tooltip("Generated input actions asset, used to find the Pause action.")]
        private InputActionAsset _actions;

        [SerializeField]
        [Tooltip("Action map containing the Pause action.")]
        private string _actionMapName = "Gameplay";

        [SerializeField]
        [Tooltip("Name of the pause action.")]
        private string _pauseActionName = "Pause";

        [Header("Presentation")]
        [SerializeField]
        [Tooltip("Root object shown while paused.")]
        private GameObject _panel;

        [SerializeField]
        [Tooltip("Settings menu opened from this pause menu. Optional, but without it the pause key " +
                 "would dismiss the pause panel and leave the settings panel over a running fight.")]
        private SettingsMenu _settingsMenu;

        [SerializeField]
        [Tooltip("The quit button, hidden on platforms where quitting does nothing.")]
        private GameObject _quitButton;

        [Header("Navigation")]
        [SerializeField]
        [Tooltip("Name of the title scene to return to. Must be in the build settings.")]
        private string _mainMenuSceneName = "MainMenu";

        [Header("Broadcast")]
        [SerializeField]
        [Tooltip("Carries whether the game is now paused, for anything that needs to react.")]
        private BoolEventChannel _pauseStateChannel;

        private ITimeService _time;
        private InputAction _pauseAction;
        private bool _isPaused;
        private bool _isSuppressed;

        /// <summary>游戏被该菜单暂停时为true。</summary>
        public bool IsPaused => _isPaused;

        /// <summary>玩家请求放弃本次尝试时触发。</summary>
        public event Action RestartRequested;

        private void Start()
        {
            ServiceRegistry.Current.TryGet(out _time);
            ResolvePauseAction();
            ApplyPanelVisibility();

            if (_quitButton != null && !CanQuit)
            {
                _quitButton.SetActive(false);
            }
        }

        private void Update()
        {
            if (_isSuppressed || _pauseAction == null)
            {
                return;
            }

            if (!_pauseAction.WasPressedThisFrame())
            {
                return;
            }

            // 设置面板在Canvas根节点，是暂停面板的同级对象。否则暂停键可能隐藏暂停、恢复战斗，
            // 却留下不透明设置界面盖住正在行动的Boss。
            if (_settingsMenu != null && _settingsMenu.IsOpen)
            {
                _settingsMenu.Close();
                return;
            }

            SetPaused(!_isPaused);
        }

        /// <summary>暂停或继续战斗。</summary>
        /// <param name="paused">true表示暂停。</param>
        public void SetPaused(bool paused)
        {
            if (_isPaused == paused)
            {
                return;
            }

            _isPaused = paused;
            _time?.SetPaused(paused);

            ApplyPanelVisibility();
            _pauseStateChannel?.Raise(paused);
        }

        /// <summary>
        /// 禁止暂停，用于战斗结束后。
        /// </summary>
        /// <remarks>
        /// 否则玩家能在胜利界面上暂停，解除后让已经死亡的双方继续战斗。
        /// </remarks>
        /// <param name="suppressed">true表示忽略暂停输入。</param>
        public void SetSuppressed(bool suppressed)
        {
            _isSuppressed = suppressed;

            if (suppressed && _isPaused)
            {
                SetPaused(false);
            }
        }

        /// <summary>继续战斗，绑定面板继续按钮。</summary>
        public void Resume() => SetPaused(false);

        /// <summary>
        /// 放弃战斗返回标题，绑定主菜单按钮。
        /// </summary>
        /// <remarks>
        /// 加载前解除暂停，避免新标题继承零时间缩放而像卡死。标题中有挑战选项，
        /// 返回路径让开战后仍能修改；浏览器不能退出应用，因此这是离开竞技场的唯一途径。
        /// </remarks>
        public void ReturnToMenu()
        {
            SetPaused(false);
            SceneManager.LoadScene(_mainMenuSceneName);
        }

        /// <summary>放弃本次尝试并请求重开，绑定重新开始按钮。</summary>
        public void Restart()
        {
            SetPaused(false);
            RestartRequested?.Invoke();
        }

        /// <summary>
        /// 退出应用，或在编辑器中结束运行模式。
        /// </summary>
        /// <remarks>
        /// 网页无法自行关闭浏览器标签，WebGL上的<see cref="Application.Quit"/>无效，
        /// 因此在运行时隐藏退出按钮。场景生成时不知道目标平台，不能在生成阶段隐藏；
        /// 浏览器中的有效退出路径是返回标题。
        /// </remarks>
        public void Quit()
        {
            SetPaused(false);

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#elif !UNITY_WEBGL
            Application.Quit();
#endif
        }

        /// <summary>
        /// 当前平台退出是否有意义，避免显示无效按钮。
        /// </summary>
        public static bool CanQuit
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            get => false;
#else
            get => true;
#endif
        }

        /// <summary>指定引用，供界面生成器使用。</summary>
        /// <param name="actions">输入动作资源。</param>
        /// <param name="panel">暂停时显示的根对象。</param>
        /// <param name="pauseStateChannel">传递暂停状态的通道。</param>
        /// <param name="settingsMenu">暂停菜单打开的设置菜单，用于随后关闭。</param>
        /// <param name="quitButton">退出按钮，退出无效的平台隐藏。</param>
        /// <param name="mainMenuSceneName">返回的标题场景。</param>
        public void Bind(
            InputActionAsset actions,
            GameObject panel,
            BoolEventChannel pauseStateChannel,
            SettingsMenu settingsMenu = null,
            GameObject quitButton = null,
            string mainMenuSceneName = "MainMenu")
        {
            _actions = actions;
            _panel = panel;
            _pauseStateChannel = pauseStateChannel;
            _settingsMenu = settingsMenu;
            _quitButton = quitButton;
            _mainMenuSceneName = mainMenuSceneName;
        }

        private void ResolvePauseAction()
        {
            if (_actions == null)
            {
                Debug.LogWarning(
                    "[Adaptive Boss Arena] PauseMenu has no input actions asset; pausing is unavailable.",
                    this);
                return;
            }

            InputActionMap map = _actions.FindActionMap(_actionMapName, throwIfNotFound: false);
            _pauseAction = map?.FindAction(_pauseActionName, throwIfNotFound: false);

            // 显式启用。玩家输入读取器启用玩法映射，但玩家对象禁用或销毁时菜单仍必须可用。
            _pauseAction?.Enable();
        }

        private void ApplyPanelVisibility()
        {
            if (_panel != null)
            {
                _panel.SetActive(_isPaused);
            }

            // 覆盖继续按钮、重开和战斗结束禁用暂停等恢复路径；
            // 从暂停打开的设置不能在暂停结束后继续存在。
            if (!_isPaused && _settingsMenu != null && _settingsMenu.IsOpen)
            {
                _settingsMenu.Close();
            }
        }
    }
}
