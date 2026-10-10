using System;
using System.Collections.Generic;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Constants;
using AdaptiveBossArena.Core.Services;
using UnityEngine;

namespace AdaptiveBossArena.Combat
{
    /// <summary>
    /// 执行一次攻击，从前摇到后摇，并处理它触及的一切。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 玩家与首领共用。双方发动的攻击采用相同定义，因此双方应通过同一套代码处理；重复实现会让伤害、
    /// 顿帧和挥空报告逐渐偏离，而学习系统依赖这些信号对双方具有相同含义。
    /// </para>
    /// <para>
    /// 每次攻击只能命中同一目标一次。否则，跨越多个帧的生效窗口会在每一帧造成伤害，
    /// 使攻击的实际伤害取决于帧率。
    /// </para>
    /// </remarks>
    public sealed class AttackExecutor
    {
        /// <summary>
        /// 生成的施法效果的兜底存活时间，单位为秒。远长于最长攻击，因此能自行清理的效果会先结束，
        /// 这里只捕获未被清理的效果。
        /// </summary>
        private const float CastVfxLifetimeSeconds = 4f;

        private readonly AttackHitDetector _detector = new AttackHitDetector();
        private readonly HashSet<int> _alreadyStruck = new HashSet<int>();
        private readonly AttackTimeline _timeline = new AttackTimeline();

        private readonly Transform _origin;
        private readonly CombatantTeam _team;
        private readonly int _instanceId;
        private readonly int _targetLayerMask;
        private readonly ITimeService _time;
        private readonly ICombatEventBus _events;
        private readonly IScreenShake _screenShake;
        private readonly Feel.AttackVisualizer _visualizer;
        private readonly IHazardField _hazardField;

        private bool _landedThisAttack;

        /// <summary>创建绑定到一个战斗参与者的执行器。</summary>
        /// <param name="origin">攻击体积定位所相对的 Transform。</param>
        /// <param name="team">攻击所属的阵营。</param>
        /// <param name="targetLayerMask">此战斗参与者的攻击可以命中的层。</param>
        /// <param name="time">时间服务，用于请求顿帧。</param>
        /// <param name="events">发布已观察事件的总线。</param>
        /// <param name="screenShake">用于命中反馈的可选震动接收器。</param>
        /// <param name="visualizer">
        /// 显示命中体积的可选地面叠加效果。缺少它时攻击不可见，因此只有不存在场景的测试可以省略它。
        /// </param>
        /// <param name="hazardField">
        /// 地面危险区域的可选接收器。首领会提供它，玩家则保留 null，
        /// 这正是只有首领攻击会在地面留下痕迹的原因。
        /// </param>
        /// <exception cref="ArgumentNullException">缺少必要依赖时抛出。</exception>
        public AttackExecutor(
            Transform origin,
            CombatantTeam team,
            int targetLayerMask,
            ITimeService time,
            ICombatEventBus events,
            IScreenShake screenShake = null,
            Feel.AttackVisualizer visualizer = null,
            IHazardField hazardField = null)
        {
            _origin = origin != null ? origin : throw new ArgumentNullException(nameof(origin));
            _team = team;
            _instanceId = origin.GetInstanceID();
            _targetLayerMask = targetLayerMask;
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _screenShake = screenShake;
            _visualizer = visualizer;
            _hazardField = hazardField;
        }

        /// <summary>单帧内检测一个攻击部件的最大采样次数。</summary>
        private const int MaxSweepSamples = 6;

        /// <summary>此身体能够用于攻击的部件；没有骨架时为空。</summary>
        private System.Collections.Generic.IReadOnlyList<StrikeVolume> _strikers = System.Array.Empty<StrikeVolume>();

        /// <summary>各攻击部件在上次推进时的根部和尖端位置，以攻击部件索引存储。</summary>
        private Vector3[] _previousStart = System.Array.Empty<Vector3>();

        private Vector3[] _previousEnd = System.Array.Empty<Vector3>();

        /// <summary>此攻击推进并记录过部件位置之前为 false。</summary>
        private bool _hasPreviousPose;

        /// <summary>此攻击本次推进使用的攻击部件索引。</summary>
        private readonly System.Collections.Generic.List<int> _activeStrikers = new System.Collections.Generic.List<int>(6);

        private readonly Collider[] _sweepBuffer = new Collider[AttackHitDetector.MaxOverlaps];
        private readonly Hurtbox[] _sweepHits = new Hurtbox[AttackHitDetector.MaxOverlaps];
        private readonly Vector3[] _sweepContacts = new Vector3[AttackHitDetector.MaxOverlaps];

        /// <summary>
        /// 向执行器提供身体用于攻击的部件。
        /// </summary>
        /// <remarks>
        /// 也要包含未激活的部件：巨兽的砍刀在狂暴时隐藏，重试时再次显示，
        /// 执行器会在挥击时询问各部件当前是否可用。
        /// </remarks>
        /// <param name="strikers">身体上的全部攻击部件。</param>
        public void SetStrikers(System.Collections.Generic.IReadOnlyList<StrikeVolume> strikers)
        {
            _strikers = strikers ?? System.Array.Empty<StrikeVolume>();
            _previousStart = new Vector3[_strikers.Count];
            _previousEnd = new Vector3[_strikers.Count];
            _hasPreviousPose = false;
        }

        /// <summary>当前执行的攻击；也可为 null。</summary>
        public AttackDefinition CurrentAttack => _timeline.CurrentAttack;

        /// <summary>攻击进行中时为 true。</summary>
        public bool IsRunning => _timeline.IsRunning;

        /// <summary>当前攻击到达的阶段。</summary>
        public AttackPhase Phase => _timeline.Phase;

        /// <summary>
        /// 上次被化解的挥击是否遇到主动招架，而不是计时防御。
        /// </summary>
        /// <remarks>
        /// 攻击者读取它来决定受挫时长。招架是需要投入且可被惩罚的动作，因此换来的空档比弹反更长。
        /// </remarks>
        public bool LastParryWasStrike { get; private set; }

        /// <summary>当前攻击已经过的秒数。</summary>
        public float ElapsedSeconds => _timeline.ElapsedSeconds;

        /// <summary>当前攻击接受后续输入时为 true。</summary>
        public bool IsInComboWindow => _timeline.IsInComboWindow;

        /// <summary>当前攻击已命中至少一个目标时为 true。</summary>
        public bool HasLanded => _landedThisAttack;

        /// <summary>此攻击每次造成伤害时触发。</summary>
        public event Action<DamageResult> HitLanded;

        /// <summary>攻击结束且攻击者恢复行动能力时触发。</summary>
        public event Action Completed;

        /// <summary>进行中的攻击切换阶段时触发。</summary>
        public event Action<AttackPhase> PhaseChanged;

        /// <summary>
        /// 此攻击被精准应对并化解时触发。
        /// </summary>
        /// <remarks>
        /// <b>处理器只能设置标记。</b>此事件在时间轴逐阶段推进循环内部触发，
        /// 因此在处理器中调用 <see cref="Cancel"/> 会在循环仍运行时重入。
        /// 招架应产生的反作用必须在下一次推进时请求并执行——硬直请求已经如此处理，
        /// 因为进入硬直状态会退出攻击状态，而退出会取消挥击。
        /// </remarks>
        public event Action Parried;

        /// <summary>开始一次攻击，替换任何已进行的攻击。</summary>
        /// <param name="attack">需要执行的攻击。</param>
        /// <param name="separationDistance">与对手的距离，会记录在事件中。</param>
        public void Begin(AttackDefinition attack, float separationDistance = 0f)
        {
            if (attack == null)
            {
                return;
            }

            _alreadyStruck.Clear();
            _landedThisAttack = false;
            _hasPreviousPose = false;

            EnsureSubscribed();
            _timeline.Begin(attack);
            SpawnCastVfx(attack);

            _events.Publish(new CombatEvent
            {
                Kind = CombatEventKind.AttackStarted,
                Actor = _team,
                Timestamp = _time.CombatTime,
                DamageType = attack.DamageType,
                Unblockable = attack.Unblockable,
                Position = _origin.position,
                Direction = _origin.forward,
                SeparationDistance = separationDistance
            });
        }

        /// <summary>推进攻击，并在命中窗口开启时处理命中。</summary>
        /// <param name="deltaTime">经过的缩放时间，因此顿帧会冻结攻击进度。</param>
        public void Tick(float deltaTime)
        {
            if (!_timeline.IsRunning)
            {
                return;
            }

            _timeline.Tick(deltaTime);

            // 在推进后处理命中，让开启窗口的帧也能执行检测。先检测会让每次攻击损失一帧的触及距离。
            if (_timeline.Phase == AttackPhase.Active)
            {
                ResolveHits(_timeline.CurrentAttack);
            }

            // 前摇期间也记录位置，因此首个生效帧从前摇最后一帧的刀刃位置开始扫掠，
            // 而不是从恰好位于的位置开始。
            RecordStrikerPoses();
        }

        /// <summary>记住各攻击部件位置，供下次推进时扫掠使用。</summary>
        private void RecordStrikerPoses()
        {
            for (int i = 0; i < _strikers.Count; i++)
            {
                if (_strikers[i] != null)
                {
                    _strikers[i].Segment(out _previousStart[i], out _previousEnd[i]);
                }
            }

            _hasPreviousPose = true;
        }

        /// <summary>
        /// 选择攻击所使用、且当前存在的攻击部件。
        /// </summary>
        /// <remarks>
        /// 没有手持武器时发动的武器攻击——剑消失后的狂暴阶段——改用拳头命中，
        /// 让持械招式组中的每个动作仍有可用于命中的部件。
        /// </remarks>
        /// <returns>找到至少一个部件时为 true。</returns>
        private bool CollectStrikers(StrikerParts parts)
        {
            _activeStrikers.Clear();
            AddAvailable(parts);

            if (_activeStrikers.Count == 0 && (parts & StrikerParts.Weapon) != 0)
            {
                AddAvailable(StrikerParts.RightHand | StrikerParts.LeftHand);
            }

            return _activeStrikers.Count > 0;
        }

        private void AddAvailable(StrikerParts parts)
        {
            for (int i = 0; i < _strikers.Count; i++)
            {
                StrikeVolume striker = _strikers[i];

                if (striker != null && striker.IsAvailable && (parts & striker.Part) != 0)
                {
                    _activeStrikers.Add(i);
                }
            }
        }

        /// <summary>
        /// 检测各攻击部件从上次推进以来扫过的路径，并对触及对象施加打击。
        /// </summary>
        private void SweepHits(AttackDefinition attack)
        {
            int found = 0;

            foreach (int index in _activeStrikers)
            {
                StrikeVolume striker = _strikers[index];
                striker.Segment(out Vector3 start, out Vector3 end);
                striker.MarkLive(Time.time);

                Vector3 fromStart = _hasPreviousPose ? _previousStart[index] : start;
                Vector3 fromEnd = _hasPreviousPose ? _previousEnd[index] : end;

                int samples = StrikeSweep.SampleCount(fromStart, fromEnd, start, end, striker.Radius, MaxSweepSamples);

                // 从上次姿势之后的第一个采样步开始，直到本次姿势；上次姿势已经检测过。
                for (int step = 1; step <= samples; step++)
                {
                    StrikeSweep.Between(
                        fromStart, fromEnd, start, end, step / (float)samples, out Vector3 a, out Vector3 b);

                    int count = Physics.OverlapCapsuleNonAlloc(
                        a, b, striker.Radius, _sweepBuffer, _targetLayerMask, QueryTriggerInteraction.Collide);

                    for (int i = 0; i < count; i++)
                    {
                        var hurtbox = _sweepBuffer[i].GetComponent<Hurtbox>();

                        if (hurtbox != null && hurtbox.IsValid)
                        {
                            found = KeepBestPerTarget(hurtbox, _sweepBuffer[i].ClosestPoint((a + b) * 0.5f), found);
                        }
                    }
                }
            }

            for (int i = 0; i < found; i++)
            {
                if (_alreadyStruck.Add(_sweepHits[i].Owner.GetHashCode()))
                {
                    ApplyTo(attack, _sweepHits[i], _sweepContacts[i]);
                }
            }
        }

        /// <summary>
        /// 记录触及的受击盒，和体积查询一样，每个战斗参与者保留价值最高的一个。
        /// </summary>
        /// <remarks>
        /// 巨兽核心位于身体内，穿过核心的刀刃也会穿过身体。选择物理系统列出的第一个对象，
        /// 会让命中核心变成掷硬币般的随机结果，而不是决策。
        /// </remarks>
        private int KeepBestPerTarget(Hurtbox hurtbox, Vector3 contact, int found)
        {
            for (int i = 0; i < found; i++)
            {
                if (!ReferenceEquals(_sweepHits[i].Owner, hurtbox.Owner))
                {
                    continue;
                }

                if (hurtbox.DamageMultiplier > _sweepHits[i].DamageMultiplier)
                {
                    _sweepHits[i] = hurtbox;
                    _sweepContacts[i] = contact;
                }

                return found;
            }

            if (found >= _sweepHits.Length)
            {
                return found;
            }

            _sweepHits[found] = hurtbox;
            _sweepContacts[found] = contact;

            return found + 1;
        }

        /// <summary>中止攻击，通常因为攻击者进入硬直。</summary>
        public void Cancel() => _timeline.Cancel();

        /// <summary>在攻击者位置生成可选施法效果，并设为子对象以跟随攻击者。</summary>
        /// <remarks>
        /// 这是技能美术资源的接入点。目前所有攻击都为 null，因此没有效果。
        /// 挂到攻击上的预制体会在此出现，设为原点的子对象以随突进移动；如果效果没有自行清理，
        /// 会在宽裕的兜底存活时间后被销毁。
        /// </remarks>
        private void SpawnCastVfx(AttackDefinition attack)
        {
            if (attack.CastVfxPrefab == null)
            {
                return;
            }

            GameObject effect = UnityEngine.Object.Instantiate(
                attack.CastVfxPrefab, _origin.position, _origin.rotation, _origin);
            UnityEngine.Object.Destroy(effect, CastVfxLifetimeSeconds);
        }

        /// <summary>通知打击的生效窗口已经开启，供负责挥击音效的对象使用。</summary>
        private void PublishActive(AttackDefinition attack)
        {
            if (attack == null)
            {
                return;
            }

            _events.Publish(new CombatEvent
            {
                Kind = CombatEventKind.AttackActive,
                Actor = _team,
                Timestamp = _time.CombatTime,
                DamageType = attack.DamageType,
                Unblockable = attack.Unblockable,
                Position = _origin.position,
                Direction = _origin.forward
            });
        }

        /// <summary>检测体积内的全部受击盒，并对尚未命中的对象造成伤害。</summary>
        private void ResolveHits(AttackDefinition attack)
        {
            // 身体具有攻击部件时，使用身体实际参与打击的部件。
            // 创作的攻击体积只决定范围攻击和没有骨架的身体的命中。
            if (attack.Strikers != StrikerParts.None && CollectStrikers(attack.Strikers))
            {
                SweepHits(attack);
                return;
            }

            int count = _detector.Query(attack, _origin, _targetLayerMask);

            for (int i = 0; i < count; i++)
            {
                Hurtbox hurtbox = _detector.Results[i];
                int targetId = hurtbox.Owner.GetHashCode();

                // 在得知结果之前记录目标是有意的。被闪避的攻击不应在下一帧获得再次命中同一目标的机会
                // 。
                if (!_alreadyStruck.Add(targetId))
                {
                    continue;
                }

                ApplyTo(attack, hurtbox);
            }
        }

        /// <summary>将攻击交给一个受击盒，并对目标决定的结果作出反应。</summary>
        private void ApplyTo(AttackDefinition attack, Hurtbox hurtbox, Vector3? contact = null)
        {
            // 若已知，则使用部件实际触及的位置，让火花和鲜血从刀刃处迸出，而不是从身体中心冒出。
            Vector3 contactPoint = contact ?? hurtbox.transform.position;
            Vector3 direction = contactPoint - _origin.position;
            direction.y = 0f;
            direction = direction.sqrMagnitude > Mathf.Epsilon ? direction.normalized : _origin.forward;

            DamageInfo damage = attack
                .BuildDamageInfo(_team, _instanceId)
                .AtPoint(contactPoint, direction);

            DamageResult result = hurtbox.Receive(damage);

            switch (result.Outcome)
            {
                case DamageOutcome.Applied:
                    OnDamageDealt(attack, contactPoint, result);
                    break;

                case DamageOutcome.PerfectDodged:
                case DamageOutcome.Invulnerable:
                    PublishEvaded(attack, contactPoint);
                    break;

                case DamageOutcome.Deflected:
                    LastParryWasStrike = false;
                    OnParried(attack, contactPoint, direction);
                    break;

                case DamageOutcome.Parried:
                    LastParryWasStrike = true;
                    OnParried(attack, contactPoint, direction);
                    break;
            }
        }

        /// <summary>报告此挥击已被招架，让攻击者为此付出代价。</summary>
        /// <remarks>
        /// 这是攻击者得知自己被招架的唯一位置。事件以<em>防守者</em>为执行者，
        /// 遵循 <see cref="CombatEventKind.Parried"/> 中记录的约定，并携带来袭方向，
        /// 让火花沿打击来向反弹，而不是形成无方向的喷散。
        /// </remarks>
        private void OnParried(AttackDefinition attack, Vector3 contactPoint, Vector3 direction)
        {
            _events.Publish(new CombatEvent
            {
                Kind = CombatEventKind.Parried,
                Actor = _team == CombatantTeam.Player ? CombatantTeam.Boss : CombatantTeam.Player,
                Timestamp = _time.CombatTime,
                DamageType = attack.DamageType,
                Unblockable = attack.Unblockable,
                Position = contactPoint,
                Direction = direction
            });

            Parried?.Invoke();
        }

        /// <summary>施加命中反馈并报告成功命中。</summary>
        private void OnDamageDealt(AttackDefinition attack, Vector3 contactPoint, DamageResult result)
        {
            _landedThisAttack = true;

            _time.RequestHitStop(Mathf.Min(attack.HitStopSeconds, GameplayConstants.MaxHitStopSeconds));
            _screenShake?.AddTrauma(attack.CameraTrauma);

            // 在震动之上增加按相同强度缩放的冲击，让重击推动摄像机向前，而轻击几乎不会产生明显变化。
            _screenShake?.Punch(attack.CameraTrauma);

            _events.Publish(new CombatEvent
            {
                Kind = CombatEventKind.AttackLanded,
                Actor = _team,
                Timestamp = _time.CombatTime,
                DamageType = attack.DamageType,
                Position = contactPoint,
                Direction = _origin.forward,
                Magnitude = result.AmountApplied
            });

            HitLanded?.Invoke(result);
        }

        /// <summary>报告已经触及目标但被回避的攻击。</summary>
        private void PublishEvaded(AttackDefinition attack, Vector3 contactPoint) =>
            _events.Publish(new CombatEvent
            {
                Kind = CombatEventKind.AttackEvaded,
                Actor = _team,
                Timestamp = _time.CombatTime,
                DamageType = attack.DamageType,
                Position = contactPoint,
                Direction = _origin.forward
            });

        /// <summary>转发时间轴完成事件；没有命中任何对象时报告挥空。</summary>
        private void OnTimelineCompleted()
        {
            if (!_landedThisAttack)
            {
                // 这个信号让首领察觉自身模式已被识破，也用于计算玩家挥空率。
                // 没有触及任何对象的攻击和成功命中的攻击一样具有信息价值。
                _events.Publish(new CombatEvent
                {
                    Kind = CombatEventKind.AttackWhiffed,
                    Actor = _team,
                    Timestamp = _time.CombatTime,
                    Position = _origin.position,
                    Direction = _origin.forward
                });
            }

            Completed?.Invoke();
        }

        private bool _isSubscribed;

        /// <summary>首次使用时连接到时间轴，且只连接一次。</summary>
        private void EnsureSubscribed()
        {
            if (_isSubscribed)
            {
                return;
            }

            _timeline.Completed += OnTimelineCompleted;
            _timeline.PhaseChanged += OnPhaseChanged;
            _isSubscribed = true;
        }

        /// <summary>
        /// 驱动地面视觉效果，并向订阅者转发阶段。
        /// </summary>
        /// <remarks>
        /// 由阶段转换驱动，而不是轮询，确保预警恰好出现在前摇开始的帧，闪光恰好出现在命中盒开启的帧。
        /// 两者若有偏差，就会让玩家学到并不存在的时机。
        /// </remarks>
        private void OnPhaseChanged(AttackPhase phase)
        {
            if (_visualizer != null)
            {
                AttackDefinition attack = _timeline.CurrentAttack;

                switch (phase)
                {
                    case AttackPhase.Startup:
                        if (attack != null && attack.ShowTelegraph)
                        {
                            _visualizer.ShowTelegraph(attack);
                        }

                        break;

                    case AttackPhase.Active:
                        // 与前摇采用相同规则。每次普通挥击下都闪出明亮形状，包括玩家挥击，
                        // 会看起来像棋盘游戏棋子，并遮住身体实际做出的打击。
                        if (attack != null && attack.ShowTelegraph)
                        {
                            _visualizer.ShowStrike(attack);
                        }

                        break;

                    default:
                        _visualizer.Hide();
                        break;
                }
            }

            if (phase == AttackPhase.Active)
            {
                PublishActive(_timeline.CurrentAttack);
                TrySpawnHazard(_timeline.CurrentAttack);

                // 除逐帧处理外，也在此处理，让进入窗口时至少检测一次。时间轴逐个阶段推进，
                // 正是为了让长帧无法在未开启命中盒的情况下跳过生效窗口——但 Tick 只在推进全部结束后
                // 读取阶段，因此足以同时跨越前摇、生效和后摇的帧会最终停在后摇，从未执行查询。
                // 挥击看似应该命中，却没有任何结果。
                //
                // 普通帧执行两次没有危害：已命中集合按挥击去重，因此第二次对已命中对象不做任何操作，
                // 同时仍可捕捉新进入的对象。
                //
                // 这带来一个约束：伤害现在从时间轴自身的逐阶段推进循环内部施加，
                // 因此伤害路径触及的任何对象都不能同步取消此执行器的时间轴。
                // 目前每处 Cancel 调用都位于状态推进或退出中，
                // 因此没有问题——但若弹反要中止攻击者的挥击，就必须延迟处理，不能当场取消。
                ResolveHits(_timeline.CurrentAttack);
            }

            PhaseChanged?.Invoke(phase);
        }

        /// <summary>会留下危险区域的攻击开始生效时，在攻击者脚下留下地面痕迹。</summary>
        /// <remarks>
        /// 生成在攻击者自身位置的地面高度：首领的砸击和冲击波从它脚下爆发，因此痕迹位于打击位置。
        /// 危险区域接收器为 null（玩家），或者攻击不留下危险区域时，不做任何操作。
        /// </remarks>
        private void TrySpawnHazard(AttackDefinition attack)
        {
            if (_hazardField == null || attack == null || !attack.LeavesHazard)
            {
                return;
            }

            Vector3 center = _origin.position;
            center.y = GameplayConstants.GroundPlaneHeight;

            _hazardField.Spawn(
                center, attack.HazardRadius, attack.HazardDamagePerTick, attack.HazardDurationSeconds);
        }
    }
}
