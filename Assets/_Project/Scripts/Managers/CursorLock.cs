using AdaptiveBossArena.UI;
using UnityEngine;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 战斗期间捕获鼠标，菜单期间释放鼠标。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 相机现在由鼠标控制环绕，因此必须捕获指针：自由指针会跑出窗口边缘而停止转向，
    /// 或在浏览器中点击游戏背后的页面。菜单需要恢复指针，因此捕获状态跟随屏幕内容，而非只设置一次。
    /// </para>
    /// <para>
    /// 浏览器只在点击后允许捕获指针，所以网页端进入战斗后的第一次点击才会捕获；在此之前逐帧重试，
    /// 是 Unity WebGL 播放器要求的方式。无论游戏设置如何，浏览器中的 Escape 都会释放指针，
    /// 同时也会打开暂停菜单，因此两者保持一致。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CursorLock : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The pause menu; the pointer is freed while it is open.")]
        private PauseMenu _pauseMenu;

        [SerializeField]
        [Tooltip("The outcome screen; the pointer is freed while it shows.")]
        private EndScreen _endScreen;

        [SerializeField]
        [Tooltip("The settings panel; the pointer is freed while it is open.")]
        private SettingsMenu _settingsMenu;

        /// <summary>指针当前是否被游戏捕获。</summary>
        public static bool IsCaptured => Cursor.lockState == CursorLockMode.Locked;

        /// <summary>依据屏幕内容判断战斗是否应该捕获指针。</summary>
        /// <param name="paused">暂停菜单是否打开。</param>
        /// <param name="outcomeShown">胜利或失败界面是否显示。</param>
        /// <param name="settingsOpen">设置面板是否打开。</param>
        /// <returns>屏幕上没有内容需要指针时为 true。</returns>
        public static bool ShouldCapture(bool paused, bool outcomeShown, bool settingsOpen) =>
            !paused && !outcomeShown && !settingsOpen;

        /// <summary>分配需要指针的菜单引用，供场景生成器使用。</summary>
        /// <param name="pauseMenu">暂停菜单。</param>
        /// <param name="endScreen">结果界面。</param>
        /// <param name="settingsMenu">设置面板。</param>
        public void Bind(PauseMenu pauseMenu, EndScreen endScreen, SettingsMenu settingsMenu)
        {
            _pauseMenu = pauseMenu;
            _endScreen = endScreen;
            _settingsMenu = settingsMenu;
        }

        private void Update()
        {
            bool capture = ShouldCapture(
                _pauseMenu != null && _pauseMenu.IsPaused,
                _endScreen != null && _endScreen.IsShown,
                _settingsMenu != null && _settingsMenu.IsOpen);

            if (capture && !IsCaptured)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else if (!capture && Cursor.lockState != CursorLockMode.None)
            {
                Release();
            }
        }

        // 离开竞技场返回标题或退出时，绝不能让指针仍被捕获。
        private void OnDisable() => Release();

        private static void Release()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
