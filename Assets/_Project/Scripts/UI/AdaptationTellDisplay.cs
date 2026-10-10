using System.Collections.Generic;
using AdaptiveBossArena.Core.Events;
using AdaptiveBossArena.Core.Services;
using AdaptiveBossArena.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace AdaptiveBossArena.UI
{
    /// <summary>
    /// 在屏幕上告知Boss改变了战斗方式。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是界面中最关键的元素，也是每个<see cref="AdaptiveBossArena.Learning.CounterStrategy"/>都必
    /// 须有提示的原因。
    /// </para>
    /// <para>
    /// Boss适应刻意缓慢而细微，例如投入攻击前稍长停顿、更近的偏好距离。没有说明原因时，
    /// 玩家容易认为Boss读取控制器；告知变化才能把作弊怀疑转化为被对手研究并压制的体验。
    /// </para>
    /// <para>
    /// 表现保持克制，在屏幕上方短暂显示一行文字后消失，避免模态弹窗中断战斗。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AdaptationTellDisplay : MonoBehaviour
    {
        /// <summary>排队提示显示前的最长等待时间，避免过时。</summary>
        private const float MaxQueueAgeSeconds = 6f;

        [Header("Source")]
        [SerializeField]
        [Tooltip("Channel carrying the tell message for a newly adopted counter-strategy.")]
        private StringEventChannel _adaptationChannel;

        [Header("Presentation")]
        [SerializeField]
        [Tooltip("Text element the message is written into.")]
        private Text _label;

        [SerializeField]
        [Tooltip("Group faded in and out to show and hide the message.")]
        private CanvasGroup _group;

        [Header("Timing")]
        [SerializeField]
        [Range(1f, 8f)]
        [Tooltip("How long a message stays fully visible.")]
        private float _holdSeconds = 3f;

        [SerializeField]
        [Range(0.1f, 1.5f)]
        [Tooltip("Fade duration at each end.")]
        private float _fadeSeconds = 0.4f;

        private readonly Queue<QueuedTell> _pending = new Queue<QueuedTell>();

        private float _visibleFor;
        private bool _isShowing;

        /// <summary>等待中的提示及其发出时间。</summary>
        private readonly struct QueuedTell
        {
            public QueuedTell(string message, float raisedAt)
            {
                Message = message;
                RaisedAt = raisedAt;
            }

            public string Message { get; }

            public float RaisedAt { get; }
        }

        /// <summary>从全局辅助功能设置读取的停留时长倍率。</summary>
        private static float HoldMultiplier => Mathf.Max(1f, FeedbackSettings.TellHoldMultiplier);

        private void OnEnable()
        {
            if (_adaptationChannel != null)
            {
                _adaptationChannel.Raised += OnAdaptationAdopted;
            }

            if (_group != null)
            {
                _group.alpha = 0f;
            }
        }

        private void OnDisable()
        {
            if (_adaptationChannel != null)
            {
                _adaptationChannel.Raised -= OnAdaptationAdopted;
            }
        }

        private void Update()
        {
            // 使用未缩放时间，让打击停顿和完美闪避慢动作中的提示仍以正常速度阅读。
            float deltaTime = Time.unscaledDeltaTime;

            if (_isShowing)
            {
                AdvanceVisible(deltaTime);
                return;
            }

            TryShowNext();
        }

        /// <summary>指定事件通道和表现组件，供界面生成器使用。</summary>
        /// <param name="channel">传递提示消息的通道。</param>
        /// <param name="label">要写入的文字组件。</param>
        /// <param name="group">用于淡入淡出的CanvasGroup。</param>
        public void Bind(StringEventChannel channel, Text label, CanvasGroup group)
        {
            _adaptationChannel = channel;
            _label = label;
            _group = group;
        }

        private void OnAdaptationAdopted(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            // 先排队再显示，避免短时间内两次适应让后一个提示覆盖尚未读完的前一个。
            _pending.Enqueue(new QueuedTell(message, Time.unscaledTime));
        }

        private void TryShowNext()
        {
            while (_pending.Count > 0)
            {
                QueuedTell next = _pending.Dequeue();

                // 等待太久的提示描述玩家已经历过的变化，再显示会造成混淆。
                if (Time.unscaledTime - next.RaisedAt > MaxQueueAgeSeconds)
                {
                    continue;
                }

                if (_label != null)
                {
                    _label.text = next.Message;
                }

                _isShowing = true;
                _visibleFor = 0f;
                return;
            }
        }

        private void AdvanceVisible(float deltaTime)
        {
            _visibleFor += deltaTime;

            float totalDuration = _fadeSeconds * 2f + _holdSeconds * HoldMultiplier;

            if (_visibleFor >= totalDuration)
            {
                _isShowing = false;

                if (_group != null)
                {
                    _group.alpha = 0f;
                }

                return;
            }

            if (_group != null)
            {
                _group.alpha = ResolveAlpha(totalDuration);
            }
        }

        /// <summary>淡入、停留，再淡出。</summary>
        private float ResolveAlpha(float totalDuration)
        {
            if (_visibleFor < _fadeSeconds)
            {
                return MathUtil.Remap01(_visibleFor, 0f, _fadeSeconds);
            }

            float fadeOutStart = totalDuration - _fadeSeconds;

            return _visibleFor > fadeOutStart
                ? 1f - MathUtil.Remap01(_visibleFor, fadeOutStart, totalDuration)
                : 1f;
        }
    }
}
