using System;
using UnityEngine;
using UnityEngine.UI;

namespace AdaptiveBossArena.UI
{
    /// <summary>
    /// 战斗开场的短暂准备和开战提示。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 控制刚恢复Boss就攻击，会让玩家觉得游戏未经准备已经开始。先短暂停留让玩家就绪，再给出明确提示；
    /// Director控制冻结时间，本组件只决定显示内容和时长。
    /// </para>
    /// <para>
    /// 背景世界暂停，所以这里使用未缩放时间；通过回调报告结束而非协程，让Director统一决定战斗开始时机。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RoundIntro : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Root object containing the banner.")]
        private GameObject _panel;

        [SerializeField]
        [Tooltip("Group faded out as the fight is released.")]
        private CanvasGroup _group;

        [SerializeField]
        [Tooltip("The single large banner line.")]
        private Text _label;

        [Header("Timing")]
        [SerializeField]
        [Range(0.2f, 2f)]
        [Tooltip("How long the 'ready' cue holds before the release.")]
        private float _readySeconds = 0.9f;

        [SerializeField]
        [Range(0.2f, 1.5f)]
        [Tooltip("How long the 'fight' cue stays up as it fades.")]
        private float _fightSeconds = 0.6f;

        // 与界面其他文字一致的骨白和旧血色。
        private static readonly Color ReadyColor = new Color(0.84f, 0.77f, 0.66f);
        private static readonly Color FightColor = new Color(0.72f, 0.12f, 0.07f);

        /// <summary>首次尝试时Boss名称显示多久后释放战斗。</summary>
        private const float TitleSeconds = 2.6f;

        /// <summary>Boss名称字号，小于开战提示，让宽字距文字能够放入屏幕。</summary>
        private const int TitleFontSize = 60;

        /// <summary>本轮首次入场的Boss名称，普通准备和开战流程为null。</summary>
        private string _title;

        /// <summary>提示条自身字号，在名称展示后恢复。</summary>
        private int _cueFontSize;

        /// <summary>标题放在下三分之一处，避开镜头中的角色脸部。</summary>
        private static readonly Vector2 TitlePosition = new Vector2(0f, -300f);

        /// <summary>提示条自身位置，在名称展示后恢复。</summary>
        private Vector2 _cuePosition;

        private Action _onComplete;
        private bool _isRunning;
        private float _elapsed;
        private bool _releasedShown;

        /// <summary>开场仍在播放时为true。</summary>
        public bool IsRunning => _isRunning;

        private void Awake() => Hide();

        /// <summary>开始开场，完成后回调。</summary>
        /// <param name="onComplete">战斗应释放时调用，保证只调用一次。</param>
        public void Begin(Action onComplete) => BeginEntrance(null, onComplete);

        /// <summary>
        /// 从Boss名称展示开始开场，完成后回调。
        /// </summary>
        /// <remarks>
        /// 首次尝试在镜头看向Boss时显示其名称；重试直接使用简短准备和开战提示。
        /// </remarks>
        /// <param name="title">要显示的名称，普通准备提示为null。</param>
        /// <param name="onComplete">战斗应释放时调用，保证只调用一次。</param>
        public void BeginEntrance(string title, Action onComplete)
        {
            _title = title;
            _onComplete = onComplete;
            _isRunning = true;
            _elapsed = 0f;
            _releasedShown = false;

            if (_panel != null)
            {
                _panel.SetActive(true);
            }

            ShowReady();
        }

        private void Update()
        {
            if (!_isRunning)
            {
                return;
            }

            _elapsed += Time.unscaledDeltaTime;

            float hold = HoldSeconds;

            if (!_releasedShown && _elapsed >= hold)
            {
                ShowFight();
            }

            if (_releasedShown && _group != null)
            {
                // 在开战窗口中渐隐提示条，让行动前最后看到的是淡出提示，而非突然消失。
                float fightElapsed = _elapsed - hold;
                _group.alpha = Mathf.Clamp01(1f - fightElapsed / _fightSeconds);
            }

            if (_elapsed >= hold + _fightSeconds)
            {
                Complete();
            }
        }

        /// <summary>开场提示停留时长，首次入场显示Boss名称，其余显示准备提示。</summary>
        public float HoldSeconds => _title != null ? TitleSeconds : _readySeconds;

        /// <summary>为已经看过的玩家跳过开场并立即开始战斗。</summary>
        public void Skip()
        {
            if (_isRunning)
            {
                Complete();
            }
        }

        /// <summary>隐藏提示条并清除当前流程。</summary>
        public void Hide()
        {
            _isRunning = false;

            if (_group != null)
            {
                _group.alpha = 0f;
            }

            if (_panel != null)
            {
                _panel.SetActive(false);
            }
        }

        private void ShowReady()
        {
            if (_group != null)
            {
                _group.alpha = 1f;
            }

            if (_label != null)
            {
                if (_cueFontSize == 0)
                {
                    _cueFontSize = _label.fontSize;
                    _cuePosition = _label.rectTransform.anchoredPosition;
                }

                _label.rectTransform.anchoredPosition = _title != null ? TitlePosition : _cuePosition;

                _label.text = _title ?? "准备好了吗？";
                _label.color = ReadyColor;
                _label.fontSize = _title != null ? TitleFontSize : _cueFontSize;
            }
        }

        private void ShowFight()
        {
            _releasedShown = true;

            if (_label != null)
            {
                _label.text = "开战！";
                _label.color = FightColor;

                if (_cueFontSize != 0)
                {
                    _label.fontSize = _cueFontSize;
                    _label.rectTransform.anchoredPosition = _cuePosition;
                }
            }
        }

        private void Complete()
        {
            Action callback = _onComplete;
            _onComplete = null;

            Hide();

            // 组件完全回到静止状态后最后调用，让Director可以开始尝试并检查本组件，避免重入。
            callback?.Invoke();
        }

        /// <summary>指定引用，供界面生成器使用。</summary>
        /// <param name="panel">包含提示条的根对象。</param>
        /// <param name="group">用于淡入淡出的CanvasGroup。</param>
        /// <param name="label">提示条文字。</param>
        public void Bind(GameObject panel, CanvasGroup group, Text label)
        {
            _panel = panel;
            _group = group;
            _label = label;
        }
    }
}
