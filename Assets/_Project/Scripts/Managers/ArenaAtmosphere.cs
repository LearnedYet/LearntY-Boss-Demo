using AdaptiveBossArena.Core.Events;
using UnityEngine;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 首领升级时改变竞技场光照、氛围与雾。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 整场战斗都在同一平面圆盘上进行，空间本身无法告诉玩家局势正在恶化。这里让房间呼应首领：
    /// 开场冷色而均匀，施压时变暖、变紧，绝境时变成灼热朦胧的红色。呈现压力不断上升，而非进度条上的数值。
    /// </para>
    /// <para>
    /// 由咆哮与光环使用的同一首领阶段通道驱动，让整个世界响应同一信号。这里只读取通道，纯粹负责表现，
    /// 不向首领回传信息。使用未缩放时间平滑变化，确保顿帧期间的阶段切换也能自然过渡，而非瞬间跳变。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ArenaAtmosphere : MonoBehaviour
    {
        /// <summary>某一阶段房间氛围的完整配置。</summary>
        /// <remarks>
        /// 只配置灯光与环境色；雾不是独立配色，而是通过<see cref="HazeColor"/> 从这些值推导。
        /// 早期版本单独驱动雾色，导致近处角色之外陷入黑暗。由房间光照生成且有亮度下限的薄雾不会如此。
        /// </remarks>
        private readonly struct Profile
        {
            public Profile(
                Color light, float lightIntensity,
                Color sky, Color equator, Color ground)
            {
                Light = light;
                LightIntensity = lightIntensity;
                Sky = sky;
                Equator = equator;
                Ground = ground;
            }

            public Color Light { get; }
            public float LightIntensity { get; }
            public Color Sky { get; }
            public Color Equator { get; }
            public Color Ground { get; }
        }

        /// <summary>氛围过渡的半衰期，单位为秒。保持缓慢，使其像氛围变化而非开关。</summary>
        private const float TransitionHalfLife = 0.8f;

        /// <summary>
        /// 薄雾开始的距离；从战斗地面任意位置看，都超过地面对侧边缘，使交战角色和全部预警保持清晰，
        /// 只有废墟逐渐淡入雾中。
        /// </summary>
        public const float FogStart = 14f;

        /// <summary>薄雾完全生效的距离，大致位于最远外墙。</summary>
        public const float FogEnd = 60f;

        private static readonly Profile[] Profiles =
        {
            // 阶段 0：克制，冷色且均匀。
            new Profile(
                new Color(1f, 0.96f, 0.9f), 1.15f,
                new Color(0.22f, 0.24f, 0.30f), new Color(0.14f, 0.14f, 0.17f), new Color(0.06f, 0.06f, 0.08f)),

            // 阶段 1：施压，更暖，房间略显紧迫。
            new Profile(
                new Color(1f, 0.82f, 0.62f), 1.25f,
                new Color(0.34f, 0.24f, 0.22f), new Color(0.22f, 0.14f, 0.13f), new Color(0.11f, 0.07f, 0.07f)),

            // 阶段 2：猛烈，灼热而鲜红。
            new Profile(
                new Color(1f, 0.55f, 0.4f), 1.4f,
                new Color(0.46f, 0.18f, 0.17f), new Color(0.30f, 0.11f, 0.11f), new Color(0.16f, 0.06f, 0.06f)),

            // 阶段 3：最后一搏，强烈、过曝的绯红渐近紫色，比其他阶段更亮，让绝境阶段一眼显出战斗高潮。
            new Profile(
                new Color(1f, 0.4f, 0.5f), 1.6f,
                new Color(0.52f, 0.14f, 0.26f), new Color(0.34f, 0.09f, 0.18f), new Color(0.18f, 0.05f, 0.10f))
        };

        [SerializeField]
        [Tooltip("The arena's directional light, recoloured per phase.")]
        private Light _directionalLight;

        [SerializeField]
        [Tooltip("Carries the boss phase index that drives the mood.")]
        private IntEventChannel _phaseChannel;

        /// <summary>开场氛围下烘焙的光照探针，场景无探针时为 null。</summary>
        private UnityEngine.Rendering.SphericalHarmonicsL2[] _bakedProbes;

        private UnityEngine.Rendering.SphericalHarmonicsL2[] _tintedProbes;
        /// <summary>初始值刻意不可达，使首帧必定写入探针并应用环境光下限。</summary>
        private Color _probeGain = Color.clear;

        /// <summary>场景加载时的房间环境光，染色探针不能低于它。</summary>
        private UnityEngine.Rendering.SphericalHarmonicsL2 _ambientFloor;

        private int _targetPhase;
        private Color _lightColor;
        private float _lightIntensity;
        private Color _sky;
        private Color _equator;
        private Color _ground;

        private void OnEnable()
        {
            if (_phaseChannel != null)
            {
                _phaseChannel.Raised += OnPhaseChanged;
            }
        }

        private void OnDisable()
        {
            if (_phaseChannel != null)
            {
                _phaseChannel.Raised -= OnPhaseChanged;
            }
        }

        private void Start()
        {
            // 环境模式必须为 Trilight，三个色带才能生效；场景构建器已设置，这里连同雾再次确保，
            // 防止未来场景遗漏。
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = FogStart;
            RenderSettings.fogEndDistance = FogEnd;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;

            // 在任何染色前缓存；后续每次染色都以烘焙值为基准，不累乘。
            LightProbes probes = LightmapSettings.lightProbes;

            if (probes != null && probes.count > 0)
            {
                _bakedProbes = probes.bakedProbes;
                _ambientFloor = RenderSettings.ambientProbe;
                _tintedProbes = new UnityEngine.Rendering.SphericalHarmonicsL2[_bakedProbes.Length];
            }

            // 直接切到开场氛围，避免从编辑器遗留的设置渐变进入战斗。
            _targetPhase = 0;
            SnapTo(Profiles[0]);
            Apply();
        }

        private void Update()
        {
            Profile target = Profiles[Mathf.Clamp(_targetPhase, 0, Profiles.Length - 1)];
            float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.0001f, TransitionHalfLife));

            _lightColor = Color.Lerp(_lightColor, target.Light, t);
            _lightIntensity = Mathf.Lerp(_lightIntensity, target.LightIntensity, t);
            _sky = Color.Lerp(_sky, target.Sky, t);
            _equator = Color.Lerp(_equator, target.Equator, t);
            _ground = Color.Lerp(_ground, target.Ground, t);

            Apply();
        }

        /// <summary>分配引用，供场景生成器使用。</summary>
        /// <param name="directionalLight">竞技场方向光。</param>
        /// <param name="phaseChannel">首领阶段索引通道。</param>
        public void Bind(Light directionalLight, IntEventChannel phaseChannel)
        {
            _directionalLight = directionalLight;
            _phaseChannel = phaseChannel;
        }

        private void OnPhaseChanged(int phaseIndex) => _targetPhase = phaseIndex;

        private void SnapTo(Profile profile)
        {
            _lightColor = profile.Light;
            _lightIntensity = profile.LightIntensity;
            _sky = profile.Sky;
            _equator = profile.Equator;
            _ground = profile.Ground;
        }

        private void Apply()
        {
            if (_directionalLight != null)
            {
                _directionalLight.color = _lightColor;
                _directionalLight.intensity = _lightIntensity;
            }

            RenderSettings.ambientSkyColor = _sky;
            RenderSettings.ambientEquatorColor = _equator;
            RenderSettings.ambientGroundColor = _ground;
            RenderSettings.fogColor = HazeColor.Derive(_equator, _lightColor, _lightIntensity);

            TintBakedLight();
        }

        /// <summary>将当前氛围映射到开场氛围下烘焙的探针。</summary>
        /// <remarks>
        /// 增益未变化时跳过，因此只在阶段过渡时重写探针，而非整场战斗逐帧重写。
        /// </remarks>
        private void TintBakedLight()
        {
            if (_bakedProbes == null || LightmapSettings.lightProbes == null)
            {
                return;
            }

            Color gain = ProbeTint.Gain(_equator, Profiles[0].Equator);

            if (Mathf.Abs(gain.r - _probeGain.r) + Mathf.Abs(gain.g - _probeGain.g) + Mathf.Abs(gain.b - _probeGain.b) < 0.002f)
            {
                return;
            }

            _probeGain = gain;
            ProbeTint.Apply(_bakedProbes, _tintedProbes, gain, _ambientFloor);
            LightmapSettings.lightProbes.bakedProbes = _tintedProbes;
        }
    }
}
