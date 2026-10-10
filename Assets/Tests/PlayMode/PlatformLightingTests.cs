using System.Collections;
using System.Linq;
using AdaptiveBossArena.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AdaptiveBossArena.Tests.PlayMode
{
    /// <summary>
    /// 测试各平台获得独立光照：桌面有屋顶阴影，网页使用较淡光束。
    /// </summary>
    [TestFixture]
    public sealed class PlatformLightingTests
    {
        private PlatformLighting _lighting;
        private Light _sun;

        [UnitySetUp]
        public IEnumerator LoadTheArena()
        {
            SceneManager.LoadScene("Arena", LoadSceneMode.Single);

            yield return null;
            yield return null;

            _lighting = Object.FindAnyObjectByType<PlatformLighting>();
            _sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);

            Assert.IsNotNull(_lighting, "The arena has no platform lighting.");
            Assert.IsNotNull(_sun, "The arena has no sun.");
        }

        [UnityTest]
        public IEnumerator OnlyTheWebPlayerTakesTheWebLighting()
        {
            yield return null;

            Assert.IsTrue(PlatformLighting.UsesWebLighting(RuntimePlatform.WebGLPlayer));
            Assert.IsFalse(PlatformLighting.UsesWebLighting(RuntimePlatform.WindowsPlayer));
            Assert.IsFalse(PlatformLighting.UsesWebLighting(RuntimePlatform.WindowsEditor));
        }

        [UnityTest]
        public IEnumerator TheDesktopSunCastsTheRoofsShadow()
        {
            _lighting.Apply(web: false);

            yield return null;

            Assert.IsNotNull(_sun.cookie, "The desktop sun has no cookie.");
        }

        [UnityTest]
        public IEnumerator TheRoofsShadowIsAnAccentNotACover()
        {
            // 最初的光照遮罩阻挡大部分日光，在运行构建中测得整帧平均亮度从 0.38降至 0.21，
            // 使竞技场像黄昏。交叉厅上方应直通天空。
            _lighting.Apply(web: false);

            yield return null;

            var cookie = (Texture2D)_sun.cookie;
            Color[] pixels = cookie.GetPixels();

            float sum = 0f, darkest = 1f;

            foreach (Color pixel in pixels)
            {
                sum += pixel.r;
                darkest = Mathf.Min(darkest, pixel.r);
            }

            Assert.GreaterOrEqual(sum / pixels.Length, 0.85f, "The roof's shadow blocks too much of the sun overall.");
            Assert.GreaterOrEqual(darkest, 0.5f - 0.01f, "The roof's shadow blacks the sun out somewhere.");
        }

        [UnityTest]
        public IEnumerator SteppingQualityBackUpRestoresTheFullScene()
        {
            // 现在战斗中可双向切换画质。低画质原先仅在加载时单向削减。
            _lighting.Apply(Core.Services.GraphicsQuality.Low);
            yield return null;
            _lighting.Apply(Core.Services.GraphicsQuality.High);
            yield return null;

            Assert.IsFalse(_lighting.RendersWebProfile, "Stepping back up left the browser's cheaper profile in place.");

            Transform shafts = GameObject.Find("LightShafts").transform;
            Assert.IsTrue(shafts.GetComponentsInChildren<Renderer>().All(r => r.enabled), "A sun shaft stayed off.");

            Assert.IsTrue(Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                    .Where(l => l.name.StartsWith("CandleLight")).All(l => l.enabled),
                "A candle stayed dark.");
        }

        [UnityTest]
        public IEnumerator TheWebRendersACheaperFrame()
        {
            _lighting.Apply(web: true);

            yield return null;

            Assert.IsTrue(_lighting.RendersWebProfile, "The web still renders the desktop's profile.");

            Transform shafts = GameObject.Find("LightShafts").transform;
            int lit = shafts.GetComponentsInChildren<Renderer>().Count(r => r.enabled);
            Assert.AreEqual(PlatformLighting.WebShaftCount, lit, "The web draws every sun shaft.");

            Assert.IsFalse(Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                    .Any(l => l.type == LightType.Point && l.enabled && l.name.StartsWith("CandleLight")),
                "A candle still lights the web build per pixel.");

            // 光束旁边：冲击爆发也有自己的“Dust”子对象。
            ParticleSystem dust = shafts.parent.Find("Dust").GetComponent<ParticleSystem>();
            Assert.LessOrEqual(dust.main.maxParticles, PlatformLighting.WebDustParticles, "The web keeps all the dust.");
        }

        [UnityTest]
        public IEnumerator TheWebDrawsFainterShaftsAndNoCookie()
        {
            // 在自身父对象下查找：立柱也有自己的 Shaft_0。
            Renderer shaft = GameObject.Find("LightShafts").transform.GetChild(0).GetComponent<Renderer>();
            float desktopAlpha = shaft.sharedMaterial.GetColor("_BaseColor").a;

            _lighting.Apply(web: true);

            yield return null;

            Assert.IsNull(_sun.cookie, "The web sun still carries the cookie.");
            Assert.Less(shaft.sharedMaterial.GetColor("_BaseColor").a, desktopAlpha * 0.6f,
                "The web shafts are not meaningfully fainter than the desktop ones.");
        }
    }
}
