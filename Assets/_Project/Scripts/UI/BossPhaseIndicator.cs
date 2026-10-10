using AdaptiveBossArena.Core.Constants;
using AdaptiveBossArena.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace AdaptiveBossArena.UI
{
    /// <summary>
    /// 显示Boss已经推进到哪个阶段。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 使用标记点，让玩家看见已完成和剩余阶段数量；生命条无法传达阶段变化带来的战斗差异。
    /// </para>
    /// <para>
    /// 进入新阶段是遭遇战最明确的节点，应给出视觉确认。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BossPhaseIndicator : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField]
        [Tooltip("Channel carrying the zero-based phase index.")]
        private IntEventChannel _phaseChannel;

        [Header("Pips")]
        [SerializeField]
        [Tooltip("One image per phase, in order.")]
        private Image[] _pips = new Image[0];

        [SerializeField]
        [Tooltip("Colour of a phase the boss has not yet reached.")]
        private Color _pendingColor = new Color(0.78f, 0.72f, 0.62f, 0.22f);

        [SerializeField]
        [Tooltip("Colour of the phase currently in effect.")]
        private Color _activeColor = new Color(0.72f, 0.1f, 0.07f, 1f);

        [SerializeField]
        [Tooltip("Colour of a phase already driven through.")]
        private Color _clearedColor = new Color(0.78f, 0.72f, 0.62f, 0.7f);

        private void OnEnable()
        {
            if (_phaseChannel != null)
            {
                _phaseChannel.Raised += OnPhaseChanged;
            }

            OnPhaseChanged(0);
        }

        private void OnDisable()
        {
            if (_phaseChannel != null)
            {
                _phaseChannel.Raised -= OnPhaseChanged;
            }
        }

        /// <summary>指定通道和阶段标记图片，供界面生成器使用。</summary>
        /// <param name="channel">传递阶段索引的通道。</param>
        /// <param name="pips">按阶段顺序排列的标记图片。</param>
        public void Bind(IntEventChannel channel, Image[] pips)
        {
            _phaseChannel = channel;
            _pips = pips ?? new Image[0];
        }

        private void OnPhaseChanged(int phaseIndex)
        {
            for (int i = 0; i < _pips.Length; i++)
            {
                if (_pips[i] == null)
                {
                    continue;
                }

                _pips[i].color = ResolveColor(i, phaseIndex);
            }
        }

        private Color ResolveColor(int pipIndex, int phaseIndex)
        {
            if (pipIndex < phaseIndex)
            {
                return _clearedColor;
            }

            return pipIndex == phaseIndex ? _activeColor : _pendingColor;
        }

        /// <summary>预期标记数量，与配置的阶段数一致。</summary>
        public static int ExpectedPipCount => GameplayConstants.BossPhaseCount;
    }
}
