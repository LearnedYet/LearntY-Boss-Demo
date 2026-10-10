using AdaptiveBossArena.Player.Controls;
using NUnit.Framework;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试让按键跨过角色暂时无法行动时段的输入缓存。
    /// </summary>
    /// <remarks>
    /// 正确缓存时玩家毫无察觉，缓存损坏则立即明显：玩家会认为游戏丢失了输入。
    /// 这些测试固定两项良好手感所需的属性：按键能保留短暂动作锁定期，且每次按键恰好消费一次。
    /// </remarks>
    [TestFixture]
    public sealed class InputBufferTests
    {
        private const float Window = 0.15f;

        /// <summary>脚本化输入源，使缓存无需真实设备即可测试。</summary>
        private sealed class FakePlayerInput : IPlayerInput
        {
            private PlayerInputAction? _pressedThisFrame;

            public Vector2 MoveDirection { get; set; }

            public bool IsEnabled { get; private set; } = true;

            public void Press(PlayerInputAction action) => _pressedThisFrame = action;

            public void ReleaseAll() => _pressedThisFrame = null;

            public bool WasPressedThisFrame(PlayerInputAction action) => _pressedThisFrame == action;

            public bool IsHeld(PlayerInputAction action) => _pressedThisFrame == action;

            public void SetEnabled(bool enabled) => IsEnabled = enabled;
        }

        [Test]
        public void FreshBuffer_HoldsNothing()
        {
            var buffer = new InputBuffer(Window);

            Assert.IsFalse(buffer.HasBuffered(PlayerInputAction.Dash, 0f));
            Assert.IsFalse(buffer.TryConsume(PlayerInputAction.Dash, 0f));
        }

        [Test]
        public void PressWithinWindow_CanBeConsumed()
        {
            var buffer = new InputBuffer(Window);
            buffer.Record(PlayerInputAction.Dash, 10f);

            Assert.IsTrue(buffer.TryConsume(PlayerInputAction.Dash, 10f + Window * 0.5f));
        }

        [Test]
        public void PressAtExactlyTheWindowEdge_IsStillValid()
        {
            var buffer = new InputBuffer(Window);
            buffer.Record(PlayerInputAction.Dash, 10f);

            // 边界上的差一错误会把宽容的缓存变成丢失输入的缓存，而玩家会认为自己明明按得及时。
            Assert.IsTrue(buffer.TryConsume(PlayerInputAction.Dash, 10f + Window));
        }

        [Test]
        public void PressBeyondTheWindow_HasExpired()
        {
            var buffer = new InputBuffer(Window);
            buffer.Record(PlayerInputAction.Dash, 10f);

            Assert.IsFalse(buffer.TryConsume(PlayerInputAction.Dash, 10f + Window + 0.01f));
        }

        [Test]
        public void Consuming_SpendsThePressExactlyOnce()
        {
            var buffer = new InputBuffer(Window);
            buffer.Record(PlayerInputAction.Dash, 10f);

            Assert.IsTrue(buffer.TryConsume(PlayerInputAction.Dash, 10f));

            // 两个状态若都响应一次按键，轻点一下就会同时触发闪避和攻击。
            Assert.IsFalse(buffer.TryConsume(PlayerInputAction.Dash, 10f));
        }

        [Test]
        public void HasBuffered_DoesNotSpendThePress()
        {
            var buffer = new InputBuffer(Window);
            buffer.Record(PlayerInputAction.Dash, 10f);

            Assert.IsTrue(buffer.HasBuffered(PlayerInputAction.Dash, 10f));
            Assert.IsTrue(buffer.TryConsume(PlayerInputAction.Dash, 10f));
        }

        [Test]
        public void ActionsAreBufferedIndependently()
        {
            var buffer = new InputBuffer(Window);
            buffer.Record(PlayerInputAction.Dash, 10f);
            buffer.Record(PlayerInputAction.HeavyAttack, 10f);

            Assert.IsTrue(buffer.TryConsume(PlayerInputAction.Dash, 10f));
            Assert.IsTrue(buffer.TryConsume(PlayerInputAction.HeavyAttack, 10f));
        }

        [Test]
        public void RepeatedPresses_KeepOnlyTheMostRecent()
        {
            var buffer = new InputBuffer(Window);

            buffer.Record(PlayerInputAction.LightAttack, 10f);
            buffer.Record(PlayerInputAction.LightAttack, 10.05f);

            Assert.IsTrue(buffer.TryConsume(PlayerInputAction.LightAttack, 10.05f));

            // 排队保存多次按键会让长动画结束后角色自行执行乱按输入。每个动作始终只保留一次按键。
            Assert.IsFalse(buffer.TryConsume(PlayerInputAction.LightAttack, 10.05f));
        }

        [Test]
        public void Clear_DiscardsEverything()
        {
            var buffer = new InputBuffer(Window);
            buffer.Record(PlayerInputAction.Dash, 10f);
            buffer.Record(PlayerInputAction.Special, 10f);

            buffer.Clear();

            Assert.IsFalse(buffer.HasBuffered(PlayerInputAction.Dash, 10f));
            Assert.IsFalse(buffer.HasBuffered(PlayerInputAction.Special, 10f));
        }

        [Test]
        public void Discard_RemovesOneActionOnly()
        {
            var buffer = new InputBuffer(Window);
            buffer.Record(PlayerInputAction.Dash, 10f);
            buffer.Record(PlayerInputAction.Heal, 10f);

            buffer.Discard(PlayerInputAction.Dash);

            Assert.IsFalse(buffer.HasBuffered(PlayerInputAction.Dash, 10f));
            Assert.IsTrue(buffer.HasBuffered(PlayerInputAction.Heal, 10f));
        }

        [Test]
        public void RecordPressesFrom_CapturesEveryActionFiredThisFrame()
        {
            var buffer = new InputBuffer(Window);
            var input = new FakePlayerInput();

            input.Press(PlayerInputAction.Special);
            buffer.RecordPressesFrom(input, 5f);

            Assert.IsTrue(buffer.HasBuffered(PlayerInputAction.Special, 5f));
            Assert.IsFalse(buffer.HasBuffered(PlayerInputAction.Dash, 5f));
        }

        [Test]
        public void RecordPressesFrom_WithNothingPressed_RecordsNothing()
        {
            var buffer = new InputBuffer(Window);
            var input = new FakePlayerInput();

            input.ReleaseAll();
            buffer.RecordPressesFrom(input, 5f);

            Assert.IsFalse(buffer.HasBuffered(PlayerInputAction.Dash, 5f));
        }
    }
}
