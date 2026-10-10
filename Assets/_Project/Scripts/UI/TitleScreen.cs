using System;
using AdaptiveBossArena.Utilities.Statistics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AdaptiveBossArena.UI
{
    /// <summary>
    /// 标题界面，游戏的入口。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 独立于竞技场场景。战斗场景加载就创建服务、双方角色和学习循环，菜单无需承担这些成本；
    /// 分离后每场战斗都从干净的新状态开始。
    /// </para>
    /// <para>
    /// 开始游戏通过加载场景，战斗内重试则<em>不</em>加载场景：先完整构建一次，再原地重置，
    /// 让种子在重复尝试间保留。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TitleScreen : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Name of the arena scene to load. Must be in the build settings.")]
        private string _arenaSceneName = "Arena";

        [SerializeField]
        [Tooltip("The quit button, hidden on platforms where quitting does nothing.")]
        private GameObject _quitButton;

        /// <summary>加载战斗，绑定开始按钮。</summary>
        public void StartGame() => SceneManager.LoadScene(_arenaSceneName);

        /// <summary>
        /// 退出应用或编辑器运行模式，绑定退出按钮。
        /// </summary>
        /// <remarks>
        /// 浏览器无法自行关闭标签页，因此隐藏无效按钮，见<see cref="Start"/>。
        /// </remarks>
        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#elif !UNITY_WEBGL
            Application.Quit();
#endif
        }

        private void Start()
        {
            // 性能测量启动直接进入战斗，因为探针位于竞技场；停留标题会只测菜单且不输出结果，
            // 普通启动不受影响。
            if (PerfCaptureRequest.Read(Environment.GetCommandLineArgs(), Application.absoluteURL).IsRequested)
            {
                StartGame();
                return;
            }

            if (_quitButton != null && !PauseMenu.CanQuit)
            {
                _quitButton.SetActive(false);
            }
        }

        /// <summary>指定要加载的场景，供场景生成器使用。</summary>
        /// <param name="arenaSceneName">竞技场场景名称。</param>
        /// <param name="quitButton">退出按钮，退出无效时隐藏。</param>
        public void Bind(string arenaSceneName, GameObject quitButton = null)
        {
            _arenaSceneName = arenaSceneName;
            _quitButton = quitButton;
        }
    }
}
