using System;
using UnityEngine;
using UnityEngine.UI;

namespace AdaptiveBossArena.UI
{
    /// <summary>
    /// 胜负确定后显示的结果面板。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 两种结果只在文字和颜色上不同，因此共用组件，避免重复淡入淡出、布局和重试绑定。
    /// </para>
    /// <para>
    /// 摘要优先显示Boss形成了多少种策略，这比耗时更能说明玩家行为有多容易被预测，
    /// 符合遭遇战的核心目标。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class EndScreen : MonoBehaviour
    {
        [Header("Presentation")]
        [SerializeField]
        [Tooltip("Root object containing the panel.")]
        private GameObject _panel;

        [SerializeField]
        [Tooltip("Group faded in when the fight ends.")]
        private CanvasGroup _group;

        [SerializeField]
        [Tooltip("Headline text, such as victory or defeat.")]
        private Text _headline;

        [SerializeField]
        [Tooltip("Summary of how the attempt went.")]
        private Text _summary;

        [SerializeField]
        [Tooltip("The boss's dossier: what it read in the player and how it answered.")]
        private Text _dossier;

        [Header("Timing")]
        [SerializeField]
        [Range(0.2f, 3f)]
        [Tooltip("Pause before the screen appears, so the final blow can land and be seen.")]
        private float _delaySeconds = 1.1f;

        [SerializeField]
        [Range(0.1f, 2f)]
        [Tooltip("Fade-in duration.")]
        private float _fadeSeconds = 0.6f;

        // 胜利用暗金、失败用旧血色，文字像铭文般排布，避免浅蓝粉色大写的街机结束画面感。
        private static readonly Color VictoryColor = new Color(0.84f, 0.72f, 0.46f);
        private static readonly Color DefeatColor = new Color(0.66f, 0.09f, 0.06f);

        private bool _isRunning;
        private float _elapsed;

        /// <summary>玩家请求再试一次时触发。</summary>
        public event Action RestartRequested;

        /// <summary>胜负已确定且此界面接管时为true。</summary>
        public bool IsShown => _isRunning;

        private void Awake() => Hide();

        private void Update()
        {
            if (!_isRunning)
            {
                return;
            }

            _elapsed += Time.unscaledDeltaTime;

            if (_elapsed < _delaySeconds)
            {
                return;
            }

            if (_group != null)
            {
                _group.alpha = Mathf.Clamp01((_elapsed - _delaySeconds) / _fadeSeconds);
            }
        }

        /// <summary>
        /// 显示结果。
        /// </summary>
        /// <param name="won">Boss是否被击败。</param>
        /// <param name="durationSeconds">本次尝试时长。</param>
        /// <param name="adaptationsAllowed">Boss形成的反制策略数量。</param>
        /// <param name="isNewRecord">本次是否刷新个人最佳。</param>
        /// <param name="dossier">Boss对玩家的分析，没有则为空。</param>
        public void Show(bool won, float durationSeconds, int adaptationsAllowed, bool isNewRecord,
            string dossier = "")
        {
            _isRunning = true;
            _elapsed = 0f;

            if (_panel != null)
            {
                _panel.SetActive(true);
            }

            if (_group != null)
            {
                _group.alpha = 0f;
            }

            if (_headline != null)
            {
                _headline.text = won ? "巨兽已倒下" : "你倒下了";
                _headline.color = won ? VictoryColor : DefeatColor;
            }

            if (_summary != null)
            {
                _summary.text = BuildSummary(won, durationSeconds, adaptationsAllowed, isNewRecord);
            }

            if (_dossier != null)
            {
                _dossier.text = dossier ?? string.Empty;
            }
        }

        /// <summary>隐藏面板并恢复中立状态。</summary>
        public void Hide()
        {
            _isRunning = false;
            _elapsed = 0f;

            if (_group != null)
            {
                _group.alpha = 0f;
            }

            if (_panel != null)
            {
                _panel.SetActive(false);
            }
        }

        /// <summary>请求再次尝试，绑定面板重试按钮。</summary>
        public void Restart()
        {
            Hide();
            RestartRequested?.Invoke();
        }

        /// <summary>指定引用，供界面生成器使用。</summary>
        /// <param name="panel">包含面板的根对象。</param>
        /// <param name="group">用于淡入淡出的CanvasGroup。</param>
        /// <param name="headline">标题文字。</param>
        /// <param name="summary">摘要文字。</param>
        /// <param name="dossier">Boss对玩家的分析文字。</param>
        public void Bind(GameObject panel, CanvasGroup group, Text headline, Text summary, Text dossier)
        {
            _panel = panel;
            _group = group;
            _headline = headline;
            _summary = summary;
            _dossier = dossier;
        }

        /// <summary>组合结果摘要。</summary>
        private static string BuildSummary(
            bool won,
            float durationSeconds,
            int adaptationsAllowed,
            bool isNewRecord)
        {
            var minutes = Mathf.FloorToInt(durationSeconds / 60f);
            var seconds = Mathf.FloorToInt(durationSeconds % 60f);

            string readAsText = adaptationsAllowed == 0
                ? "它没能看穿你的习惯。"
                : adaptationsAllowed == 1
                    ? "它看穿了你的一个习惯。"
                    : $"它看穿了你的 {adaptationsAllowed} 个习惯。";

            string record = isNewRecord ? "\n\n刷新个人最佳纪录。" : string.Empty;

            string closing = won
                ? "\n\n下次试着让它学到更少，再赢一次。"
                : "\n\n改变你的打法。它只会针对你重复的行为。";

            return $"{readAsText}\n\n用时：{minutes:00}:{seconds:00}{record}{closing}";
        }
    }
}
