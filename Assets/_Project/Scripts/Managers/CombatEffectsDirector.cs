using AdaptiveBossArena.Combat.Feel;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Events;
using AdaptiveBossArena.Core.Services;
using UnityEngine;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 将观察到的战斗事件转换为火花、闪光和拖尾。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 它是 <see cref="CombatAudioDirector"/> 的视觉对应部分，刻意采用相同结构：战斗系统发布事件，
    /// 不了解呈现方式。添加新效果无需修改战斗代码。
    /// </para>
    /// <para>
    /// 注意它<em>不会</em>做的事：绝不向战斗回传任何信息。它是事件总线的纯消费者；在学习系统统计之外，
    /// 总线只适合承担这种职责。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CombatEffectsDirector : MonoBehaviour
    {
        /// <summary>
        /// 爆发效果在事件报告位置上方的偏移高度。
        /// </summary>
        /// <remarks>
        /// 战斗事件位于角色变换原点，也就是地面。此处发出的火花有一半被地面吞没，看起来像尘土而非命中。
        /// 双方胸口附近的高度更接近实际击中位置。
        /// </remarks>
        private const float ImpactHeight = 1.1f;

        /// <summary>阶段切换冲击波的爆发高度，大致位于首领中心。</summary>
        private const float ShockwaveHeight = 1.2f;

        /// <summary>闪避时增大视野角的度数，制造速度爆发感。</summary>
        private const float DashFovWiden = 6f;

        /// <summary>精准弹反时缩窄视野角，使注意力迅速聚焦招架。</summary>
        private const float DeflectFovNarrow = 3.5f;

        /// <summary>完美闪避时缩窄视野角，与慢动作相呼应。</summary>
        private const float PerfectDodgeFovNarrow = 5f;

        /// <summary>失衡时的屏幕震动强度，以轻微冲击表现踉跄的实体感。</summary>
        private const float OverbalanceTrauma = 0.2f;

        /// <summary>
        /// 身体撞墙时的震动，介于重击与砸击之间：墙壁冲击更硬，但它不是攻击，不能表现成攻击。
        /// </summary>
        private const float WallImpactTrauma = 0.32f;

        /// <summary>
        /// 防御崩溃后瞬间的世界速度。
        /// </summary>
        /// <remarks>
        /// 交锋中最重要的时刻以全速发生，刚开始就结束。以三分之一速度持续三分之一秒，既能看清踉跄，
        /// 也不会超出提供的破绽窗口。
        /// </remarks>
        private const float PoiseBreakSlowMotionScale = 0.35f;

        private const float PoiseBreakSlowMotionSeconds = 0.35f;

        /// <summary>防御崩溃时的震动和相机冲击，比任何单次攻击更重。</summary>
        private const float PoiseBreakTrauma = 0.38f;

        /// <summary>轻击留下的飞溅大致尺寸，单位为米。</summary>
        private const float LightBloodSize = 0.8f;

        /// <summary>重击留下的飞溅大致尺寸，单位为米。</summary>
        private const float HeavyBloodSize = 1.4f;

        /// <summary>飞溅旋转和尺寸的种子，仅用于表现，不使用玩法随机源。</summary>
        private const uint BloodSeed = 0xB100Du;

        [SerializeField]
        [Tooltip("Raised on a perfect dodge.")]
        private VoidEventChannel _perfectDodgeChannel;

        [SerializeField]
        [Tooltip("Carries the boss phase index, used to erupt a shockwave on each escalation.")]
        private IntEventChannel _bossPhaseChannel;

        [SerializeField]
        [Tooltip("Raised when the boss overbalances, used to kick up dust at its feet.")]
        private VoidEventChannel _overbalanceChannel;

        [SerializeField]
        [Tooltip("Material impact sparks are drawn with. Assigned by the scene generator, so its shader ships.")]
        private Material _impactMaterial;

        [SerializeField]
        [Tooltip("Alpha-blended material blood and dust are drawn with. Assigned by the scene generator.")]
        private Material _matterMaterial;

        [SerializeField]
        [Tooltip("Splatter left on the floor under a landed blow. Assigned by the scene generator.")]
        private Material _bloodMaterial;

        private BloodDecalPool _blood;

        private ImpactBurstPool _bursts;
        private ICombatEventBus _events;
        private IScreenShake _shake;
        private ITimeService _time;
        private WeaponTrail _playerTrail;
        private WeaponTrail _bossTrail;
        private Transform _bossTransform;

        private void Awake()
        {
            _bursts = gameObject.AddComponent<ImpactBurstPool>();
            _bursts.Construct(_impactMaterial, _matterMaterial);

            if (_bloodMaterial != null)
            {
                _blood = new GameObject("BloodDecals").AddComponent<BloodDecalPool>();
                _blood.transform.SetParent(transform, worldPositionStays: false);
                _blood.Construct(_bloodMaterial, new XorShiftRandomProvider(BloodSeed));
            }
        }

        private void Start()
        {
            ServiceRegistry.Current.TryGet(out _shake);
            ServiceRegistry.Current.TryGet(out _time);

            if (ServiceRegistry.Current.TryGet(out _events))
            {
                _events.EventRecorded += OnCombatEvent;
            }

            if (_perfectDodgeChannel != null)
            {
                _perfectDodgeChannel.Raised += OnPerfectDodge;
            }

            if (_bossPhaseChannel != null)
            {
                _bossPhaseChannel.Raised += OnBossPhaseChanged;
            }

            if (_overbalanceChannel != null)
            {
                _overbalanceChannel.Raised += OnOverbalance;
            }

            ResolveTrails();
        }

        private void OnDestroy()
        {
            if (_events != null)
            {
                _events.EventRecorded -= OnCombatEvent;
            }

            if (_perfectDodgeChannel != null)
            {
                _perfectDodgeChannel.Raised -= OnPerfectDodge;
            }

            if (_bossPhaseChannel != null)
            {
                _bossPhaseChannel.Raised -= OnBossPhaseChanged;
            }

            if (_overbalanceChannel != null)
            {
                _overbalanceChannel.Raised -= OnOverbalance;
            }
        }

        /// <summary>首领踉跄时在脚下扬起尘土，标记识破其判断换来的破绽。</summary>
        /// <remarks>
        /// 贴近地面的暗淡、分散爆发；格挡风格更像尘土而非火花，再叠加轻微震动，
        /// 使过度投入既能被看到也能被感到。效果放在首领位置，因为落空的是首领攻击，而非玩家攻击。
        /// </remarks>
        private void OnOverbalance()
        {
            if (_bossTransform != null)
            {
                _bursts.Play(_bossTransform.position + Vector3.up * 0.25f, Vector3.up, ImpactFlavour.Stone);
            }

            _shake?.AddTrauma(OverbalanceTrauma);
        }

        /// <summary>将观察事件映射到爆发效果或拖尾。</summary>
        private void OnCombatEvent(CombatEvent combatEvent)
        {
            switch (combatEvent.Kind)
            {
                case CombatEventKind.AttackStarted:
                    TrailFor(combatEvent.Actor)?.Begin();
                    break;

                case CombatEventKind.AttackLanded:
                    Burst(combatEvent.Position, combatEvent.Direction, FlavourFor(combatEvent.DamageType));
                    _blood?.Place(combatEvent.Position, combatEvent.Direction,
                        FlavourFor(combatEvent.DamageType) == ImpactFlavour.Heavy ? HeavyBloodSize : LightBloodSize);
                    break;

                case CombatEventKind.Deflected:
                    // 由总线驱动，而非弹反事件通道；后者在同一时刻触发，但不携带位置。
                    // 同时监听两者会在战斗最重要的单个瞬间触发两次爆发。
                    Burst(combatEvent.Position, -combatEvent.Direction, ImpactFlavour.Deflect);
                    _shake?.PunchFov(-DeflectFovNarrow);
                    break;

                case CombatEventKind.DodgePerformed:
                    // 闪避时整台相机向外舒展，这让翻滚呈现为速度爆发，而非瞬间传送。
                    _shake?.PunchFov(DashFovWiden);
                    break;

                case CombatEventKind.Blocked:
                    Burst(combatEvent.Position, -combatEvent.Direction, ImpactFlavour.Block);
                    break;

                case CombatEventKind.Parried when combatEvent.Actor != CombatantTeam.Player:
                    // 仅处理玩家之外的角色招架。玩家自身弹反也会发布 Deflected，已由上面的分支处理；
                    // 两者都触发会让战斗最重要时刻出现双倍火花，与上方注释防范的是同一种错误。
                    Burst(combatEvent.Position, -combatEvent.Direction, ImpactFlavour.Deflect);
                    break;

                case CombatEventKind.WallImpact:
                    // 在身体撞石的位置，火花从墙面向战斗区域弹回。
                    Burst(combatEvent.Position, -combatEvent.Direction, ImpactFlavour.Stone);
                    _shake?.AddTrauma(WallImpactTrauma);
                    break;

                case CombatEventKind.PoiseBroken:
                    Burst(combatEvent.Position, Vector3.up, ImpactFlavour.PostureBreak);
                    _time?.RequestSlowMotion(PoiseBreakSlowMotionScale, PoiseBreakSlowMotionSeconds);
                    _shake?.AddTrauma(PoiseBreakTrauma);
                    _shake?.Punch(PoiseBreakTrauma);
                    break;

                case CombatEventKind.Riposte:
                    TrailFor(combatEvent.Actor)?.Begin();
                    Burst(combatEvent.Position, Vector3.up, ImpactFlavour.PostureBreak);
                    break;
            }
        }

        /// <summary>播放爆发效果，并从地面抬升到实际命中位置。</summary>
        private void Burst(Vector3 position, Vector3 direction, ImpactFlavour flavour) =>
            _bursts.Play(position + Vector3.up * ImpactHeight, direction, flavour);

        /// <summary>更重的攻击产生更大、更慢的爆发，使重量既可见也可闻。</summary>
        private static ImpactFlavour FlavourFor(DamageType damageType)
        {
            switch (damageType)
            {
                case DamageType.Heavy:
                case DamageType.BossMelee:
                    return ImpactFlavour.Heavy;

                default:
                    return ImpactFlavour.Light;
            }
        }

        /// <summary>正在挥动哪一方的武器。</summary>
        private WeaponTrail TrailFor(CombatantTeam team) =>
            team == CombatantTeam.Player ? _playerTrail : _bossTrail;

        /// <summary>
        /// 查找双方的拖尾。
        /// </summary>
        /// <remarks>
        /// 通过场景搜索而非注入解析，因为双方由场景生成器从预制体生成，没有传递引用的接缝。
        /// 搜索仅在启动时执行一次。
        /// </remarks>
        private void ResolveTrails()
        {
            WeaponTrail[] trails = FindObjectsByType<WeaponTrail>(FindObjectsSortMode.None);

            foreach (WeaponTrail trail in trails)
            {
                var combatant = trail.GetComponentInParent<IDamageable>();

                if (combatant == null)
                {
                    continue;
                }

                if (combatant.Team == CombatantTeam.Player)
                {
                    _playerTrail = trail;
                }
                else
                {
                    _bossTrail = trail;
                    _bossTransform = (combatant as Component)?.transform;
                }
            }
        }

        /// <summary>每次阶段升级时从首领位置爆发冲击波。</summary>
        /// <remarks>
        /// 阶段通道在开场和重置时也会触发，因此只有大于零的索引才触发爆发；首领生命只会下降，
        /// 所以这是真正的升级。
        /// </remarks>
        private void OnBossPhaseChanged(int phaseIndex)
        {
            if (phaseIndex <= 0 || _bossTransform == null)
            {
                return;
            }

            _bursts.Play(
                _bossTransform.position + Vector3.up * ShockwaveHeight, Vector3.up, ImpactFlavour.PostureBreak);
        }

        private void OnPerfectDodge()
        {
            // 紧凑放大视野以配合慢动作，将画面聚焦于刚刚识破判断所换来的破绽。
            _shake?.PunchFov(-PerfectDodgeFovNarrow);

            // 放在玩家位置而非命中点，因为没有击中任何目标；爆发标记玩家躲开攻击时的位置。
            if (_playerTrail != null)
            {
                // 与其他效果不同，此处不再抬高：拖尾已经位于胸口高度，
                // 这是本类唯一已经处于正确高度的位置。
                _bursts.Play(_playerTrail.transform.position, Vector3.up, ImpactFlavour.Deflect);
            }
        }

        /// <summary>分配事件通道，供场景生成器使用。</summary>
        /// <param name="perfectDodge">完美闪避通道。</param>
        /// <param name="bossPhase">首领阶段索引通道。</param>
        /// <param name="overbalance">首领失衡通道。</param>
        /// <param name="impactMaterial">绘制命中火花的材质。</param>
        public void Bind(
            VoidEventChannel perfectDodge,
            IntEventChannel bossPhase,
            VoidEventChannel overbalance,
            Material impactMaterial,
            Material matterMaterial = null,
            Material bloodMaterial = null)
        {
            _perfectDodgeChannel = perfectDodge;
            _bossPhaseChannel = bossPhase;
            _overbalanceChannel = overbalance;
            _impactMaterial = impactMaterial;
            _matterMaterial = matterMaterial;
            _bloodMaterial = bloodMaterial;
        }

        /// <summary>绘制地面飞溅的材质。</summary>
        public Material BloodMaterial => _bloodMaterial;

        /// <summary>绘制命中火花的材质。</summary>
        public Material ImpactMaterial => _impactMaterial;

        /// <summary>绘制血液和尘土的材质。</summary>
        public Material MatterMaterial => _matterMaterial;
    }
}
