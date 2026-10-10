using AdaptiveBossArena.Core.Perception;
using NUnit.Framework;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试使首领按人类时间尺度反应的感知延迟。
    /// </summary>
    /// <remarks>
    /// 这是防作弊设计的运行时部分，程序集依赖图是编译时部分。若延迟降至零，首领就会响应当前帧，
    /// 佯攻也会失效，因此直接断言保障，而非等待试玩发现。
    /// </remarks>
    [TestFixture]
    public sealed class DelayedPerceptionSourceTests
    {
        private const float Latency = 0.15f;
        private const float SampleInterval = 0.02f;

        /// <summary>
        /// 玩家替身的位置编码其采样时间，使任何观察都能追溯至捕获时刻。
        /// </summary>
        private sealed class TimestampEncodingPlayer : IObservablePlayer
        {
            public ObservableActionState State { get; set; } = ObservableActionState.Idle;

            public PlayerObservation CaptureObservation(float timestamp) => new PlayerObservation
            {
                Timestamp = timestamp,
                Position = new Vector3(timestamp, 0f, 0f),
                ActionState = State,
                NormalizedHealth = 1f,
                IsValid = true
            };
        }

        [Test]
        public void BeforeHistoryCoversTheLatency_NothingIsPerceived()
        {
            var player = new TimestampEncodingPlayer();
            var source = new DelayedPerceptionSource(player, Latency);

            source.Sample(0f);
            source.Sample(SampleInterval);

            // 战斗开头短时间内，首领合理地没有任何可据以行动的信息。
            Assert.IsFalse(source.TryGetPerceived(out _));
        }

        [Test]
        public void OncePopulated_PerceivedObservationLagsByTheConfiguredLatency()
        {
            var player = new TimestampEncodingPlayer();
            var source = new DelayedPerceptionSource(player, Latency);

            float time = 0f;
            for (int i = 0; i < 100; i++)
            {
                source.Sample(time);
                time += SampleInterval;
            }

            float now = time - SampleInterval;

            Assert.IsTrue(source.TryGetPerceived(out PlayerObservation observation));

            float actualLag = now - observation.Timestamp;

            // 返回截止时刻之前或当时的最新采样，因此延迟介于配置值与配置值加一个采样间隔之间。
            Assert.GreaterOrEqual(actualLag, Latency - 0.0001f);
            Assert.LessOrEqual(actualLag, Latency + SampleInterval + 0.0001f);
        }

        [Test]
        public void PerceivedObservation_NeverReflectsTheCurrentFrame()
        {
            var player = new TimestampEncodingPlayer();
            var source = new DelayedPerceptionSource(player, Latency);

            float time = 0f;
            for (int i = 0; i < 100; i++)
            {
                source.Sample(time);
                time += SampleInterval;
            }

            // 只在最新一帧切换玩家可见动作。
            player.State = ObservableActionState.HeavyAttacking;
            source.Sample(time);

            Assert.IsTrue(source.TryGetPerceived(out PlayerObservation observation));
            Assert.AreEqual(
                ObservableActionState.Idle,
                observation.ActionState,
                "The boss must not see an action that only began this frame.");
        }

        [Test]
        public void AKnockdownReachesTheBossOnlyAfterTheLatency()
        {
            // 两种站姿状态是首领侧感知防火墙的新信息，因此享有与其它动作相同的保障：延后看见，
            // 绝不能在发生当帧看见。
            var player = new TimestampEncodingPlayer();
            var source = new DelayedPerceptionSource(player, Latency);

            float time = 0f;
            for (int i = 0; i < 100; i++)
            {
                source.Sample(time);
                time += SampleInterval;
            }

            player.State = ObservableActionState.KnockedDown;
            float flooredAt = time;

            while (time - flooredAt < Latency + SampleInterval * 2f)
            {
                source.Sample(time);
                Assert.IsTrue(source.TryGetPerceived(out PlayerObservation seen));

                if (seen.ActionState == ObservableActionState.KnockedDown)
                {
                    Assert.GreaterOrEqual(time - flooredAt, Latency - 0.0001f, "The boss saw the knockdown early.");
                    return;
                }

                time += SampleInterval;
            }

            Assert.Fail("The boss never saw the knockdown at all.");
        }

        [Test]
        public void TryGetPerceivedAt_LooksFurtherIntoThePast()
        {
            var player = new TimestampEncodingPlayer();
            var source = new DelayedPerceptionSource(player, Latency);

            float time = 0f;
            for (int i = 0; i < 200; i++)
            {
                source.Sample(time);
                time += SampleInterval;
            }

            Assert.IsTrue(source.TryGetPerceived(out PlayerObservation recent));
            Assert.IsTrue(source.TryGetPerceivedAt(0.5f, out PlayerObservation older));

            Assert.Less(older.Timestamp, recent.Timestamp);
            Assert.AreEqual(0.5f, recent.Timestamp - older.Timestamp, SampleInterval + 0.0001f);
        }

        [Test]
        public void TryGetPerceivedAt_BeyondRetainedHistory_ReturnsFalse()
        {
            var player = new TimestampEncodingPlayer();
            var source = new DelayedPerceptionSource(player, Latency);

            float time = 0f;
            for (int i = 0; i < 50; i++)
            {
                source.Sample(time);
                time += SampleInterval;
            }

            Assert.IsFalse(source.TryGetPerceivedAt(60f, out _));
        }

        [Test]
        public void SetLatency_ChangesHowFarBackThePerceivedSampleSits()
        {
            var player = new TimestampEncodingPlayer();
            var source = new DelayedPerceptionSource(player, Latency);

            float time = 0f;
            for (int i = 0; i < 200; i++)
            {
                source.Sample(time);
                time += SampleInterval;
            }

            Assert.IsTrue(source.TryGetPerceived(out PlayerObservation beforeChange));

            source.SetLatency(0.4f);
            Assert.IsTrue(source.TryGetPerceived(out PlayerObservation afterChange));

            Assert.Less(afterChange.Timestamp, beforeChange.Timestamp);
        }

        [Test]
        public void Reset_DiscardsHistory()
        {
            var player = new TimestampEncodingPlayer();
            var source = new DelayedPerceptionSource(player, Latency);

            float time = 0f;
            for (int i = 0; i < 100; i++)
            {
                source.Sample(time);
                time += SampleInterval;
            }

            source.Reset();

            Assert.AreEqual(0, source.SampleCount);
            Assert.IsFalse(source.TryGetPerceived(out _));
        }

        [Test]
        public void HistoryIsBounded_SoALongFightCostsConstantMemory()
        {
            var player = new TimestampEncodingPlayer();
            var source = new DelayedPerceptionSource(player, Latency, historyCapacity: 64);

            float time = 0f;
            for (int i = 0; i < 5000; i++)
            {
                source.Sample(time);
                time += SampleInterval;
            }

            Assert.AreEqual(64, source.SampleCount);
        }

        [Test]
        public void OneActionKeepsOneStartTimeAcrossEveryFrameItIsSeenIn()
        {
            // 观察者将一次挥击视为单一事件所需的身份标识。Timestamp每帧递增，
            // 因此按它区分事件会把一次挥击当成每秒六十个新事件，每事件概率便累积至近乎必然。
            // 首领招架随机判定正是如此，少量基础概率就从近乎休眠变成“招架每次重击”。
            var early = new PlayerObservation
            {
                IsValid = true,
                Timestamp = 10.1f,
                TimeInActionState = 0.1f,
                ActionState = ObservableActionState.HeavyAttacking
            };

            var late = new PlayerObservation
            {
                IsValid = true,
                Timestamp = 10.9f,
                TimeInActionState = 0.9f,
                ActionState = ObservableActionState.HeavyAttacking
            };

            Assert.AreEqual(
                early.ActionStartedAt, late.ActionStartedAt, 0.0001f,
                "The same swing reported two different start times.");
        }

        [Test]
        public void ASecondActionReportsADifferentStartTime()
        {
            var first = new PlayerObservation
            {
                IsValid = true,
                Timestamp = 10.9f,
                TimeInActionState = 0.9f,
                ActionState = ObservableActionState.HeavyAttacking
            };

            var second = new PlayerObservation
            {
                IsValid = true,
                Timestamp = 11.0f,
                TimeInActionState = 0.0f,
                ActionState = ObservableActionState.HeavyAttacking
            };

            // 连续两次挥击。若被合并为同一开始时间，首领只会决策一次，并将结果用于后续每次挥击。
            Assert.AreNotEqual(first.ActionStartedAt, second.ActionStartedAt);
        }
    }
}
