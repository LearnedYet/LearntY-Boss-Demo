using AdaptiveBossArena.Core.Services;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// WebGL 与桌面构建之间不同的光照选择。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 有两项，均在场景启动时决定。桌面为太阳添加光照遮罩，呈现破屋顶横梁与倒塌石板投到地面的阴影；
    /// 网页端省略它，避免每个受光像素再多采样一张预算无法承受的纹理。
    /// </para>
    /// <para>
    /// 网页端的太阳光束更淡。相同叠加平面在浏览器中明显比桌面更亮，
    /// 因为桌面的抗锯齿和环境光遮蔽会柔化周围画面；同一光束在一端像氛围，在另一端像亮条。
    /// 切换第二个材质而非编辑共享材质，可保持资源原样及合批完整。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlatformLighting : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The arena's directional light, which takes the cookie on desktop.")]
        private Light _sun;

        [SerializeField]
        [Tooltip("The broken roof's shadow pattern, projected by the sun on desktop.")]
        private Texture2D _sunCookie;

        [SerializeField]
        [Min(1f)]
        [Tooltip("Metres of floor one repeat of the cookie covers.")]
        private float _cookieSize = 26f;

        [SerializeField]
        [Tooltip("The sun shafts, whose material is swapped on the web.")]
        private Renderer[] _shafts = new Renderer[0];

        [SerializeField]
        [Tooltip("The fainter shaft material the web build uses.")]
        private Material _webShaftMaterial;

        [SerializeField]
        [Tooltip("The global post-processing volume, which takes the cheaper profile on the web.")]
        private UnityEngine.Rendering.Volume _volume;

        [SerializeField]
        [Tooltip("The browser's post-processing profile: the same grade without grain or aberration.")]
        private UnityEngine.Rendering.VolumeProfile _webProfile;

        [SerializeField]
        [Tooltip("The camera, whose anti-aliasing is the cheap kind on the web.")]
        private UniversalAdditionalCameraData _camera;

        [SerializeField]
        [Tooltip("The drifting dust, thinned on the web.")]
        private ParticleSystem _dust;

        [SerializeField]
        [Tooltip("The candle lights, switched off on the web; their flames still glow.")]
        private Light[] _candles = new Light[0];

        /// <summary>网页端保留的太阳光束数；其余只有无法承受的重复绘制开销。</summary>
        public const int WebShaftCount = 3;

        /// <summary>网页端同时漂浮的尘埃上限，低于桌面的 260 个。</summary>
        public const int WebDustParticles = 80;

        /// <summary>平台是否采用较轻量的网页光照。</summary>
        /// <param name="platform">运行平台。</param>
        /// <returns>WebGL 播放器时为 true。</returns>
        public static bool UsesWebLighting(RuntimePlatform platform) => platform == RuntimePlatform.WebGLPlayer;

        /// <summary>分配光束及网页材质，供场景生成器使用。</summary>
        /// <param name="shafts">光束渲染器。</param>
        /// <param name="webShaftMaterial">网页端较淡的材质。</param>
        public void BindShafts(Renderer[] shafts, Material webShaftMaterial)
        {
            _shafts = shafts ?? new Renderer[0];
            _webShaftMaterial = webShaftMaterial;
        }

        /// <summary>分配太阳与光照遮罩，供场景生成器使用。</summary>
        /// <param name="sun">方向光。</param>
        /// <param name="cookie">屋顶阴影图案。</param>
        public void BindSun(Light sun, Texture2D cookie)
        {
            _sun = sun;
            _sunCookie = cookie;
        }

        /// <summary>分配网页端将简化的后处理与相机，供场景生成器使用。</summary>
        /// <param name="volume">全局后处理体积。</param>
        /// <param name="webProfile">浏览器配置。</param>
        /// <param name="camera">主相机的 URP 数据。</param>
        public void BindWebBudget(
            UnityEngine.Rendering.Volume volume, UnityEngine.Rendering.VolumeProfile webProfile, UniversalAdditionalCameraData camera)
        {
            _volume = volume;
            _webProfile = webProfile;
            _camera = camera;
        }

        /// <summary>分配网页端将减少的场景装饰，供场景生成器使用。</summary>
        /// <param name="dust">漂浮尘埃。</param>
        /// <param name="candles">蜡烛灯光。</param>
        public void BindSceneBudget(ParticleSystem dust, Light[] candles)
        {
            _dust = dust;
            _candles = candles ?? new Light[0];
        }

        /// <summary>体积是否使用浏览器的低成本配置渲染，公开供测试。</summary>
        public bool RendersWebProfile => _volume != null && _webProfile != null && _volume.sharedProfile == _webProfile;

        // 在所有 Start 之前执行，使 ScreenEffects 克隆实际画质使用的配置。
        private void Awake()
        {
            RememberFullScene();
            Apply(GraphicsTier.Current);
            GraphicsTier.Changed += OnQualityChanged;
        }

        private void OnDestroy() => GraphicsTier.Changed -= OnQualityChanged;

        private void OnQualityChanged(GraphicsQuality quality)
        {
            Apply(quality);

            // 屏幕效果写入体积自己的配置副本；切换刚刚替换了该副本。
            FindAnyObjectByType<ScreenEffects>()?.RebindProfile();
        }

        /// <summary>构建时的场景状态，使画质降低后还能恢复。</summary>
        private Material[] _fullShaftMaterials;
        private UnityEngine.Rendering.VolumeProfile _fullProfile;
        private AntialiasingMode _fullAntialiasing;
        private int _fullDustParticles;
        private ParticleSystem.MinMaxCurve _fullDustRate;
        private bool _remembered;

        private void RememberFullScene()
        {
            if (_remembered)
            {
                return;
            }

            _remembered = true;
            _fullShaftMaterials = new Material[_shafts.Length];

            for (int i = 0; i < _shafts.Length; i++)
            {
                _fullShaftMaterials[i] = _shafts[i] != null ? _shafts[i].sharedMaterial : null;
            }

            _fullProfile = _volume != null ? _volume.sharedProfile : null;
            _fullAntialiasing = _camera != null ? _camera.antialiasing : AntialiasingMode.None;

            if (_dust != null)
            {
                _fullDustParticles = _dust.main.maxParticles;
                _fullDustRate = _dust.emission.rateOverTime;
            }
        }

        /// <summary>应用某个平台的光照，保留供比较两个平台的测试使用。</summary>
        /// <param name="web">浏览器最简光照时为 true，完整场景时为 false。</param>
        public void Apply(bool web) => Apply(web ? GraphicsQuality.Low : GraphicsQuality.High);

        /// <summary>
        /// 应用指定画质的光照：低画质预算，或恢复完整场景。
        /// </summary>
        /// <param name="quality">竞技场使用的画质。</param>
        public void Apply(GraphicsQuality quality)
        {
            RememberFullScene();
            bool web = quality == GraphicsQuality.Low;

            if (_sun != null)
            {
                _sun.cookie = web ? null : _sunCookie;

                // URP 通过自身灯光数据而非内置属性设置方向光遮罩尺寸。
                if (_sun.TryGetComponent(out UniversalAdditionalLightData data))
                {
                    data.lightCookieSize = new Vector2(_cookieSize, _cookieSize);
                }
            }

            if (!web)
            {
                RestoreFullScene();
                return;
            }

            for (int i = 0; i < _shafts.Length; i++)
            {
                if (_shafts[i] == null)
                {
                    continue;
                }

                if (_webShaftMaterial != null)
                {
                    _shafts[i].sharedMaterial = _webShaftMaterial;
                }

                // 每个光束都是覆盖大块屏幕的高叠加平面；三个仍足以表现太阳光。
                _shafts[i].enabled = i < WebShaftCount;
            }

            ApplyWebBudget();
        }

        /// <summary>
        /// 降低浏览器在笔记本集成显卡上的渲染成本。
        /// </summary>
        /// <remarks>
        /// 每项削减都尽量少影响外观：低成本配置保留完整调色；快速近似抗锯齿只需一次处理，
        /// 子像素方案需要三次；只移除蜡烛逐像素照明，火焰仍通过泛光发亮；尘埃减少而非消失。
        /// </remarks>
        private void ApplyWebBudget()
        {
            SwapProfile(_webProfile);

            if (_camera != null)
            {
                _camera.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            }

            if (_dust != null)
            {
                ParticleSystem.MainModule main = _dust.main;
                main.maxParticles = WebDustParticles;

                ParticleSystem.EmissionModule emission = _dust.emission;
                emission.rateOverTime = 6f;
            }

            foreach (Light candle in _candles)
            {
                if (candle != null)
                {
                    candle.enabled = false;
                }
            }
        }

        /// <summary>恢复低画质预算移除的全部内容。</summary>
        private void RestoreFullScene()
        {
            for (int i = 0; i < _shafts.Length; i++)
            {
                if (_shafts[i] == null)
                {
                    continue;
                }

                if (_fullShaftMaterials != null && i < _fullShaftMaterials.Length && _fullShaftMaterials[i] != null)
                {
                    _shafts[i].sharedMaterial = _fullShaftMaterials[i];
                }

                _shafts[i].enabled = true;
            }

            SwapProfile(_fullProfile);

            if (_camera != null)
            {
                _camera.antialiasing = _fullAntialiasing;
            }

            if (_dust != null && _fullDustParticles > 0)
            {
                ParticleSystem.MainModule main = _dust.main;
                main.maxParticles = _fullDustParticles;

                ParticleSystem.EmissionModule emission = _dust.emission;
                emission.rateOverTime = _fullDustRate;
            }

            foreach (Light candle in _candles)
            {
                if (candle != null)
                {
                    candle.enabled = true;
                }
            }
        }

        /// <summary>
        /// 让体积使用另一份配置渲染。
        /// </summary>
        /// <remarks>
        /// 任何对象读取体积配置后，体积都会使用自己的私有副本，忽略共享配置，
        /// 因此过去在战斗开始后切换共享配置不会改变画面。清除副本后，下次读取会克隆新配置。
        /// </remarks>
        private void SwapProfile(UnityEngine.Rendering.VolumeProfile profile)
        {
            if (_volume == null || profile == null || _volume.sharedProfile == profile)
            {
                return;
            }

            _volume.sharedProfile = profile;
            _volume.profile = null;
        }
    }
}
