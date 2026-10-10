using System;
using AdaptiveBossArena.Core.Constants;
using UnityEngine;

namespace AdaptiveBossArena.Player.Controls
{
    /// <summary>
    /// 记住近期动作按键，在角色恢复行动时重放。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是项目中改善响应手感最有价值的单一组件，工作正常时却不会被察觉。没有它，
    /// 闪避后摇结束前一帧按下的攻击会被丢弃，玩家判断游戏漏输入完全合理。
    /// 约 150 毫秒缓存使角色像在等待指令，而非忽略指令。
    /// </para>
    /// <para>
    /// 按每个动作保存一次按键，而非队列。队列会在长动画之后依次消耗连续乱按的多次输入，像角色自行行动。
    /// 每动作仅保留最近一次按键，宽容时机误差却不接管操作。
    /// </para>
    /// <para>
    /// 明确消费输入：使用缓存输入的状态会清除它，因此两个状态不能使用同一次按键。
    /// </para>
    /// </remarks>
    public sealed class InputBuffer
    {
        /// <summary>表示动作从未按下的哨兵值。</summary>
        private const float NeverPressed = float.NegativeInfinity;

        private readonly float _windowSeconds;
        private readonly float[] _pressTimes;

        /// <summary>创建缓存。</summary>
        /// <param name="windowSeconds">
        /// 按键保持有效的时长，默认采用 <see cref="GameplayConstants.InputBufferSeconds"/>。
        /// </param>
        public InputBuffer(float windowSeconds = GameplayConstants.InputBufferSeconds)
        {
            _windowSeconds = Mathf.Max(0f, windowSeconds);

            int actionCount = Enum.GetValues(typeof(PlayerInputAction)).Length;
            _pressTimes = new float[actionCount];

            Clear();
        }

        /// <summary>按键有效时长，单位秒。</summary>
        public float WindowSeconds => _windowSeconds;

        /// <summary>记录动作被按下。</summary>
        /// <param name="action">按下的动作。</param>
        /// <param name="timeNow">当前战斗时钟时间。</param>
        public void Record(PlayerInputAction action, float timeNow) => _pressTimes[(int)action] = timeNow;

        /// <summary>轮询输入源的全部动作，记录本帧触发的动作。</summary>
        /// <param name="input">要轮询的输入源。</param>
        /// <param name="timeNow">当前战斗时钟时间。</param>
        public void RecordPressesFrom(IPlayerInput input, float timeNow)
        {
            for (int i = 0; i < _pressTimes.Length; i++)
            {
                var action = (PlayerInputAction)i;

                if (input.WasPressedThisFrame(action))
                {
                    _pressTimes[i] = timeNow;
                }
            }
        }

        /// <summary>检查动作是否已缓存，但不消费它。</summary>
        /// <param name="action">要检查的动作。</param>
        /// <param name="timeNow">当前战斗时钟时间。</param>
        /// <returns>按键仍位于窗口内时为 true。</returns>
        public bool HasBuffered(PlayerInputAction action, float timeNow) =>
            timeNow - _pressTimes[(int)action] <= _windowSeconds;

        /// <summary>
        /// 存在缓存动作时消费它。
        /// </summary>
        /// <param name="action">要消费的动作。</param>
        /// <param name="timeNow">当前战斗时钟时间。</param>
        /// <returns>此前存在按键且现已消费时为 true。</returns>
        public bool TryConsume(PlayerInputAction action, float timeNow)
        {
            if (!HasBuffered(action, timeNow))
            {
                return false;
            }

            _pressTimes[(int)action] = NeverPressed;
            return true;
        }

        /// <summary>丢弃缓存按键，不执行动作。</summary>
        /// <param name="action">要丢弃的动作。</param>
        public void Discard(PlayerInputAction action) => _pressTimes[(int)action] = NeverPressed;

        /// <summary>
        /// 丢弃全部缓存按键。
        /// </summary>
        /// <remarks>
        /// 死亡和解除暂停时调用。若重放暂停前按下的输入，恢复游戏瞬间角色会突然行动。
        /// </remarks>
        public void Clear()
        {
            for (int i = 0; i < _pressTimes.Length; i++)
            {
                _pressTimes[i] = NeverPressed;
            }
        }
    }
}
