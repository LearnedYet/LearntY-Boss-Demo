using System;
using AdaptiveBossArena.Utilities.Collections;
using UnityEngine;

namespace AdaptiveBossArena.Core.Perception
{
    /// <summary>
    /// 记录玩家观察的滚动历史，并延迟提供这些观察。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 采样由外部组合根驱动，而非 AI 主动拉取。这种反转是刻意的：Boss 从未持有玩家引用，
    /// 因此无法请求比提供给它的数据更及时的观察。
    /// </para>
    /// <para>
    /// 历史保存在固定容量环形缓冲区中，因此无论战斗持续多久，内存和每帧工作量都不变。
    /// </para>
    /// </remarks>
    public sealed class DelayedPerceptionSource : IPerceptionSource
    {
        /// <summary>在六十赫兹采样频率下约四秒的历史。</summary>
        public const int DefaultHistoryCapacity = 256;

        /// <summary>
        /// 保证容量能够覆盖的采样频率，单位为每秒样本数。
        /// </summary>
        /// <remarks>
        /// 缓冲区按样本计数，却按秒查询，因此帧率过高时，保存的真实时间跨度可能小于感知延迟。
        /// 此时 Boss 永远看不到足够早的历史，<c>TryGetPerceived</c> 每次都失败，整个战斗都会静止。
        /// 由于失去感知是安全失败方式，系统也不会报错。按此频率确定容量，可为合理帧率留足时间窗口，
        /// 固定开销仅几千字节。
        /// </remarks>
        private const float GuaranteedSamplesPerSecond = 2000f;

        /// <summary>感知延迟之外的额外历史，避免查询刚好处于边界。</summary>
        private const float HistoryMarginSeconds = 0.5f;

        private readonly CircularBuffer<PlayerObservation> _history;
        private readonly IObservablePlayer _player;

        private float _latencySeconds;
        private float _currentTime;

        /// <summary>创建观察所提供玩家的感知源。</summary>
        /// <param name="player">要采样的可观察玩家。</param>
        /// <param name="latencySeconds">感知数据应落后当前时刻的时长。</param>
        /// <param name="historyCapacity">
        /// 保留的样本数。设为零会按延迟计算容量，正式使用应如此；只有需要固定容量时才传入值，
        /// 例如边界测试。
        /// </param>
        /// <exception cref="ArgumentNullException">未提供玩家时抛出。</exception>
        public DelayedPerceptionSource(
            IObservablePlayer player,
            float latencySeconds,
            int historyCapacity = 0)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _latencySeconds = Mathf.Max(0f, latencySeconds);
            _history = new CircularBuffer<PlayerObservation>(
                historyCapacity > 0 ? historyCapacity : CapacityFor(_latencySeconds));
        }

        /// <summary>
        /// 在保证采样频率下覆盖延迟所需的样本数。
        /// </summary>
        /// <param name="latencySeconds">Boss 必须能够回看的时长。</param>
        /// <returns>覆盖延迟并留有余量的容量。</returns>
        public static int CapacityFor(float latencySeconds) =>
            Mathf.CeilToInt(
                (Mathf.Max(0f, latencySeconds) + HistoryMarginSeconds) * GuaranteedSamplesPerSecond);

        /// <inheritdoc />
        public float LatencySeconds => _latencySeconds;

        /// <summary>当前保留的样本数。</summary>
        public int SampleCount => _history.Count;

        /// <summary>
        /// 捕获一次观察。组合根应每帧调用一次。
        /// </summary>
        /// <param name="timestamp">当前战斗时钟时间，必须单调递增。</param>
        public void Sample(float timestamp)
        {
            _currentTime = timestamp;
            _history.Add(_player.CaptureObservation(timestamp));
        }

        /// <inheritdoc />
        public bool TryGetPerceived(out PlayerObservation observation) =>
            TryGetPerceivedAt(0f, out observation);

        /// <inheritdoc />
        public bool TryGetPerceivedAt(float secondsFurtherBack, out PlayerObservation observation)
        {
            observation = default;

            if (_history.Count == 0)
            {
                return false;
            }

            float targetTime = _currentTime - _latencySeconds - Mathf.Max(0f, secondsFurtherBack);

            // 历史尚未覆盖足够早的时间，这会发生在战斗刚开始时。Boss 没有可据以行动的数据，正符合预期。
            if (_history.Oldest.Timestamp > targetTime)
            {
                return false;
            }

            observation = _history[FindNewestIndexAtOrBefore(targetTime)];
            return observation.IsValid;
        }

        /// <summary>调整感知延迟，在 Boss 后续阶段感知更加敏锐时使用。</summary>
        /// <param name="latencySeconds">新的延迟。负值限制为零。</param>
        public void SetLatency(float latencySeconds) => _latencySeconds = Mathf.Max(0f, latencySeconds);

        /// <summary>丢弃全部历史，例如战斗重新开始时。</summary>
        public void Reset()
        {
            _history.Clear();
            _currentTime = 0f;
        }

        /// <summary>
        /// 在按时间排序的历史中二分查找不晚于指定时刻的最新样本。
        /// </summary>
        /// <param name="targetTime">样本时间戳上限。</param>
        /// <returns>历史缓冲区索引。调用者必须已确认历史覆盖该时刻。</returns>
        private int FindNewestIndexAtOrBefore(float targetTime)
        {
            int low = 0;
            int high = _history.Count - 1;
            int best = 0;

            while (low <= high)
            {
                int mid = low + (high - low) / 2;

                if (_history[mid].Timestamp <= targetTime)
                {
                    best = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return best;
        }
    }
}
