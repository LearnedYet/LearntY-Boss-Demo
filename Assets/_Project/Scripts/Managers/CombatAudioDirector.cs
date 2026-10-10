using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Events;
using AdaptiveBossArena.Core.Services;
using UnityEngine;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 将观察到的战斗事件转换为声音。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 所有从“发生了什么”到“听到什么”的映射都集中在这里，不散落于战斗代码。战斗系统发布事件，
    /// 完全无需知道音频存在，与抬头显示采用相同的分离方式。
    /// </para>
    /// <para>
    /// 监听战斗事件总线，它已在准确时刻携带准确事件；总线原为学习系统构建，
    /// 但值得学习的事件恰好也是值得听见的事件。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CombatAudioDirector : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Raised on a clean deflect.")]
        private VoidEventChannel _deflectChannel;

        [SerializeField]
        [Tooltip("Raised on a perfect dodge.")]
        private VoidEventChannel _perfectDodgeChannel;

        [SerializeField]
        [Tooltip("Carries the boss phase index, used to trigger the roar.")]
        private IntEventChannel _phaseChannel;

        [SerializeField]
        [Tooltip("Carries normalised player focus, used to chime the moment it fills.")]
        private FloatEventChannel _focusChannel;

        [SerializeField]
        [Tooltip("Carries the drawn weapon's swing cue, so each weapon sounds like itself.")]
        private StringEventChannel _weaponSwingCueChannel;

        /// <summary>玩家当前武器挥动时使用的音效标识。</summary>
        /// <remarks>
        /// 采用缓存而非查询，因为报告挥击的战斗事件不携带武器：事件结构体刻意保持可直接复制，
        /// 使整场战斗事件能够存放在环形缓冲而不分配内存；引用会破坏这一点。
        /// </remarks>
        private string _playerSwingCue = AudioService.Cues.Whoosh;

        [SerializeField]
        [Tooltip("Raised when the boss overbalances, used to sound the unsteady stagger.")]
        private VoidEventChannel _overbalanceChannel;

        private IAudioService _audio;
        private ICombatEventBus _events;

        /// <summary>锁存标记，使专注充满提示每次充满仅播放一次，而非逐帧播放。</summary>
        private bool _focusWasFull;

        private void Start()
        {
            ServiceRegistry.Current.TryGet(out _audio);

            if (ServiceRegistry.Current.TryGet(out _events))
            {
                _events.EventRecorded += OnCombatEvent;
            }

            if (_deflectChannel != null)
            {
                _deflectChannel.Raised += OnDeflect;
            }

            if (_perfectDodgeChannel != null)
            {
                _perfectDodgeChannel.Raised += OnPerfectDodge;
            }

            if (_phaseChannel != null)
            {
                _phaseChannel.Raised += OnPhaseChanged;
            }

            if (_focusChannel != null)
            {
                _focusChannel.Raised += OnFocusChanged;
            }

            if (_weaponSwingCueChannel != null)
            {
                _weaponSwingCueChannel.Raised += OnWeaponSwingCueChanged;
            }

            if (_overbalanceChannel != null)
            {
                _overbalanceChannel.Raised += OnOverbalance;
            }

            _audio?.PlayMusic("music.bed");

            // 中殿的环境声贯穿全程，使攻击间的安静仍像石厅，而非完全无声。
            _audio?.PlayAmbience(AudioService.Cues.Ambience);
        }

        private void OnDestroy()
        {
            if (_events != null)
            {
                _events.EventRecorded -= OnCombatEvent;
            }

            if (_deflectChannel != null)
            {
                _deflectChannel.Raised -= OnDeflect;
            }

            if (_perfectDodgeChannel != null)
            {
                _perfectDodgeChannel.Raised -= OnPerfectDodge;
            }

            if (_phaseChannel != null)
            {
                _phaseChannel.Raised -= OnPhaseChanged;
            }

            if (_focusChannel != null)
            {
                _focusChannel.Raised -= OnFocusChanged;
            }

            if (_weaponSwingCueChannel != null)
            {
                _weaponSwingCueChannel.Raised -= OnWeaponSwingCueChanged;
            }

            if (_overbalanceChannel != null)
            {
                _overbalanceChannel.Raised -= OnOverbalance;
            }
        }

        /// <summary>首领失衡瞬间播放踉跄声，标记破绽。</summary>
        private void OnOverbalance() => _audio?.PlayCue2D(AudioService.Cues.Overbalance);

        /// <summary>专注充满瞬间鸣响一次，让玩家听见强化特殊技已就绪。</summary>
        private void OnFocusChanged(float normalized)
        {
            bool full = normalized >= 0.999f;

            if (full && !_focusWasFull)
            {
                _audio?.PlayCue2D(AudioService.Cues.FocusFull);
            }

            _focusWasFull = full;
        }

        /// <summary>将观察事件映射到音效提示。</summary>
        private void OnCombatEvent(CombatEvent combatEvent)
        {
            if (_audio == null)
            {
                return;
            }

            switch (combatEvent.Kind)
            {
                case CombatEventKind.AttackStarted:
                    // 危险、不可格挡攻击前摇开始时立即播放警示音，作为“不要格挡”的声音提示。
                    // 巨兽其他前摇发出用力挥击的低吼，即使视线转向别处也能听见。骑士前摇则保持安静。
                    if (combatEvent.Unblockable)
                    {
                        _audio.PlayCue(AudioService.Cues.Peril, combatEvent.Position);
                    }
                    else if (combatEvent.Actor == CombatantTeam.Boss)
                    {
                        _audio.PlayCue(AudioService.Cues.BossGrunt, combatEvent.Position);
                    }

                    break;

                case CombatEventKind.AttackActive:
                    // 刀刃移动时才听见挥击，而非在前摇阶段提前一秒播放；玩家曾反馈声音与所见不匹配。
                    _audio.PlayCue(SwingCueFor(combatEvent), combatEvent.Position);
                    break;

                case CombatEventKind.AttackLanded:
                    _audio.PlayCue(ImpactCueFor(combatEvent.DamageType), combatEvent.Position);
                    break;

                case CombatEventKind.Blocked:
                    _audio.PlayCue(AudioService.Cues.Block, combatEvent.Position);
                    break;

                case CombatEventKind.Parried when combatEvent.Actor != CombatantTeam.Player:
                    // 首领招架。玩家自己的弹反已通过弹反通道播放平面声音，因为发生在玩家耳边；
                    // 这次发生在其他位置，所以使用空间音效并加以过滤，否则玩家弹反会发出两次金属声。
                    _audio.PlayCue(AudioService.Cues.Deflect, combatEvent.Position);
                    break;

                case CombatEventKind.PlayerParried:
                    // 比同时触发的弹反鸣响更亮、更重，使两者在交锋中不会被误认。
                    _audio.PlayCue2D(AudioService.Cues.Parry);
                    break;

                case CombatEventKind.GuardRaised:
                    _audio.PlayCue2D(AudioService.Cues.GuardRaise);
                    break;

                case CombatEventKind.PoiseBroken:
                    _audio.PlayCue(AudioService.Cues.PostureBreak, combatEvent.Position);
                    break;

                case CombatEventKind.DodgePerformed:
                    _audio.PlayCue2D(AudioService.Cues.Dash);
                    break;

                case CombatEventKind.HealStarted:
                    _audio.PlayCue2D(AudioService.Cues.Heal);
                    break;

                case CombatEventKind.WeaponDrawn:
                    _audio.PlayCue2D(AudioService.Cues.WeaponDraw);
                    break;

                case CombatEventKind.Riposte:
                    // 处决本身的重音，叠加在实际命中声之上。
                    _audio.PlayCue2D(AudioService.Cues.Execution);
                    break;

                // 下面四类事件从一开始就发布，却没有任何声音响应。
                case CombatEventKind.AttackWhiffed:
                    // 只有破风声而没有接触。若没有它，空挥与被游戏忽略的输入无法区分。
                    _audio.PlayCue(AudioService.Cues.Whiff, combatEvent.Position);
                    break;

                case CombatEventKind.AttackEvaded:
                    // 躲开攻击是小小胜利，也值得通过声音体现。
                    _audio.PlayCue(AudioService.Cues.PerfectDodge, combatEvent.Position);
                    break;

                case CombatEventKind.HealCompleted:
                    _audio.PlayCue2D(AudioService.Cues.HealComplete);
                    break;

                case CombatEventKind.Defeated:
                    // 战斗最重要的时刻，过去却在无声中发生。
                    _audio.PlayCue(
                        combatEvent.Actor == CombatantTeam.Player
                            ? AudioService.Cues.PlayerDeath
                            : AudioService.Cues.BossDeath,
                        combatEvent.Position);
                    break;
            }
        }

        /// <summary>选择前摇发出的声音。</summary>
        /// <remarks>
        /// 危险优先于武器身份：不可格挡攻击必须先表现为威胁，再表现其他特征。
        /// </remarks>
        private string SwingCueFor(in CombatEvent combatEvent)
        {
            // 巨兽挥舞巨剑，声音也应如此：沉重劈斩，而非普通呼啸。
            return combatEvent.Actor == CombatantTeam.Player
                ? _playerSwingCue
                : AudioService.Cues.SwingGreatsword;
        }

        /// <summary>记住玩家当前持有的武器。</summary>
        private void OnWeaponSwingCueChanged(string cueId)
        {
            if (!string.IsNullOrEmpty(cueId))
            {
                _playerSwingCue = cueId;
            }
        }

        /// <summary>更重的攻击使用更低沉、更长的冲击声，让重量可听见。</summary>
        private static string ImpactCueFor(DamageType damageType)
        {
            switch (damageType)
            {
                case DamageType.Heavy:
                case DamageType.BossMelee:
                    return AudioService.Cues.HitHeavy;

                default:
                    return AudioService.Cues.HitLight;
            }
        }

        private void OnDeflect() => _audio?.PlayCue2D(AudioService.Cues.Deflect);

        private void OnPerfectDodge() => _audio?.PlayCue2D(AudioService.Cues.PerfectDodge);

        private void OnPhaseChanged(int phaseIndex)
        {
            // 音乐随每个阶段逐渐增厚，重置时也会退回基础层。
            _audio?.SetMusicIntensity(phaseIndex);

            // 开场阶段不算升级，因此不播放咆哮。
            if (phaseIndex > 0)
            {
                _audio?.PlayCue2D(AudioService.Cues.BossRoar);
            }
        }

        /// <summary>分配事件通道，供场景生成器使用。</summary>
        /// <param name="deflect">精准弹反通道。</param>
        /// <param name="perfectDodge">完美闪避通道。</param>
        /// <param name="phase">首领阶段通道。</param>
        /// <param name="focus">归一化的玩家专注通道。</param>
        /// <param name="overbalance">首领失衡通道。</param>
        public void Bind(
            VoidEventChannel deflect,
            VoidEventChannel perfectDodge,
            IntEventChannel phase,
            FloatEventChannel focus,
            VoidEventChannel overbalance,
            StringEventChannel weaponSwingCue = null)
        {
            _deflectChannel = deflect;
            _perfectDodgeChannel = perfectDodge;
            _phaseChannel = phase;
            _focusChannel = focus;
            _overbalanceChannel = overbalance;
            _weaponSwingCueChannel = weaponSwingCue;
        }
    }
}
