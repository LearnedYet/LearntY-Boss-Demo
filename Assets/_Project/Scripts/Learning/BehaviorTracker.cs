using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Utilities;
using AdaptiveBossArena.Utilities.Statistics;
using UnityEngine;

namespace AdaptiveBossArena.Learning
{
    /// <summary>
    /// 观察战斗，并维护每项习惯背后的累计统计。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 订阅战斗事件总线，低频采样距离。这里只负责记录，不做决策。这里的内容绝不会直接触发首领行动，
    /// 因此可以逐帧运行，而不会重新引入即时反应。
    /// </para>
    /// <para>
    /// 两种机制让画像逐渐淡化。连续量采用带半衰期的指数加权平均值，闪避方向直方图按相同节奏衰减。
    /// 玩家不再重复某项习惯后，首领会停止针对它反制，使改变战术成为真正有效的应对，而非只是寄希望于它。
    /// </para>
    /// </remarks>
    public sealed class BehaviorTracker
    {
        /// <summary>闪避后将攻击视为追击的时间窗口。</summary>
        private const float PostDodgeWindowSeconds = 0.6f;

        /// <summary>归一化时采用的实际每秒攻击次数上限。</summary>
        private const float MaxSustainableAttacksPerSecond = 2.5f;

        /// <summary>归一化间距时视为竞技场最远距离的数值。</summary>
        private const float MaxMeaningfulDistance = 14f;

        /// <summary>距离采样频率。采用粗粒度，因为它服务于趋势而非即时反应。</summary>
        private const float DistanceSampleIntervalSeconds = 0.2f;

        private readonly CombatMemory _memory;
        private readonly DirectionHistogram _dashDirections = new DirectionHistogram();

        private readonly Ewma _attackRate;
        private readonly Ewma _separation;
        private readonly Ewma _approachTendency;

        private readonly float _memoryHalfLifeSeconds;

        private float _lastDodgeAt = float.NegativeInfinity;
        private float _lastDistanceSampleAt;
        private float _lastDistance = -1f;
        private float _lastEventTime;

        private int _postDodgeAttacks;
        private int _dodgesFollowedByAnything;
        private int _distanceSamples;

        /// <summary>创建跟踪器。</summary>
        /// <param name="memory">记录观察事件的位置。</param>
        /// <param name="memoryHalfLifeSeconds">
        /// 未被强化的习惯影响衰减至一半所需的时间。这是决定首领对玩家画像有多固执的最重要参数。
        /// </param>
        public BehaviorTracker(CombatMemory memory, float memoryHalfLifeSeconds)
        {
            _memory = memory;
            _memoryHalfLifeSeconds = Mathf.Max(1f, memoryHalfLifeSeconds);

            _attackRate = new Ewma(_memoryHalfLifeSeconds);
            _separation = new Ewma(_memoryHalfLifeSeconds);
            _approachTendency = new Ewma(_memoryHalfLifeSeconds);
        }

        /// <summary>观察到的闪避方向，随时间衰减。</summary>
        public DirectionHistogram DashDirections => _dashDirections;

        /// <summary>平滑后的玩家攻击频率，单位为次每秒。</summary>
        public float AttacksPerSecond => _attackRate.Value;

        /// <summary>平滑后的交战双方间距，单位为世界单位。</summary>
        public float AverageSeparation => _separation.Value;

        /// <summary>平滑后的逼近而非后退倾向，范围为负一至一。</summary>
        public float ApproachTendency => _approachTendency.Value;

        /// <summary>在追击窗口内接上攻击的闪避次数。</summary>
        public int PostDodgeAttacks => _postDodgeAttacks;

        /// <summary>有机会接上攻击的闪避次数。</summary>
        public int DodgeOpportunities => _dodgesFollowedByAnything;

        /// <summary>已采集的距离样本数。</summary>
        public int DistanceSampleCount => _distanceSamples;

        /// <summary>记录一个观察到的事件。</summary>
        /// <param name="combatEvent">发生的事件。</param>
        public void OnCombatEvent(in CombatEvent combatEvent)
        {
            _memory.Record(combatEvent);

            float elapsed = Mathf.Max(0f, combatEvent.Timestamp - _lastEventTime);
            _lastEventTime = combatEvent.Timestamp;

            if (combatEvent.Actor != CombatantTeam.Player)
            {
                return;
            }

            switch (combatEvent.Kind)
            {
                case CombatEventKind.AttackStarted:
                    RecordPlayerAttack(combatEvent, elapsed);
                    break;

                case CombatEventKind.DodgePerformed:
                    RecordPlayerDodge(combatEvent);
                    break;
            }
        }

        /// <summary>采样连续量，并使画像向中性状态衰减。</summary>
        /// <param name="deltaTime">经过缩放的时间增量。</param>
        /// <param name="timeNow">当前战斗时钟时间。</param>
        /// <param name="separationDistance">交战双方当前的距离。</param>
        public void Tick(float deltaTime, float timeNow, float separationDistance)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            _dashDirections.Decay(_memoryHalfLifeSeconds, deltaTime);

            // 没有动作发生时，攻击频率向零衰减，因此停止施压的玩家不再被判断为具有进攻性。
            _attackRate.DecayToward(0f, deltaTime);

            if (timeNow - _lastDistanceSampleAt < DistanceSampleIntervalSeconds)
            {
                return;
            }

            float sampleInterval = timeNow - _lastDistanceSampleAt;
            _lastDistanceSampleAt = timeNow;

            SampleSeparation(separationDistance, sampleInterval);
        }

        /// <summary>将距离读数计入间距和逼近倾向的平均值。</summary>
        private void SampleSeparation(float separationDistance, float sampleInterval)
        {
            if (separationDistance <= 0f || float.IsInfinity(separationDistance))
            {
                return;
            }

            _separation.AddSample(separationDistance, sampleInterval);
            _distanceSamples++;

            if (_lastDistance >= 0f)
            {
                // 距离缩短时为正。这是判断玩家进攻性的可观察依据：不是攻击次数多，
                // 而是主动选择贴近对手。
                float closing = Mathf.Clamp(_lastDistance - separationDistance, -1f, 1f);
                _approachTendency.AddSample(closing, sampleInterval);
            }

            _lastDistance = separationDistance;
        }

        /// <summary>记录攻击，并检查它是否接在闪避之后。</summary>
        private void RecordPlayerAttack(in CombatEvent combatEvent, float elapsed)
        {
            float instantaneousRate = elapsed > 0f ? 1f / elapsed : MaxSustainableAttacksPerSecond;
            _attackRate.AddSample(
                Mathf.Min(instantaneousRate, MaxSustainableAttacksPerSecond), elapsed);

            if (combatEvent.Timestamp - _lastDodgeAt <= PostDodgeWindowSeconds)
            {
                _postDodgeAttacks++;

                // 消耗此记录，避免一次闪避被计作多次追击。
                _lastDodgeAt = float.NegativeInfinity;
            }
        }

        /// <summary>记录闪避及其方向。</summary>
        private void RecordPlayerDodge(in CombatEvent combatEvent)
        {
            _lastDodgeAt = combatEvent.Timestamp;
            _dodgesFollowedByAnything++;

            _dashDirections.Add(MathUtil.FlattenToPlane(combatEvent.Direction));
        }

        /// <summary>按实际攻击频率上限归一化。</summary>
        /// <returns>零至一之间的攻击频率。</returns>
        public float NormalizedAttackFrequency() =>
            Mathf.Clamp01(_attackRate.Value / MaxSustainableAttacksPerSecond);

        /// <summary>按竞技场有效范围归一化平均间距。</summary>
        /// <returns>偏好距离，零表示近身交战，一表示保持距离牵制。</returns>
        public float NormalizedPreferredDistance() =>
            Mathf.Clamp01(_separation.Value / MaxMeaningfulDistance);

        /// <summary>将逼近倾向映射到零至一的范围。</summary>
        /// <returns>进攻性，零表示后退，一表示施压。</returns>
        public float NormalizedAggression() => Mathf.Clamp01(_approachTendency.Value * 0.5f + 0.5f);

        /// <summary>清除全部统计值，供战斗重试使用。</summary>
        public void Reset()
        {
            _dashDirections.Reset();
            _attackRate.Reset();
            _separation.Reset();
            _approachTendency.Reset();

            _lastDodgeAt = float.NegativeInfinity;
            _lastDistanceSampleAt = 0f;
            _lastDistance = -1f;
            _lastEventTime = 0f;

            _postDodgeAttacks = 0;
            _dodgesFollowedByAnything = 0;
            _distanceSamples = 0;
        }
    }
}
