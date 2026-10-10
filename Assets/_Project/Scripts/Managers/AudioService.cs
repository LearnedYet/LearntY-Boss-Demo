using System.Collections.Generic;
using AdaptiveBossArena.Core.Services;
using AdaptiveBossArena.Utilities.Audio;
using UnityEngine;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 使用程序生成的音频片段播放游戏全部声音。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 实现第一阶段设计的 <see cref="IAudioService"/> 契约；此前一直没有实现，
    /// 直到发现无声游玩体验很差。战斗反馈的听觉重要性至少与视觉相当：若接触发生的准确帧没有声音，
    /// 无论屏幕如何震动，命中都会像没有落地。
    /// </para>
    /// <para>
    /// 音源采用对象池且限制发声数量。否则五分之一秒内命中的三段连击会叠加三份相同冲击声，
    /// 产生刺耳声团，而非三次清晰命中。
    /// </para>
    /// <para>
    /// 每次播放轻微随机改变音高。反复相同采样是声音合成感最明显的标志；
    /// 几个百分点的变化便能几乎消除这种印象，而且没有额外成本。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AudioService : MonoBehaviour, IAudioService
    {
        /// <summary>同时播放的效果声数，超过后回收最旧音源。</summary>
        private const int VoiceCount = 12;

        /// <summary>同一音效两次播放之间的最短间隔，单位为秒。</summary>
        private const float SameCueCooldown = 0.035f;

        /// <summary>每次播放采用的音高变化。</summary>
        private const float PitchJitter = 0.08f;

        /// <summary>叠加音乐循环：基础铺底、紧张层与推动节奏的脉冲。</summary>
        private static readonly string[] MusicLayerClips = { "music.layer0", "music.layer1", "music.layer2" };

        /// <summary>
        /// 各强度级别下每层的目标音量，行表示级别，列表示层。
        /// </summary>
        /// <remarks>
        /// 零级只有基础层，每升一级淡入另一层；最高级时基础层稍微压低，让推动节奏的脉冲清晰突出，
        /// 而非混浊叠加。
        /// </remarks>
        private static readonly float[][] IntensityTargets =
        {
            new[] { 1f, 0f, 0f },
            new[] { 1f, 0.85f, 0f },
            new[] { 0.9f, 0.9f, 0.85f }
        };

        /// <summary>音乐层每秒趋近目标音量的速度。</summary>
        private const float MusicFadePerSecond = 0.8f;

        /// <summary>调用者要求无淡变时采用的变化速率。</summary>
        private const float ImmediateMusicFade = 100f;

        /// <summary>世界声音保持满音量的距离，单位为米。</summary>
        /// <remarks>
        /// 大致对应实际交战距离，使普通战斗不会因自身距离衰减而变安静；衰减用于定位声音，而非隐藏声音。
        /// </remarks>
        private const float NearFieldMetres = 4f;

        /// <summary>超过后世界声音基本不可闻的距离，单位为米。</summary>
        private const float ArenaAudibleMetres = 22f;

        /// <summary>响亮效果出现时配乐被压低的程度。</summary>
        private const float DuckDepth = 0.45f;

        /// <summary>配乐恢复前压低效果持续的时间。</summary>
        private const float DuckSeconds = 0.35f;

        /// <summary>触发配乐压低的音效增益阈值。</summary>
        private const float DuckThresholdGain = 0.9f;

        /// <summary>当前配乐压低状态的剩余秒数。</summary>
        private float _duckRemaining;

        /// <summary>
        /// 在各片段生成峰值之上叠加的音效相对响度。
        /// </summary>
        /// <remarks>
        /// 片段峰值决定声音形状，这里决定它在混音中占多大比例。两者分开，
        /// 使平衡可在不重新生成音频的情况下调整；这里也是记录各提示相对重要性的唯一位置。
        /// 未列出的音效采用单位增益。
        /// </remarks>
        private static readonly Dictionary<string, float> CueGains = new Dictionary<string, float>
        {
            // 持续背景声。响亮脚步最容易让混音令人疲劳。
            { Cues.FootstepPlayer, 0.5f },
            { Cues.FootstepBoss, 0.7f },
            { Cues.Whoosh, 0.7f },
            { Cues.SwingBlade, 0.7f },
            { Cues.SwingGreatsword, 0.8f },
            { Cues.SwingEnergy, 0.65f },
            { Cues.Whiff, 0.55f },
            { Cues.GuardRaise, 0.6f },

            // 决定战斗走向的时刻。
            { Cues.Deflect, 1f },
            { Cues.Parry, 1f },
            { Cues.PostureBreak, 1f },
            { Cues.Execution, 1f },
            { Cues.Peril, 1f },
            { Cues.BossRoar, 1f },
            { Cues.PlayerDeath, 1f },
            { Cues.BossDeath, 1f },

            // 界面声音刻意足够轻，可以位于进行中的战斗声之下。
            { Cues.UiClick, 0.45f },
            { Cues.UiHover, 0.3f }
        };

        /// <summary>当前淡变速率，由调用者要求的淡变时长设定。</summary>
        private float _musicFadePerSecond = MusicFadePerSecond;

        /// <summary>配乐是否已开始，确保强度只初始化一次。</summary>
        private bool _hasMusicStarted;

        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, float> _lastPlayedAt = new Dictionary<string, float>();
        private readonly Dictionary<AudioBus, float> _busVolumes = new Dictionary<AudioBus, float>();

        private AudioSource[] _voices;
        private AudioSource[] _musicLayers;
        private float[] _musicTargets;
        private int _nextVoice;

        /// <summary>音效标识保持为常量，防止调用处拼写错误。</summary>
        public static class Cues
        {
            /// <summary>轻攻击命中。</summary>
            public const string HitLight = "hit.light";

            /// <summary>重攻击命中。</summary>
            public const string HitHeavy = "hit.heavy";

            /// <summary>挥击划过空气。</summary>
            public const string Whoosh = "swing.whoosh";

            /// <summary>精准弹反。</summary>
            public const string Deflect = "guard.deflect";

            /// <summary>主动招架，比弹反更明亮、更硬，因为动作更难。</summary>
            public const string Parry = "player.parry";

            /// <summary>较晚格挡。</summary>
            public const string Block = "guard.block";

            /// <summary>防御或姿态崩溃。</summary>
            public const string PostureBreak = "guard.break";

            /// <summary>首领升级到新阶段。</summary>
            public const string BossRoar = "boss.roar";

            /// <summary>完美闪避。</summary>
            public const string PerfectDodge = "dodge.perfect";

            /// <summary>闪避起步。</summary>
            public const string Dash = "dodge.whoosh";

            /// <summary>引导治疗。</summary>
            public const string Heal = "player.heal";

            /// <summary>拔出武器。</summary>
            public const string WeaponDraw = "weapon.draw";

            /// <summary>处决，对崩溃防御的反击。</summary>
            public const string Execution = "player.execution";

            /// <summary>玩家脚步。</summary>
            public const string FootstepPlayer = "step.player";

            /// <summary>首领脚步，更重、更低沉。</summary>
            public const string FootstepBoss = "step.boss";

            /// <summary>专注槽充满。</summary>
            public const string FocusFull = "focus.full";

            /// <summary>地面危险区域爆发。</summary>
            public const string Hazard = "hazard.erupt";

            /// <summary>灼烧地面每次伤害骑士时的嘶声。</summary>
            public const string HazardBurn = "hazard.burn";

            /// <summary>巨兽用力挥击时的低吼，作为前摇的声音提示。</summary>
            public const string BossGrunt = "boss.grunt";

            /// <summary>大教堂本身的声音，战斗之下缓慢的石厅空气声。</summary>
            public const string Ambience = "ambience.nave";

            /// <summary>举起防御。</summary>
            public const string GuardRaise = "guard.raise";

            /// <summary>单次心跳，玩家濒死时脉冲播放。</summary>
            public const string Heartbeat = "player.heartbeat";

            /// <summary>危险、不可格挡攻击前摇的警示音。</summary>
            public const string Peril = "peril.warning";

            /// <summary>首领投入挥击后失衡产生的不稳踉跄。</summary>
            public const string Overbalance = "boss.overbalance";

            /// <summary>玩家倒下；过去战斗结束没有独立声音。</summary>
            public const string PlayerDeath = "player.death";

            /// <summary>首领倒下，更低沉、更长，因为倒下的是更大物体。</summary>
            public const string BossDeath = "boss.death";

            /// <summary>未碰到任何目标的挥击。</summary>
            public const string Whiff = "swing.whiff";

            /// <summary>完整结束而非被打断的治疗。</summary>
            public const string HealComplete = "player.healed";

            /// <summary>普通长剑挥击。</summary>
            public const string SwingBlade = "swing.blade";

            /// <summary>巨剑挥击，缓慢、低沉且有破风感。</summary>
            public const string SwingGreatsword = "swing.greatsword";

            /// <summary>能量刃挥击，轻薄而迅速。</summary>
            public const string SwingEnergy = "swing.energy";

            /// <summary>菜单按钮按下。</summary>
            public const string UiClick = "ui.click";

            /// <summary>菜单按钮高亮。</summary>
            public const string UiHover = "ui.hover";
        }

        /// <summary>
        /// 用一段录制片段替换一个合成音效。
        /// </summary>
        /// <remarks>
        /// 真实音频的接入途径。这里所有声音启动时由代码生成，无需携带资源，也无成本，但无法胜过录音。
        /// 指定音效标识并添加文件，就能替换单个声音而不修改调用者，因此音效库可以逐个替换，
        /// 而不必一次全部替换。
        /// </remarks>
        [System.Serializable]
        private struct CueOverride
        {
            [Tooltip("Cue identifier to replace, as listed in AudioService.Cues.")]
            public string CueId;

            [Tooltip("Clip to play instead of the synthesised one.")]
            public AudioClip Clip;

            [Tooltip("Several takes of the same sound, played in turn so no hit sounds identical to the last.")]
            public AudioClip[] Clips;

            [Tooltip("Base pitch for this cue, one for as recorded. A heavier body can reuse a lighter sound lower.")]
            public float Pitch;
        }

        /// <summary>某音效有多个录制版本时保存其全部版本。</summary>
        private readonly Dictionary<string, AudioClip[]> _takes = new Dictionary<string, AudioClip[]>();

        /// <summary>
        /// 每个录制音效所替换的合成声音，在录音仍然解码时临时代播，确保攻击绝不无声。
        /// </summary>
        private readonly Dictionary<string, AudioClip> _synthesised = new Dictionary<string, AudioClip>();

        /// <summary>每个音效上次播放的版本，确保下次不重复。</summary>
        private readonly Dictionary<string, int> _lastTake = new Dictionary<string, int>();

        /// <summary>音效基础音高，用于降低或升高录制版本的播放音高。</summary>
        private readonly Dictionary<string, float> _basePitch = new Dictionary<string, float>();

        /// <summary>注入的随机流用于版本选择及音高变化；为 null 时轮流播放且不变化。</summary>
        private IRandomProvider _random;

        [SerializeField]
        [Tooltip("Recorded clips that replace individual synthesised cues. Leave empty to use the " +
                 "generated bank.")]
        private CueOverride[] _cueOverrides = new CueOverride[0];

        private void Awake()
        {
            BuildVoices();
            GenerateClips();
            ApplyCueOverrides();

            // 必须与 SettingsData 默认值一致，否则首次打开设置菜单并传递保存数值前，
            // 游戏会使用另一套音量。
            _busVolumes[AudioBus.Master] = SettingsData.DefaultMasterVolume;
            _busVolumes[AudioBus.Music] = SettingsData.DefaultMusicVolume;
            _busVolumes[AudioBus.Effects] = SettingsData.DefaultEffectsVolume;
            _busVolumes[AudioBus.Interface] = SettingsData.DefaultEffectsVolume;

            ServiceRegistry.Current.RegisterOrReplace<IAudioService>(this);
        }

        /// <inheritdoc />
        public void PlayCue(string cueId, Vector3 worldPosition) => Play(cueId, worldPosition, spatial: true);

        /// <inheritdoc />
        public void PlayCue2D(string cueId) => Play(cueId, Vector3.zero, spatial: false);

        /// <inheritdoc />
        public void PlayMusic(string trackId, float fadeSeconds = 1f)
        {
            if (_musicLayers == null)
            {
                return;
            }

            // 分层配乐是游戏唯一音乐，因此空曲目表示淡出至静音。
            if (string.IsNullOrEmpty(trackId))
            {
                for (int i = 0; i < _musicLayers.Length; i++)
                {
                    _musicTargets[i] = 0f;
                }

                return;
            }

            for (int i = 0; i < _musicLayers.Length; i++)
            {
                if (_musicLayers[i] != null && _clips.TryGetValue(MusicLayerClips[i], out AudioClip clip))
                {
                    _musicLayers[i].clip = clip;

                    if (!_musicLayers[i].isPlaying)
                    {
                        _musicLayers[i].Play();
                    }
                }
            }

            // 遵守请求的淡变，而非忽略它；只有尚未播放时才引入基础层。
            // 过去无条件重置会使重试等后续调用在首领仍处于后期阶段时，悄悄将配乐退回开场层。
            _musicFadePerSecond = fadeSeconds > 0f ? 1f / fadeSeconds : ImmediateMusicFade;

            if (!_hasMusicStarted)
            {
                _hasMusicStarted = true;
                SetMusicIntensity(0);
            }
        }

        /// <inheritdoc />
        public void SetMusicIntensity(int level)
        {
            if (_musicTargets == null)
            {
                return;
            }

            int clamped = Mathf.Clamp(level, 0, IntensityTargets.Length - 1);

            for (int i = 0; i < _musicTargets.Length; i++)
            {
                _musicTargets[i] = IntensityTargets[clamped][i];
            }
        }

        /// <inheritdoc />
        public void SetBusVolume(AudioBus bus, float linearVolume) =>
            _busVolumes[bus] = Mathf.Clamp01(linearVolume);

        /// <summary>当前播放中的房间循环环境声。</summary>
        private AudioSource _ambience;

        /// <summary>房间声相对于音乐总线的响度，保持存在感但不压过战斗。</summary>
        private const float AmbienceGain = 0.55f;

        /// <inheritdoc />
        public void PlayAmbience(string cueId)
        {
            if (!_clips.TryGetValue(cueId, out AudioClip clip) || clip == null)
            {
                return;
            }

            if (_ambience == null)
            {
                var ambienceObject = new GameObject("Ambience");
                ambienceObject.transform.SetParent(transform, false);
                _ambience = ambienceObject.AddComponent<AudioSource>();
                _ambience.loop = true;
                _ambience.spatialBlend = 0f;
                _ambience.playOnAwake = false;
            }

            if (_ambience.clip == clip && _ambience.isPlaying)
            {
                return;
            }

            _ambience.clip = clip;
            _ambience.volume = EffectiveVolume(AudioBus.Music) * AmbienceGain;
            _ambience.Play();
        }

        /// <summary>让各音乐层平滑趋近目标，使强度与音量自然变化。</summary>
        private void Update()
        {
            // 房间声与配乐一样遵守音乐音量及主音量设置。
            if (_ambience != null)
            {
                _ambience.volume = EffectiveVolume(AudioBus.Music) * AmbienceGain;

                // 录音解码前开始会一直无声，因此就绪后再启动。
                if (!_ambience.isPlaying && _ambience.clip != null &&
                    _ambience.clip.loadState == AudioDataLoadState.Loaded)
                {
                    _ambience.Play();
                }
            }

            if (_musicLayers == null)
            {
                return;
            }

            _duckRemaining = Mathf.Max(0f, _duckRemaining - Time.unscaledDeltaTime);

            // 在整个压低时段逐渐恢复，而非瞬间弹回，使连续冲击持续压住配乐，直到交锋结束才再次升起。
            float duck = Mathf.Lerp(1f, 1f - DuckDepth, _duckRemaining / DuckSeconds);
            float effectiveMusic = EffectiveVolume(AudioBus.Music) * duck;
            float step = _musicFadePerSecond * Time.unscaledDeltaTime;

            for (int i = 0; i < _musicLayers.Length; i++)
            {
                if (_musicLayers[i] != null)
                {
                    _musicLayers[i].volume =
                        Mathf.MoveTowards(_musicLayers[i].volume, _musicTargets[i] * effectiveMusic, step);
                }
            }
        }

        /// <inheritdoc />
        public float GetBusVolume(AudioBus bus) =>
            _busVolumes.TryGetValue(bus, out float volume) ? volume : 1f;

        /// <summary>
        /// 在下一个空闲音源上播放音效。
        /// </summary>
        /// <remarks>
        /// 逐音效冷却让连击听起来仍像连击。否则多次几乎同帧命中会合成一个巨大瞬态，
        /// 听起来像失真而非多次冲击。
        /// </remarks>
        private void Play(string cueId, Vector3 worldPosition, bool spatial)
        {
            if (!_clips.TryGetValue(cueId, out AudioClip clip) || clip == null)
            {
                return;
            }

            clip = TakeFor(cueId, clip);

            // 仍在解码：这次播放合成声音而非静音，并让它完整结束。
            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                if (clip.loadState == AudioDataLoadState.Unloaded)
                {
                    clip.LoadAudioData();
                }

                if (!_synthesised.TryGetValue(cueId, out clip) || clip == null)
                {
                    return;
                }
            }

            float now = Time.unscaledTime;

            if (_lastPlayedAt.TryGetValue(cueId, out float lastPlayed) &&
                now - lastPlayed < SameCueCooldown)
            {
                return;
            }

            _lastPlayedAt[cueId] = now;

            AudioSource voice = ClaimVoice();

            voice.transform.position = worldPosition;

            // 完全空间化，而非过去的三分之二。每个世界声音留三分之一未声像定位，
            // 会使正确放置的冲击声仍从混音中心泄漏。
            voice.spatialBlend = spatial ? 1f : 0f;
            voice.clip = clip;
            voice.volume = EffectiveVolume(AudioBus.Effects) * GainFor(cueId);
            float basePitch = _basePitch.TryGetValue(cueId, out float pitch) ? pitch : 1f;
            float jitter = _random != null ? _random.NextFloat(-PitchJitter, PitchJitter) : 0f;
            voice.pitch = basePitch * (1f + jitter);

            voice.Play();

            // 响亮瞬间压低配乐，而非与配乐竞争。游戏中没有其他组件协调两者，若无此处理，
            // 音乐只会继续叠加。
            RequestDuck(GainFor(cueId));
        }

        /// <summary>
        /// 选择打断代价最低的音源。
        /// </summary>
        /// <remarks>
        /// 过去严格轮转会按顺序抢占下一音源，不管它是否仍在播放，导致长声音尾部被后续声音切断：
        /// 咆哮持续1.4 秒，姿态崩溃 0.9 秒，而双方脚步就能在更短时间内轮完音源池。优先选空闲音源；
        /// 全部忙碌时，最接近结束的声音被打断损失最小。
        /// </remarks>
        private AudioSource ClaimVoice()
        {
            for (int i = 0; i < _voices.Length; i++)
            {
                int index = (_nextVoice + i) % _voices.Length;

                if (!_voices[index].isPlaying)
                {
                    _nextVoice = (index + 1) % _voices.Length;
                    return _voices[index];
                }
            }

            int bestIndex = 0;
            float leastRemaining = float.MaxValue;

            for (int i = 0; i < _voices.Length; i++)
            {
                AudioSource candidate = _voices[i];

                // 浏览器中录制片段移交后才解码，提前读取长度只会产生警告且返回无用信息。
                // 仍在解码的音源才刚开始，因此最不值得切断。
                float remaining = candidate.clip != null && candidate.clip.loadState == AudioDataLoadState.Loaded
                    ? candidate.clip.length - candidate.time
                    : float.MaxValue;

                if (remaining < leastRemaining)
                {
                    leastRemaining = remaining;
                    bestIndex = i;
                }
            }

            _nextVoice = (bestIndex + 1) % _voices.Length;
            return _voices[bestIndex];
        }

        /// <summary>
        /// 用录制片段替换合成声音。
        /// </summary>
        /// <remarks>
        /// 在生成后执行，确保覆盖优先；找不到匹配音效标识时警告而非失败，
        /// 否则标识拼写错误会造成声音悄悄从不播放，这是最难发现的音频问题。
        /// </remarks>
        private void ApplyCueOverrides()
        {
            if (_cueOverrides == null)
            {
                return;
            }

            foreach (CueOverride replacement in _cueOverrides)
            {
                AudioClip first = replacement.Clip;

                if (first == null && replacement.Clips != null)
                {
                    first = System.Array.Find(replacement.Clips, clip => clip != null);
                }

                if (first == null || string.IsNullOrWhiteSpace(replacement.CueId))
                {
                    continue;
                }

                if (!_clips.ContainsKey(replacement.CueId) && replacement.CueId != Cues.Ambience)
                {
                    Debug.LogWarning(
                        $"[Adaptive Boss Arena] Audio override names '{replacement.CueId}', which is " +
                        "not a known cue. It will never play. Check the identifiers in " +
                        "AudioService.Cues.",
                        this);
                    continue;
                }

                if (_clips.TryGetValue(replacement.CueId, out AudioClip generated) && generated != null)
                {
                    _synthesised[replacement.CueId] = generated;
                }

                _clips[replacement.CueId] = first;

                // 浏览器只在请求后解码录音，解码前播放的片段无声。竞技场加载时请求全部版本，
                // 让它们在入场阶段而非第一次攻击时完成解码。
                first.LoadAudioData();

                if (replacement.Clips != null)
                {
                    foreach (AudioClip take in replacement.Clips)
                    {
                        if (take != null)
                        {
                            take.LoadAudioData();
                        }
                    }
                }

                if (replacement.Clips != null && replacement.Clips.Length > 1)
                {
                    _takes[replacement.CueId] = System.Array.FindAll(replacement.Clips, clip => clip != null);
                }

                if (replacement.Pitch > 0f)
                {
                    _basePitch[replacement.CueId] = replacement.Pitch;
                }
            }
        }

        /// <summary>
        /// 音效应播放的版本；存在多个时不能与上次相同。
        /// </summary>
        /// <remarks>
        /// 连续两次相同命中录音会让声音像采样，而不像实际攻击。与其他概率机制一样从注入随机流选择，
        /// 且绝不连续重复。
        /// </remarks>
        private AudioClip TakeFor(string cueId, AudioClip fallback)
        {
            if (!_takes.TryGetValue(cueId, out AudioClip[] takes) || takes.Length < 2)
            {
                return fallback;
            }

            _lastTake.TryGetValue(cueId, out int last);
            _random ??= ServiceRegistry.Current != null && ServiceRegistry.Current.TryGet(out IRandomProvider random) ? random : null;

            int next = _random != null
                ? (last + 1 + Mathf.Min(takes.Length - 2, (int)(_random.NextFloat01() * (takes.Length - 1)))) % takes.Length
                : (last + 1) % takes.Length;

            _lastTake[cueId] = next;
            return takes[next];
        }

        /// <summary>音效相对响度，未列出时采用单位增益。</summary>
        private float GainFor(string cueId) =>
            CueGains.TryGetValue(cueId, out float gain) ? gain : 1f;

        /// <summary>声音足够响亮时进一步压低当前配乐。</summary>
        private void RequestDuck(float gain)
        {
            if (gain < DuckThresholdGain)
            {
                return;
            }

            _duckRemaining = DuckSeconds;
        }

        /// <summary>创建池化音源与音乐音源。</summary>
        private void BuildVoices()
        {
            _voices = new AudioSource[VoiceCount];

            for (int i = 0; i < VoiceCount; i++)
            {
                var voiceObject = new GameObject($"Voice_{i:D2}");
                voiceObject.transform.SetParent(transform, false);

                AudioSource source = voiceObject.AddComponent<AudioSource>();
                source.playOnAwake = false;

                // 采用对数衰减，并与竞技场尺度匹配。过去从一米到四十米线性衰减，而地面只有十六米，
                // 导致五米外声音仍有百分之九十、竞技场对侧仍有百分之七十五，完全没有远近感。
                // 现在普通交战距离内保持满音量，之后真正衰减。
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = NearFieldMetres;
                source.maxDistance = ArenaAudibleMetres;

                // 这里运动速度不足以让多普勒有益，反而会在脚步与挥击上产生不需要的音高摆动，
                // 再叠加已有的刻意随机变化。
                source.dopplerLevel = 0f;

                // 效果刻意忽略时间缩放，使冲击声在它导致的顿帧期间正常响起，而非降调成低吟。
                source.ignoreListenerPause = true;

                // 效果刻意忽略时间缩放，使冲击声在它导致的顿帧期间正常响起，而非降调成低吟。
                source.ignoreListenerPause = true;

                _voices[i] = source;
            }

            _musicLayers = new AudioSource[MusicLayerClips.Length];
            _musicTargets = new float[MusicLayerClips.Length];

            for (int i = 0; i < MusicLayerClips.Length; i++)
            {
                var layerObject = new GameObject($"Music_{i}");
                layerObject.transform.SetParent(transform, false);

                AudioSource source = layerObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 0f;
                source.volume = 0f;

                _musicLayers[i] = source;
            }
        }

        /// <summary>合成游戏使用的全部音频片段。</summary>
        private void GenerateClips()
        {
            // 冲击采用分层而非滤波噪声：尖裂声表现接触，降调主体表现背后重量。峰值负责混音；
            // 重击比轻击响，是因为明确设定，而非滤波器偶然造成。
            _clips[Cues.HitLight] =
                ToneGenerator.CreateWeightedImpact("hit.light", 150f, 2400f, 0.22f, 0.62f, 0.4f, seed: 3);

            _clips[Cues.HitHeavy] =
                ToneGenerator.CreateWeightedImpact("hit.heavy", 68f, 1000f, 0.42f, 0.92f, 0.6f, seed: 5);

            _clips[Cues.Block] =
                ToneGenerator.CreateWeightedImpact("guard.block", 115f, 700f, 0.26f, 0.58f, 0.35f, seed: 11);

            // 逐武器挥击。同一动作组有三种声音轮廓：巨剑缓慢、破风感强，长剑中性，能量刃轻薄、迅速。
            _clips[Cues.Whoosh] = ToneGenerator.CreateWhoosh("swing.whoosh");
            _clips[Cues.SwingBlade] = ToneGenerator.CreateWhoosh("swing.blade", 0.28f, 7, 0.42f, 1f);
            _clips[Cues.SwingGreatsword] =
                ToneGenerator.CreateWhoosh("swing.greatsword", 0.46f, 13, 0.5f, 0.45f);
            _clips[Cues.SwingEnergy] =
                ToneGenerator.CreateWhoosh("swing.energy", 0.2f, 17, 0.38f, 2.1f);

            // 弹反刻意采用游戏最高、最亮的声音，因为这一刻玩家最需要知道自己准确做对了。
            _clips[Cues.Deflect] = ToneGenerator.CreateMetallicRing("guard.deflect", 1180f, 0.45f, 0.85f);
            _clips[Cues.Parry] = ToneGenerator.CreateMetallicRing("player.parry", 1720f, 0.6f, 1f);
            _clips[Cues.PostureBreak] = ToneGenerator.CreateMetallicRing("guard.break", 320f, 0.9f, 0.9f);
            _clips[Cues.PerfectDodge] = ToneGenerator.CreateMetallicRing("dodge.perfect", 1560f, 0.3f, 0.72f);

            _clips[Cues.BossRoar] = ToneGenerator.CreateRoar("boss.roar", peak: 1f);

            // 战斗的两种结局。过去两者完全无声，遭遇战最重要的瞬间在沉默中经过。首领终结声更低、更长，
            // 因为倒下的物体更大。
            _clips[Cues.PlayerDeath] = ToneGenerator.CreateDeathKnell("player.death", 104f, 1.9f, 0.95f);
            _clips[Cues.BossDeath] = ToneGenerator.CreateDeathKnell("boss.death", 68f, 2.6f, 1f);

            // 比挥击更短、更多气息的破风声，使闪避表现为玩家移动，而非攻击。
            _clips[Cues.Dash] = ToneGenerator.CreateWhoosh("dodge.whoosh", 0.2f, seed: 21);

            // 治疗声温暖上扬；专注充满瞬间播放短亮提示，治疗真正完成而非被打断时再播放较柔和提示。
            _clips[Cues.Heal] = ToneGenerator.CreateShimmer("player.heal", 520f, 0.7f, 0.55f);
            _clips[Cues.HealComplete] = ToneGenerator.CreateShimmer("player.healed", 700f, 0.5f, 0.5f);
            _clips[Cues.FocusFull] = ToneGenerator.CreateShimmer("focus.full", 1040f, 0.4f, 0.68f);

            // 拔出武器时短促高亮的金属出鞘声。
            _clips[Cues.WeaponDraw] = ToneGenerator.CreateMetallicRing("weapon.draw", 1500f, 0.2f, 0.5f);

            // 处决是玩家能施加的最重、最低沉攻击，作为防御崩溃的回报，冲击应强于其他全部攻击。
            _clips[Cues.Execution] =
                ToneGenerator.CreateWeightedImpact("player.execution", 52f, 620f, 0.6f, 1f, 0.8f, seed: 29);

            // 脚步：玩家轻拍，首领更重、更低沉。两者都位于混音后方，因为不断播放，绝不能与命中竞争。
            _clips[Cues.FootstepPlayer] =
                ToneGenerator.CreateImpact("step.player", 0.08f, 520f, seed: 31, peak: 0.26f);

            _clips[Cues.FootstepBoss] =
                ToneGenerator.CreateWeightedImpact("step.boss", 58f, 300f, 0.22f, 0.44f, 0.3f, seed: 37);

            // 地面危险区域爆发时的低沉宽频轰鸣。
            _clips[Cues.Hazard] =
                ToneGenerator.CreateWeightedImpact("hazard.erupt", 44f, 380f, 0.62f, 0.8f, 0.75f, seed: 41);

            // 灼烧地面烧伤骑士时的短亮嘶声；高频嘈杂以免与攻击混淆，保持短促，
            // 因为骑士停在火中时每次伤害都会重复。
            _clips[Cues.HazardBurn] =
                ToneGenerator.CreateImpact("hazard.burn", 0.2f, 3200f, seed: 53, peak: 0.3f);

            // 巨兽前摇的低短用力声，直到被录音替换。
            _clips[Cues.BossGrunt] =
                ToneGenerator.CreateWeightedImpact("boss.grunt", 70f, 260f, 0.28f, 0.5f, 0.35f, seed: 59);

            // 举起防御时短促柔和的移动声。
            _clips[Cues.GuardRaise] =
                ToneGenerator.CreateImpact("guard.raise", 0.09f, 900f, seed: 47, peak: 0.34f);

            // 心跳是旧问题最明显的例子：85 Hz 时滤波器只留下满幅的百分之四，
            // 因此游戏的低生命警告实际上无声。
            _clips[Cues.Heartbeat] =
                ToneGenerator.CreateWeightedImpact("player.heartbeat", 46f, 140f, 0.3f, 0.7f, 0.25f, seed: 43);

            // 危险攻击警示：快速颤音之上的紧张三全音刺声。
            _clips[Cues.Peril] = ToneGenerator.CreatePerilWarning("peril.warning", peak: 0.9f);

            // 首领失衡：不稳的低沉踉跄声，倾倒时音高下沉。
            _clips[Cues.Overbalance] = ToneGenerator.CreateStumble("boss.overbalance", peak: 0.78f);

            // 未击中任何目标的挥击：只有空气声，完全无接触。虽然轻，缺少它却会让空挥像游戏漏掉了输入，
            // 而非攻击漏掉了目标。
            _clips[Cues.Whiff] = ToneGenerator.CreateWhoosh("swing.whiff", 0.34f, 53, 0.3f, 0.7f);

            // 菜单反馈使用早已存在、却未被任何内容使用的界面总线。
            _clips[Cues.UiClick] = ToneGenerator.CreateMetallicRing("ui.click", 1320f, 0.12f, 0.4f);
            _clips[Cues.UiHover] = ToneGenerator.CreateMetallicRing("ui.hover", 1760f, 0.07f, 0.22f);

            // 三个和声相关的叠加循环，避免不协和：低音铺底，高五度的紧张层，以及高八度、
            // 供最终阶段使用的节奏脉冲。
            _clips[MusicLayerClips[0]] = ToneGenerator.CreateDrone(MusicLayerClips[0], 55f);
            _clips[MusicLayerClips[1]] = ToneGenerator.CreateDrone(MusicLayerClips[1], 82.5f);
            _clips[MusicLayerClips[2]] = ToneGenerator.CreatePulse(MusicLayerClips[2], 110f, 120f, 8);
        }

        /// <summary>将某总线音量与主音量结合。</summary>
        private float EffectiveVolume(AudioBus bus) =>
            GetBusVolume(bus) * GetBusVolume(AudioBus.Master);
    }
}
