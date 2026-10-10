using System.Collections;
using AdaptiveBossArena.Core.Constants;
using AdaptiveBossArena.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AdaptiveBossArena.Tests.PlayMode
{
    /// <summary>
    /// 测试教堂装饰竞技场，却不改变战斗。
    /// </summary>
    /// <remarks>
    /// 环境最容易在不改 AI 代码时破坏 AI：战斗地面的一个多余碰撞体便会阻挡冲锋、
    /// 闪避或首领定位假定畅通的视线，而AI 本身的所有测试仍通过。
    /// </remarks>
    [TestFixture]
    public sealed class CathedralTests
    {
        private const float Radius = 16f;

        [UnitySetUp]
        public IEnumerator LoadTheArena()
        {
            SceneManager.LoadScene("Arena", LoadSceneMode.Single);

            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheCathedralIsInTheScene()
        {
            yield return null;

            Assert.IsNotNull(GameObject.Find("Cathedral"), "The arena was generated without its cathedral.");
        }

        [UnityTest]
        public IEnumerator TheFloorIsSolidEverywhereAFighterCanStand()
        {
            yield return null;

            int mask = 1 << Layers.Arena;

            for (float x = -Radius + 1f; x <= Radius - 1f; x += 2f)
            {
                for (float z = -Radius + 1f; z <= Radius - 1f; z += 2f)
                {
                    if (x * x + z * z > (Radius - 1f) * (Radius - 1f))
                    {
                        continue;
                    }

                    bool hit = Physics.Raycast(new Vector3(x, 5f, z), Vector3.down, out RaycastHit floor, 10f, mask);

                    Assert.IsTrue(hit, $"No floor under ({x}, {z}).");
                    Assert.AreEqual(0f, floor.point.y, 0.05f, $"The floor under ({x}, {z}) is not at ground level.");
                }
            }
        }

        [UnityTest]
        public IEnumerator NothingOfTheRuinStandsOnTheFightingFloor()
        {
            yield return null;

            // 对竞技场层上每个实体，测量它在身体高度处距离地面中央的最近位置。
            // 地板自身最近点在该高度下方，墙环最近点则位于其半径，
            // 因此任何其它位于内部的对象都是干扰物。对竞技场使用的盒体计算精确。
            var body = new Vector3(0f, 1.2f, 0f);

            foreach (Collider collider in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (collider.gameObject.layer != Layers.Arena || collider.isTrigger)
                {
                    continue;
                }

                Vector3 nearest = collider.ClosestPoint(body);
                float fromCentre = new Vector2(nearest.x, nearest.z).magnitude;

                Assert.IsFalse(
                    nearest.y > 0.3f && fromCentre < Radius - 0.6f,
                    $"{collider.name} stands on the fighting floor, {fromCentre:F1} m from its centre.");
            }

            Collider[] dressing = GameObject.Find("Cathedral").GetComponentsInChildren<Collider>(true);

            Assert.IsEmpty(dressing, "Cathedral dressing carries a collider: " +
                (dressing.Length > 0 ? dressing[0].name : ""));
        }

        [UnityTest]
        public IEnumerator TheBakedLightingSurvivesTheSceneBeingRebuilt()
        {
            // 每次设置都通过代码重新生成场景，没有界面，也没有可用于烘焙的 GPU。
            // 只有每次重建都重新采用烘焙结果，烘焙才有价值。
            yield return null;

            ReflectionProbe probe = Object.FindAnyObjectByType<ReflectionProbe>();

            Assert.IsNotNull(probe, "The arena has no reflection probe.");
            Assert.AreEqual(UnityEngine.Rendering.ReflectionProbeMode.Custom, probe.mode, "The reflection probe lost its bake.");
            Assert.IsNotNull(probe.customBakedTexture, "The reflection probe has no baked cubemap.");

            Assert.IsNotNull(LightmapSettings.lightProbes, "The arena lost its baked light probes.");
            Assert.Greater(LightmapSettings.lightProbes.count, 0, "The arena lost its baked light probes.");
        }

        [UnityTest]
        public IEnumerator TheHazeIsOnAndNeverBlack()
        {
            yield return null;
            yield return null;

            Assert.IsTrue(RenderSettings.fog, "The haze is off.");
            Assert.GreaterOrEqual(
                HazeColor.Luminance(RenderSettings.fogColor), HazeColor.MinimumLuminance - 0.001f,
                "The haze has gone dark, which is how fog was lost last time.");
        }
    }
}
