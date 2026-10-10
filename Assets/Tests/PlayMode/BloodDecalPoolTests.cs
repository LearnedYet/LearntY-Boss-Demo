using System.Collections;
using AdaptiveBossArena.Combat.Feel;
using AdaptiveBossArena.Core.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AdaptiveBossArena.Tests.PlayMode
{
    /// <summary>
    /// 测试攻击下方会产生地面血迹，且积累量永远不超过对象池容量。
    /// </summary>
    [TestFixture]
    public sealed class BloodDecalPoolTests
    {
        [UnityTest]
        public IEnumerator SplattersAreLaidOnTheFloorAndRecycledOldestFirst()
        {
            var host = new GameObject("BloodHost");
            var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));

            try
            {
                BloodDecalPool pool = host.AddComponent<BloodDecalPool>();
                pool.Construct(material, new XorShiftRandomProvider(7u));

                pool.Place(new Vector3(2f, 1.1f, 3f), Vector3.forward, 1f);
                yield return null;

                Assert.AreEqual(1, pool.VisibleCount, "A blow left no blood.");

                Transform splatter = host.transform.GetChild(0);
                Assert.Less(splatter.position.y, 0.05f, "The blood is not on the floor.");

                for (int i = 0; i < BloodDecalPool.Capacity * 2; i++)
                {
                    pool.Place(new Vector3(i, 1f, 0f), Vector3.right, 1f);
                }

                yield return null;

                Assert.AreEqual(BloodDecalPool.Capacity, pool.VisibleCount, "Blood piled up past its pool.");
            }
            finally
            {
                Object.Destroy(host);
                Object.Destroy(material);
            }
        }
    }
}
