using AdaptiveBossArena.Core.Events;
using AdaptiveBossArena.Core.Services;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 根据战斗状态驱动后处理体积。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 体积配置原本是固定外观，这里让它响应当前时刻：玩家濒死时画面变红、降低饱和度，受击时跳动，
    /// 精准弹反时伴随镜头冲击闪亮。它是顿帧与震动的屏幕对应反馈，让玩家在理解之前先感受到。
    /// </para>
    /// <para>
    /// 修改的是 <see cref="Volume.profile"/> 逐实例副本，而非共享资源，因此这里的变化不会写回磁盘。
    /// 两种短暂冲击遵守减少闪光的无障碍设置；持续低生命调色是缓慢柔和变化，不是闪光，所以保留。
    /// 全部使用未缩放时间，以便顿帧期间仍可读。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ScreenEffects : MonoBehaviour
    {
        /// <summary>危险调色开始生效的生命比例阈值。</summary>
        private const float LowHealthThreshold = 0.4f;

        /// <summary>危险调色完全生效的生命比例。</summary>
        private const float LowHealthFloor = 0.08f;

        private const float LowHealthVignetteAdd = 0.22f;
        private const float LowHealthSaturationDrop = 45f;
        private const float LowHealthEaseHalfLife = 0.35f;

        private const float DamagePulseVignette = 0.14f;
        private const float DeflectChromaticAdd = 0.55f;
        private const float DeflectBloomAdd = 0.7f;
        private const float PulseDecayPerSecond = 3.5f;

        /// <summary>心跳开始播放的生命比例阈值。</summary>
        private const float HeartbeatStartHealth = 0.35f;

        /// <summary>心跳刚开始时两拍之间的秒数。</summary>
        private const float HeartbeatSlowSeconds = 1.1f;

        /// <summary>濒死时两拍之间的秒数，表现急促心跳。</summary>
        private const float HeartbeatFastSeconds = 0.42f;

        private static readonly Color LowHealthVignetteColor = new Color(0.5f, 0.02f, 0.03f);

        [SerializeField]
        [Tooltip("The global post-processing volume whose profile is driven.")]
        private Volume _volume;

        [SerializeField]
        [Tooltip("Normalised player health, driving the low-health grade and the damage pulse.")]
        private FloatEventChannel _playerHealthChannel;

        [SerializeField]
        [Tooltip("Raised on a clean deflect, driving the lens punch.")]
        private VoidEventChannel _deflectChannel;

        private Vignette _vignette;
        private ColorAdjustments _color;
        private ChromaticAberration _aberration;
        private Bloom _bloom;

        private float _baseVignette;
        private Color _baseVignetteColor;
        private float _baseSaturation;
        private float _baseAberration;
        private float _baseBloom;

        private IAudioService _audio;

        private float _health = 1f;
        private float _lowHealthEased;
        private float _damagePulse;
        private float _deflectPunch;
        private float _heartbeatTimer;

        private void OnEnable()
        {
            if (_playerHealthChannel != null)
            {
                _playerHealthChannel.Raised += OnHealthChanged;
            }

            if (_deflectChannel != null)
            {
                _deflectChannel.Raised += OnDeflect;
            }
        }

        private void OnDisable()
        {
            if (_playerHealthChannel != null)
            {
                _playerHealthChannel.Raised -= OnHealthChanged;
            }

            if (_deflectChannel != null)
            {
                _deflectChannel.Raised -= OnDeflect;
            }
        }

        private void Start()
        {
            if (_volume == null || _volume.profile == null)
            {
                enabled = false;
                return;
            }

            RebindProfile();
            ServiceRegistry.Current.TryGet(out _audio);
        }

        /// <summary>
        /// 画质切换替换配置后，再次读取体积配置中的效果。
        /// </summary>
        /// <remarks>
        /// 脉冲和暗角写入体积自己的配置副本。切换会替换该副本，若不重新读取，
        /// 每次命中仍会写入已经不再绘制的旧副本。
        /// </remarks>
        public void RebindProfile()
        {
            if (_volume == null)
            {
                return;
            }

            // 首次访问时克隆逐实例配置，使运行时修改绝不影响资源。
            VolumeProfile profile = _volume.profile;

            profile.TryGet(out _vignette);
            profile.TryGet(out _color);
            profile.TryGet(out _aberration);
            profile.TryGet(out _bloom);

            _baseVignette = _vignette != null ? _vignette.intensity.value : 0f;
            _baseVignetteColor = _vignette != null ? _vignette.color.value : Color.black;
            _baseSaturation = _color != null ? _color.saturation.value : 0f;
            _baseAberration = _aberration != null ? _aberration.intensity.value : 0f;
            _baseBloom = _bloom != null ? _bloom.intensity.value : 0f;
        }

        private void Update()
        {
            float deltaTime = Time.unscaledDeltaTime;

            _damagePulse = Mathf.Max(0f, _damagePulse - PulseDecayPerSecond * deltaTime);
            _deflectPunch = Mathf.Max(0f, _deflectPunch - PulseDecayPerSecond * deltaTime);

            float lowHealthTarget = 1f - Mathf.InverseLerp(LowHealthFloor, LowHealthThreshold, _health);
            float ease = 1f - Mathf.Exp(-deltaTime / Mathf.Max(0.0001f, LowHealthEaseHalfLife));
            _lowHealthEased = Mathf.Lerp(_lowHealthEased, lowHealthTarget, ease);

            TickHeartbeat(deltaTime);
            Apply();
        }

        /// <summary>
        /// 玩家濒死时播放心跳，生命越低节奏越快。
        /// </summary>
        /// <remarks>
        /// 采用声音而非闪光，因此抑制闪光的玩家仍会听见；它是变红暗角所传达“你快死了”的声音对应信号。
        /// 玩家死亡或脱离危险时立即停止。
        /// </remarks>
        private void TickHeartbeat(float deltaTime)
        {
            bool dying = _health > 0.001f && _health <= HeartbeatStartHealth;

            if (!dying || _audio == null)
            {
                // 重置，使下次接近死亡时立即播放第一拍。
                _heartbeatTimer = 0f;
                return;
            }

            _heartbeatTimer -= deltaTime;

            if (_heartbeatTimer <= 0f)
            {
                _audio.PlayCue2D(AudioService.Cues.Heartbeat);

                float urgency = Mathf.Clamp01(Mathf.InverseLerp(HeartbeatStartHealth, LowHealthFloor, _health));
                _heartbeatTimer = Mathf.Lerp(HeartbeatSlowSeconds, HeartbeatFastSeconds, urgency);
            }
        }

        /// <summary>分配引用，供场景生成器使用。</summary>
        /// <param name="volume">全局后处理体积。</param>
        /// <param name="playerHealth">归一化的玩家生命通道。</param>
        /// <param name="deflect">精准弹反通道。</param>
        public void Bind(Volume volume, FloatEventChannel playerHealth, VoidEventChannel deflect)
        {
            _volume = volume;
            _playerHealthChannel = playerHealth;
            _deflectChannel = deflect;
        }

        private void OnHealthChanged(float normalized)
        {
            // 生命下降表示受击；玩家要求减少闪光时抑制冲击。
            if (normalized < _health - Mathf.Epsilon && !FeedbackSettings.ReducedFlashing)
            {
                _damagePulse = 1f;
            }

            _health = normalized;
        }

        private void OnDeflect()
        {
            if (!FeedbackSettings.ReducedFlashing)
            {
                _deflectPunch = 1f;
            }
        }

        /// <summary>
        /// 写入调色，但只写变化的数值。
        /// </summary>
        /// <remarks>
        /// 写入体积参数会标记后处理栈重新评估，饱和度变化还会重建颜色查找表。
        /// 满生命且没有受击效果时这里不发生变化，因此弱显卡浏览器不应每秒为其支付六十次开销。
        /// </remarks>
        private void Apply()
        {
            if (_vignette != null)
            {
                SetIfChanged(_vignette.intensity,
                    _baseVignette + _lowHealthEased * LowHealthVignetteAdd + _damagePulse * DamagePulseVignette);

                float redward = Mathf.Clamp01(_lowHealthEased * 0.85f + _damagePulse);
                Color colour = Color.Lerp(_baseVignetteColor, LowHealthVignetteColor, redward);

                if (!Approximately(_vignette.color.value, colour))
                {
                    _vignette.color.value = colour;
                }
            }

            if (_color != null)
            {
                SetIfChanged(_color.saturation, _baseSaturation - _lowHealthEased * LowHealthSaturationDrop);
            }

            if (_aberration != null)
            {
                SetIfChanged(_aberration.intensity, Mathf.Clamp01(_baseAberration + _deflectPunch * DeflectChromaticAdd));
            }

            if (_bloom != null)
            {
                SetIfChanged(_bloom.intensity, _baseBloom + _deflectPunch * DeflectBloomAdd);
            }
        }

        /// <summary>小于此幅度的调色变化不可见，不值得写入。</summary>
        private const float WriteEpsilon = 0.0005f;

        private static void SetIfChanged(VolumeParameter<float> parameter, float value)
        {
            if (Mathf.Abs(parameter.value - value) > WriteEpsilon)
            {
                parameter.value = value;
            }
        }

        private static bool Approximately(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) <= WriteEpsilon && Mathf.Abs(a.g - b.g) <= WriteEpsilon &&
            Mathf.Abs(a.b - b.b) <= WriteEpsilon && Mathf.Abs(a.a - b.a) <= WriteEpsilon;
    }
}
