using System.Collections;
using AdaptiveBossArena.Combat.Feel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AdaptiveBossArena.Tests.PlayMode
{
    /// <summary>
    /// 测试挥动刀刃留下的拖影。
    /// </summary>
    /// <remarks>
    /// 原先它是身体旁固定点后方的一条带状图形，绕战斗者画圆，挥击结束后像平面卡片一样悬在空中。
    /// </remarks>
    [TestFixture]
    public sealed class BladeTrailTests
    {
        [UnityTest]
        public IEnumerator ASwingLaysDownASmearAlongTheBladeThatFadesAway()
        {
            var pivot = new GameObject("Blade");
            WeaponTrail trail = pivot.AddComponent<WeaponTrail>();
            trail.Configure(0.3f, 1f, Color.white, null);

            try
            {
                trail.Begin();

                // 在五分之一秒内让刀刃扫过四分之一圈。
                for (float t = 0f; t < 0.2f; t += Time.deltaTime)
                {
                    pivot.transform.rotation = Quaternion.Euler(0f, t / 0.2f * 90f, 0f);
                    yield return null;
                }

                Assert.GreaterOrEqual(trail.VisibleSamples, 2, "The swing left no smear.");

                var surface = GameObject.Find("BladeSmear");
                Assert.IsNotNull(surface, "The smear is not drawn in world space.");

                Bounds bounds = surface.GetComponent<MeshFilter>().sharedMesh.bounds;
                Assert.Greater(bounds.max.magnitude, 0.9f, "The smear does not reach the blade's tip.");
                Assert.Less(Vector3.Distance(bounds.center, Vector3.zero), 1f, "The smear is not where the blade swept.");

                float waited = 0f;

                while (waited < 1.5f)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }

                Assert.IsFalse(trail.IsEmitting, "The blade kept emitting after its swing.");
                Assert.AreEqual(0, trail.VisibleSamples, "The smear never faded.");
            }
            finally
            {
                Object.Destroy(pivot);
            }
        }
    }
}
