using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试持续危险区域使用的地面裂痕表现。
    /// </summary>
    /// <remarks>
    /// 原先它是硬边的平坦红色圆盘，看起来只是地面标记。裂痕必须在外缘前淡出，
    /// 确保绘制用四边形的边缘永远不可见，并沿裂缝发光。
    /// </remarks>
    [TestFixture]
    public sealed class HazardScarTests
    {
        private Texture2D _scar;

        [SetUp]
        public void LoadTheScar()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/HazardDisc.mat");
            Assume.That(material != null, "Setup has not generated the hazard material.");

            _scar = material.GetTexture("_BaseMap") as Texture2D;
            Assert.IsNotNull(_scar, "The hazard is drawn untextured, as a flat disc.");
        }

        [Test]
        public void TheScarFadesOutBeforeTheQuadsEdge()
        {
            foreach (var uv in new[] { new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0f), new Vector2(0.02f, 0.02f) })
            {
                Assert.Less(_scar.GetPixelBilinear(uv.x, uv.y).a, 0.02f, "The scar's edge is visible at " + uv + ".");
            }
        }

        [Test]
        public void TheScarIsDarkWithGlowingCracks()
        {
            float brightest = 0f, darkestOpaque = 1f;

            for (int y = 0; y < _scar.height; y += 1)
            {
                for (int x = 0; x < _scar.width; x += 1)
                {
                    Color c = _scar.GetPixel(x, y);

                    if (c.a > 0.5f)
                    {
                        brightest = Mathf.Max(brightest, c.r);
                        darkestOpaque = Mathf.Min(darkestOpaque, c.r);
                    }
                }
            }

            Assert.Greater(brightest, 0.8f, "No crack glows.");
            Assert.Less(darkestOpaque, 0.15f, "No part of the scar is scorched dark.");
        }
    }
}
