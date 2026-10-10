using AdaptiveBossArena.Core.Services;
using NUnit.Framework;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试命中停顿、慢动作与暂停的唯一管理者。
    /// </summary>
    /// <remarks>
    /// 组合规则比单个效果更重要。典型故障是在命中停顿生效时进入暂停，恢复后还原错误时间倍率，
    /// 使游戏永久处于慢动作。这些测试固定避免该故障的优先级顺序。
    /// </remarks>
    [TestFixture]
    public sealed class TimeServiceTests
    {
        private const float Frame = 1f / 60f;
        private const float Tolerance = 0.0001f;

        private float _originalTimeScale;
        private float _originalFixedDeltaTime;

        [SetUp]
        public void CaptureEngineTimeState()
        {
            // 服务会写入全局引擎状态，因此随后还原，避免影响本次会话后续运行的任何测试。
            _originalTimeScale = Time.timeScale;
            _originalFixedDeltaTime = Time.fixedDeltaTime;
        }

        [TearDown]
        public void RestoreEngineTimeState()
        {
            Time.timeScale = _originalTimeScale;
            Time.fixedDeltaTime = _originalFixedDeltaTime;
        }

        [Test]
        public void HitStop_LeavesThePhysicsStepAtItsBaseSize()
        {
            // 步长原先会随时间倍率降低至约 1.7e-6 秒。尚未使用 Rigidbody时无害，
            // 布娃娃使用该步长则会停止正确解析关节。
            var service = new TimeService(Frame);
            service.RequestHitStop(0.1f);

            service.Tick(Frame);

            Assert.AreEqual(Frame, service.FixedDeltaTime, Tolerance);
            Assert.AreEqual(Frame, Time.fixedDeltaTime, Tolerance);
        }

        [Test]
        public void Pause_NeverHandsTheEngineAZeroPhysicsStep()
        {
            // 暂停原先直接写入零值，而引擎不接受零步长。
            var service = new TimeService(Frame);

            service.SetPaused(true);

            Assert.AreEqual(Frame, service.FixedDeltaTime, Tolerance);
            Assert.Greater(Time.fixedDeltaTime, 0f);
        }

        [Test]
        public void SlowMotion_ShrinksThePhysicsStepWithTheClock()
        {
            // 保留原先行为：慢动作中的物理在屏幕上仍与正常速度同样平滑，而非明显变得粗糙。
            var service = new TimeService(Frame);
            service.RequestSlowMotion(0.5f, 1f);

            service.Tick(Frame);

            Assert.AreEqual(Frame * 0.5f, service.FixedDeltaTime, Tolerance);
        }

        [Test]
        public void DeepSlowMotion_CannotShrinkThePhysicsStepPastItsFloor()
        {
            var service = new TimeService(Frame);
            service.RequestSlowMotion(0.01f, 1f);

            service.Tick(Frame);

            Assert.AreEqual(0.01f, service.TimeScale, Tolerance);
            Assert.AreEqual(Frame * 0.25f, service.FixedDeltaTime, Tolerance);
        }

        [Test]
        public void WithNoEffects_TimeRunsAtNormalSpeed()
        {
            var service = new TimeService();

            service.Tick(Frame);

            Assert.AreEqual(1f, service.TimeScale, Tolerance);
            Assert.AreEqual(Frame, service.DeltaTime, Tolerance);
            Assert.AreEqual(Frame, service.CombatTime, Tolerance);
        }

        [Test]
        public void HitStop_FreezesScaledTimeButNotRealTime()
        {
            var service = new TimeService();
            service.RequestHitStop(0.1f);

            service.Tick(Frame);

            Assert.Less(service.TimeScale, 0.01f);
            Assert.AreEqual(Frame, service.UnscaledDeltaTime, Tolerance);
            Assert.Less(service.DeltaTime, 0.001f);
        }

        [Test]
        public void HitStop_ExpiresAfterItsRealTimeDuration()
        {
            var service = new TimeService();
            service.RequestHitStop(0.05f);

            for (int i = 0; i < 4; i++)
            {
                service.Tick(Frame);
            }

            Assert.AreEqual(1f, service.TimeScale, Tolerance);
        }

        [Test]
        public void HitStop_TakesTheLongerOfTwoOverlappingRequests()
        {
            var service = new TimeService();

            service.RequestHitStop(0.12f);
            service.Tick(Frame);
            service.RequestHitStop(0.03f);

            // 重击冻结期间到来的轻击不能缩短较重冲击的停顿。
            for (int i = 0; i < 4; i++)
            {
                service.Tick(Frame);
            }

            Assert.Less(service.TimeScale, 0.01f, "The shorter request must not have shortened the freeze.");
        }

        [Test]
        public void SlowMotion_ScalesTimeAndThenRestoresIt()
        {
            var service = new TimeService();
            service.RequestSlowMotion(0.35f, 0.1f);

            service.Tick(Frame);
            Assert.AreEqual(0.35f, service.TimeScale, Tolerance);

            for (int i = 0; i < 8; i++)
            {
                service.Tick(Frame);
            }

            Assert.AreEqual(1f, service.TimeScale, Tolerance);
        }

        [Test]
        public void HitStop_TakesPrecedenceOverSlowMotion()
        {
            var service = new TimeService();
            service.RequestSlowMotion(0.3f, 1f);
            service.RequestHitStop(0.1f);

            service.Tick(Frame);

            Assert.Less(service.TimeScale, 0.01f);
        }

        [Test]
        public void Pause_TakesPrecedenceOverEverythingElse()
        {
            var service = new TimeService();
            service.RequestSlowMotion(0.3f, 1f);
            service.RequestHitStop(0.5f);
            service.Tick(Frame);

            service.SetPaused(true);

            Assert.AreEqual(0f, service.TimeScale, Tolerance);
            Assert.IsTrue(service.IsPaused);
        }

        [Test]
        public void Unpause_RestoresTheEffectThatWasInFlight()
        {
            var service = new TimeService();
            service.RequestSlowMotion(0.4f, 10f);
            service.Tick(Frame);

            service.SetPaused(true);
            service.Tick(Frame);
            service.SetPaused(false);
            service.Tick(Frame);

            // 此测试防止恢复暂停后直接全速并丢弃慢动作，更糟的是恢复为永不清除的过期倍率。
            Assert.AreEqual(0.4f, service.TimeScale, Tolerance);
        }

        [Test]
        public void WhilePaused_EffectTimersDoNotExpire()
        {
            var service = new TimeService();
            service.RequestSlowMotion(0.5f, 0.2f);
            service.Tick(Frame);

            service.SetPaused(true);
            for (int i = 0; i < 60; i++)
            {
                service.Tick(Frame);
            }

            service.SetPaused(false);
            service.Tick(Frame);

            Assert.AreEqual(0.5f, service.TimeScale, Tolerance);
        }

        [Test]
        public void CombatTime_AdvancesWithScaledTimeOnly()
        {
            var service = new TimeService();

            service.Tick(Frame);
            float afterNormalFrame = service.CombatTime;

            service.RequestHitStop(0.2f);
            for (int i = 0; i < 5; i++)
            {
                service.Tick(Frame);
            }

            // 观察时间戳使用此时钟，因此命中停顿也必须冻结首领感知，
            // 不能交给它比战斗实际产生的信息更新的数据。
            Assert.AreEqual(afterNormalFrame, service.CombatTime, 0.001f);
        }

        [Test]
        public void ClearTimeEffects_ReturnsToNormalSpeedImmediately()
        {
            var service = new TimeService();
            service.RequestHitStop(1f);
            service.RequestSlowMotion(0.2f, 1f);
            service.Tick(Frame);

            service.ClearTimeEffects();
            service.Tick(Frame);

            Assert.AreEqual(1f, service.TimeScale, Tolerance);
        }

        [Test]
        public void ResetCombatClock_ReturnsTheClockToZero()
        {
            var service = new TimeService();
            service.Tick(Frame);
            service.Tick(Frame);

            service.ResetCombatClock();

            Assert.AreEqual(0f, service.CombatTime, Tolerance);
        }
    }
}
