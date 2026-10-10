using AdaptiveBossArena.Game;
using NUnit.Framework;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 断言第三人称摄像机无论朝向哪里都保持地平线水平。
    /// </summary>
    /// <remarks>
    /// 俯仰以前在朝向旋转后绕世界 X 施加。看向 Z时是俯仰，看向 X 时变成滚转，绕圈时地平线倾斜；
    /// 双方沿一轴时少见，击退、抛起和冲锋使其遍及全场后不断出现。
    /// </remarks>
    [TestFixture]
    public sealed class CameraOrbitTests
    {
        private const float Pitch = 12f;

        [Test]
        public void TheHorizonStaysLevelAtEveryHeading([NUnit.Framework.Range(0, 345, 15)] int headingDegrees)
        {
            Vector3 forward = Quaternion.Euler(0f, headingDegrees, 0f) * Vector3.forward;
            Quaternion rotation = ArenaCameraRig.OrbitRotation(forward, Pitch);

            Assert.AreEqual(0f, (rotation * Vector3.right).y, 0.001f, "The camera is rolled.");
        }

        [Test]
        public void ItLooksDownByThePitchAlongTheHeading([NUnit.Framework.Range(0, 315, 45)] int headingDegrees)
        {
            Vector3 forward = Quaternion.Euler(0f, headingDegrees, 0f) * Vector3.forward;
            Vector3 look = ArenaCameraRig.OrbitRotation(forward, Pitch) * Vector3.forward;

            var flat = new Vector3(look.x, 0f, look.z).normalized;

            Assert.AreEqual(1f, Vector3.Dot(flat, forward), 0.001f, "The camera is not looking along its heading.");
            Assert.AreEqual(-Mathf.Sin(Pitch * Mathf.Deg2Rad), look.y, 0.001f, "The camera's pitch changes with heading.");
        }

        [Test]
        public void TheCameraLooksPastTheShoulderRatherThanThroughTheKnight([NUnit.Framework.Range(0, 315, 45)] int headingDegrees)
        {
            // 越肩视角：骑士肩部 pivot 位于画面中心线左侧，距离正好为肩偏移，完整 boom 位于镜头前。
            // 近距离从后方直看骑士，会让背部遮住巨兽。
            var pivot = new Vector3(3f, 1.55f, -2f);
            Quaternion view = ArenaCameraRig.OrbitRotation(
                Quaternion.Euler(0f, headingDegrees, 0f) * Vector3.forward, Pitch);

            Vector3 lens = ArenaCameraRig.ShoulderPosition(pivot, view, boom: 2.8f, shoulder: 0.55f);
            Vector3 pivotSeen = Quaternion.Inverse(view) * (pivot - lens);

            Assert.AreEqual(-0.55f, pivotSeen.x, 0.001f, "The knight is not held to the left of the frame.");
            Assert.AreEqual(0f, pivotSeen.y, 0.001f, "The pivot is not level with the view's centre line.");
            Assert.AreEqual(2.8f, pivotSeen.z, 0.001f, "The lens is not the boom's length behind the pivot.");
        }
    }
}
