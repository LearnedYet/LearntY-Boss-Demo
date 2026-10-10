using System.Collections;
using AdaptiveBossArena.AI;
using AdaptiveBossArena.Game;
using AdaptiveBossArena.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AdaptiveBossArena.Tests.PlayMode
{
    /// <summary>
    /// 测试竞技场相机在真实战斗中保持稳定。
    /// </summary>
    /// <remarks>
    /// 击倒、挑飞、墙边推挤与冲锋使双方遍布竞技场并穿过彼此空间，这些场景从未验证过构图。
    /// 单帧大幅甩动的相机，无论各帧单独看多好，都像故障。
    /// </remarks>
    [TestFixture]
    public sealed class CameraStabilityTests
    {
        /// <summary>60 Hz 下相机朝向单帧允许转动的最大角度，超过便像镜头切换。</summary>
        private const float MaximumTurnPerFrameDegrees = 12f;

        [UnitySetUp]
        public IEnumerator LoadTheArena()
        {
            SceneManager.LoadScene("Arena", LoadSceneMode.Single);

            float waited = 0f;

            do
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            while (Time.timeScale <= 0f && waited < 15f);
        }

        [UnityTest]
        public IEnumerator TheCameraNeverWhipsDuringAFight()
        {
            var rig = Object.FindAnyObjectByType<ArenaCameraRig>();
            var player = Object.FindAnyObjectByType<PlayerController>();
            var boss = Object.FindAnyObjectByType<BossController>();

            Assert.IsNotNull(rig);

            Vector3 previous = Flat(rig.transform.forward);
            float worst = 0f, worstSeparation = 0f;
            int whips = 0;
            float elapsed = 0f;

            while (elapsed < 20f)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;

                // 归一化至 60 Hz，避免将慢帧误认为快速转动。
                float frames = Mathf.Max(1f, Time.unscaledDeltaTime * 60f);
                Vector3 heading = Flat(rig.transform.forward);
                float turn = Vector3.Angle(previous, heading) / frames;
                previous = heading;

                if (turn > MaximumTurnPerFrameDegrees)
                {
                    whips++;
                }

                if (turn > worst)
                {
                    worst = turn;
                    worstSeparation = Vector3.Distance(player.transform.position, boss.transform.position);
                }
            }

            Debug.Log($"[CAMERA] worst turn {worst:F1} deg/frame at fighter separation {worstSeparation:F2} m, frames over limit {whips}");

            Assert.LessOrEqual(worst, MaximumTurnPerFrameDegrees,
                $"The camera whipped {worst:F1} degrees in one frame, with the fighters {worstSeparation:F2} m apart ({whips} frames over).");
        }

        [UnityTest]
        public IEnumerator TheCameraCanSeeThePlayerPinnedAgainstTheWall()
        {
            // 击退和墙边推挤如今经常制造的情况：玩家靠着墙环，首领在中间，相机位于玩家身后，也就是墙外。
            var rig = Object.FindAnyObjectByType<ArenaCameraRig>();
            var player = Object.FindAnyObjectByType<PlayerController>();
            var boss = Object.FindAnyObjectByType<BossController>();

            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var context = (PlayerContext)typeof(PlayerController).GetField("_context", flags).GetValue(player);

            int occluded = 0, frames = 0;
            float worstRadius = 0f;

            foreach (Vector3 side in new[] { Vector3.back, Vector3.left, Vector3.right, Vector3.forward })
            {
                context.Motor.Teleport(side * 15.2f);
                boss.transform.position = new Vector3(0f, boss.transform.position.y, 0f);

                float settle = 0f;

                while (settle < 1.5f)
                {
                    yield return null;
                    settle += Time.unscaledDeltaTime;
                    context.Motor.Teleport(side * 15.2f);
                }

                Camera camera = rig.GetComponentInChildren<Camera>();
                Vector3 eye = camera.transform.position;
                Vector3 chest = player.transform.position + Vector3.up * 1.2f;

                frames++;
                worstRadius = Mathf.Max(worstRadius, new Vector2(eye.x, eye.z).magnitude);

                if (Physics.Linecast(eye, chest, 1 << Core.Constants.Layers.Arena))
                {
                    occluded++;
                }
            }

            Debug.Log($"[CAMERA] pinned against the wall: occluded from {occluded} of {frames} sides, camera out to radius {worstRadius:F1} m");

            Assert.AreEqual(0, occluded, $"The wall hid the player from the camera on {occluded} of {frames} sides (camera at radius {worstRadius:F1} m).");
        }

        [UnityTest]
        public IEnumerator LockOnKeepsTheBruteInViewThroughACircleStrafe()
        {
            // 玩家反馈锁定有问题。锁定的职责是无论骑士如何绕行，巨兽都不离开屏幕，
            // 因此让骑士以快于任何真实横移的速度绕巨兽完整一圈，并逐帧检查巨兽胸口是否在屏幕内。
            var rig = Object.FindAnyObjectByType<ArenaCameraRig>();
            var player = Object.FindAnyObjectByType<PlayerController>();
            var boss = Object.FindAnyObjectByType<BossController>();

            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var context = (PlayerContext)typeof(PlayerController).GetField("_context", flags).GetValue(player);
            typeof(ArenaCameraRig).GetField("_isLockedOn", flags).SetValue(rig, true);

            // 骑士绕行期间，没有其它模块移动巨兽。
            boss.enabled = false;
            Vector3 centre = new Vector3(0f, boss.transform.position.y, 0f);
            boss.transform.position = centre;

            Camera camera = rig.GetComponentInChildren<Camera>();
            const float Radius = 4f;
            const float DegreesPerSecond = 120f;
            float elapsed = 0f;
            int frames = 0, lost = 0;

            while (elapsed < 3.4f)
            {
                float angle = elapsed * DegreesPerSecond * Mathf.Deg2Rad;
                context.Motor.Teleport(centre + new Vector3(Mathf.Sin(angle), 0f, -Mathf.Cos(angle)) * Radius);

                yield return null;
                elapsed += Time.unscaledDeltaTime;

                // 第一帧留出稳定时间，之后逐帧计入。
                if (elapsed < 0.4f)
                {
                    continue;
                }

                Vector3 seen = camera.WorldToViewportPoint(centre + Vector3.up * 1.9f);
                frames++;

                if (seen.z <= 0f || seen.x < 0.05f || seen.x > 0.95f || seen.y < 0.05f || seen.y > 0.95f)
                {
                    lost++;
                }
            }

            Debug.Log($"[CAMERA] lock-on circle strafe: brute off screen on {lost} of {frames} frames");

            Assert.LessOrEqual(lost, frames / 20, $"Locked on, the brute left the screen on {lost} of {frames} frames.");
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
        }
    }
}
