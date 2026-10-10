using AdaptiveBossArena.Core.Events;
using AdaptiveBossArena.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace AdaptiveBossArena.UI
{
    /// <summary>
    /// 显示归一化资源值、带延迟追赶填充的资源条。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 追赶条是核心反馈：立即跳变只说明生命变化，追赶填充显示变化<em>幅度</em>，
    /// 战斗中玩家无法靠读数字迅速得知。
    /// </para>
    /// <para>
    /// 监听事件通道而不轮询角色，因此HUD不持有玩家或Boss引用，可用于双方。
    /// </para>
    /// <para>
    /// 使用未缩放时间，让打击停顿期间追赶动画仍能继续。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ResourceBar : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField]
        [Tooltip("Channel carrying the normalised value, from zero to one.")]
        private FloatEventChannel _channel;

        [Header("Fills")]
        [SerializeField]
        [Tooltip("Image that tracks the value immediately.")]
        private Image _fill;

        [SerializeField]
        [Tooltip("Image that eases toward the value behind the main fill, showing recent loss. Optional.")]
        private Image _trailingFill;

        [Header("Feel")]
        [SerializeField]
        [Range(0.05f, 1.5f)]
        [Tooltip("Half-life of the trailing fill's chase. Longer makes a big hit read as more dramatic.")]
        private float _trailHalfLife = 0.35f;

        [SerializeField]
        [Range(0f, 1.5f)]
        [Tooltip("Delay before the trailing fill starts chasing, so the gap is legible first.")]
        private float _trailDelaySeconds = 0.4f;

        private float _current = 1f;
        private float _trailing = 1f;

        /// <summary>
        /// 当前资源条显示的比例。
        /// </summary>
        /// <remarks>
        /// 公开给自动化测试，断言资源池变化实际到达界面；未接线和没有变化的资源条外观看起来相同，
        /// 测试能区分。
        /// </remarks>
        public float DisplayedFraction => _current;

        /// <summary>资源条是否有监听通道，用于诊断。</summary>
        public bool HasChannel => _channel != null;
        private float _trailHoldRemaining;

        private void OnEnable()
        {
            if (_channel != null)
            {
                _channel.Raised += OnValueChanged;
            }
        }

        private void OnDisable()
        {
            if (_channel != null)
            {
                _channel.Raised -= OnValueChanged;
            }
        }

        private void Update()
        {
            if (_trailingFill == null)
            {
                return;
            }

            float deltaTime = Time.unscaledDeltaTime;

            if (_trailHoldRemaining > 0f)
            {
                _trailHoldRemaining -= deltaTime;
                return;
            }

            _trailing = MathUtil.Damp(_trailing, _current, _trailHalfLife, deltaTime);
            SetWidthFraction(_trailingFill, Mathf.Max(_trailing, _current));
        }

        /// <summary>设置显示值并立即同步两个填充，用于重置战斗。</summary>
        /// <param name="normalized">零至一的数值。</param>
        public void SetImmediate(float normalized)
        {
            _current = Mathf.Clamp01(normalized);
            _trailing = _current;
            _trailHoldRemaining = 0f;

            ApplyFills();
        }

        /// <summary>指定通道和图片，供界面生成器使用。</summary>
        /// <param name="channel">传递归一化数值的通道。</param>
        /// <param name="fill">即时填充图片。</param>
        /// <param name="trailingFill">追赶填充图片，可为空。</param>
        public void Bind(FloatEventChannel channel, Image fill, Image trailingFill)
        {
            _channel = channel;
            _fill = fill;
            _trailingFill = trailingFill;
        }

        private void OnValueChanged(float normalized)
        {
            float previous = _current;
            _current = Mathf.Clamp01(normalized);

            // 停留只作用于资源损失。获得资源应立即显示，方便玩家判断能否再执行动作。
            if (_current < previous)
            {
                _trailHoldRemaining = _trailDelaySeconds;
            }
            else
            {
                _trailing = _current;
            }

            ApplyFills();
        }

        /// <summary>
        /// 拉伸矩形调整两个填充，不使用<see cref="Image.fillAmount"/>。
        /// </summary>
        /// <remarks>
        /// Image没有Sprite时<c>fillAmount</c>会被忽略，这些条是纯色四边形，
        /// 可能出现底层值正常更新却始终满条的症状。调整锚点无需依赖Sprite。
        /// </remarks>
        private void ApplyFills()
        {
            SetWidthFraction(_fill, _current);
            SetWidthFraction(_trailingFill, Mathf.Max(_trailing, _current));
        }

        /// <summary>按父对象宽度的比例拉伸图片矩形。</summary>
        private static void SetWidthFraction(Image image, float fraction)
        {
            if (image == null)
            {
                return;
            }

            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
