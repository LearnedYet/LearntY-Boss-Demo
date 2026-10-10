using System;
using System.Text;
using AdaptiveBossArena.AI;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Services;
using AdaptiveBossArena.Learning;
using AdaptiveBossArena.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 展示首领当前对玩家的判断。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 调校此遭遇战几乎必备的工具。适应本身不可见：首领渐进改变行为，却不会说明原因，
    /// 因此仅靠游玩无法分辨阈值太高还是太低。此面板是确认首领是否得出预期结论的唯一途径。
    /// </para>
    /// <para>
    /// 刻意采用即时模式 GUI。无需预制体、画布或布局工作，且正式发布构建会完全移除，因此外观不必精致。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AdaptationDebugOverlay : MonoBehaviour
    {
        private const float PanelWidth = 420f;
        private const float PanelMargin = 12f;
        private const int BarSegments = 20;

        /// <summary>事件尾部列表展示的最近战斗事件数量。</summary>
        /// <remarks>
        /// 足够覆盖完整连段及应对，又足够短，便于在战斗进行时一眼读懂。
        /// </remarks>
        private const int EventTailLength = 10;

        [SerializeField]
        [Tooltip("Key that shows and hides the panel.")]
        private Key _toggleKey = Key.F1;

        [SerializeField]
        [Tooltip("Whether the panel starts visible.")]
        private bool _visibleOnStart;

        [SerializeField]
        [Tooltip("Boss to inspect. Found automatically when left empty.")]
        private BossController _boss;

        private readonly StringBuilder _builder = new StringBuilder(1024);
        private readonly StringBuilder _countsBuilder = new StringBuilder(256);
        private bool _isVisible;
        private bool _forced;
        private GUIStyle _style;

        private PlayerController _player;
        private ITimeService _time;
        private ICombatEventBus _events;

        /// <summary>供尾部列表使用的最近事件，按从旧到新排序。</summary>
        private readonly CombatEvent[] _recentEvents = new CombatEvent[EventTailLength];
        private int _recentCount;

        /// <summary>所有已观察事件的累计计数，以 <see cref="CombatEventKind"/> 索引。</summary>
        private readonly int[] _eventCounts =
            new int[Enum.GetValues(typeof(CombatEventKind)).Length];

        [Header("Console Diagnostics")]
        [SerializeField]
        [Tooltip("Writes a periodic vitals line to the log. In a browser this reaches the developer " +
                 "console, which is the only way to read a shipped build's state remotely.")]
        private bool _logHeartbeat = true;

        [SerializeField]
        [Range(1f, 10f)]
        [Tooltip("Seconds between heartbeat lines.")]
        private float _heartbeatSeconds = 2f;

        [SerializeField]
        [Range(10f, 600f)]
        [Tooltip("Stops the heartbeat after this long, so a left-open tab does not fill the console.")]
        private float _heartbeatStopsAfterSeconds = 180f;

        private float _nextHeartbeatAt;

        /// <summary>
        /// 训练模式下强制显示面板，不受切换键影响。
        /// </summary>
        /// <remarks>
        /// 训练是玩家主动选择的练习模式，而了解首领正在推断什么正是练习的意义，
        /// 因此强制面板在玩家构建中也会渲染，开发者切换入口则仍从正式发布中移除。
        /// </remarks>
        /// <param name="on">是否强制显示面板。</param>
        public void SetForcedVisible(bool on) => _forced = on;

        private void Start()
        {
            _isVisible = _visibleOnStart;

            if (_boss == null)
            {
                _boss = FindAnyObjectByType<BossController>();
            }

            _player = FindAnyObjectByType<PlayerController>();

            ServiceRegistry.Current.TryGet(out _time);

            // 事件尾部列表是关键诊断工具：它可以区分“从未发动攻击”、
            // “发动了攻击但全部落空”和“攻击命中但界面没有变化”。这几种情况从外部看完全相同，
            // 仅阅读代码无法区分。
            if (ServiceRegistry.Current.TryGet(out _events))
            {
                _events.EventRecorded += OnCombatEvent;
            }
        }

        private void OnDestroy()
        {
            if (_events != null)
            {
                _events.EventRecorded -= OnCombatEvent;
            }
        }

        /// <summary>保留最近事件，并移出最旧事件。</summary>
        private void OnCombatEvent(CombatEvent combatEvent)
        {
            var kind = (int)combatEvent.Kind;

            if (kind >= 0 && kind < _eventCounts.Length)
            {
                _eventCounts[kind]++;
            }

            if (_recentCount < _recentEvents.Length)
            {
                _recentEvents[_recentCount++] = combatEvent;
                return;
            }

            Array.Copy(_recentEvents, 1, _recentEvents, 0, _recentEvents.Length - 1);
            _recentEvents[_recentEvents.Length - 1] = combatEvent;
        }

        /// <summary>
        /// 通过 Input System 轮询切换键。
        /// </summary>
        /// <remarks>
        /// 刻意不使用 <c>UnityEngine.Input</c>。项目设置为只使用 Input System包时，
        /// 旧类首次访问就会抛出异常；用于诊断游戏的调试工具若反而导致崩溃，比没有工具更糟。
        /// </remarks>
        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard[_toggleKey].wasPressedThisFrame)
            {
                _isVisible = !_isVisible;
            }

            TickHeartbeat();
        }

        /// <summary>
        /// 按较慢间隔把战斗生命体征写入日志。
        /// </summary>
        /// <remarks>
        /// 屏幕面板需要有人坐在屏幕前观察，日志不需要：浏览器中它会进入开发者控制台，
        /// 因此可以从无人游玩的标签页远程诊断部署构建。这一点很重要，因为首领会攻击静止玩家，
        /// 足以证明伤害是否生效，无需任何输入。
        /// </remarks>
        private void TickHeartbeat()
        {
            if (!_logHeartbeat || Time.unscaledTime > _heartbeatStopsAfterSeconds)
            {
                return;
            }

            if (Time.unscaledTime < _nextHeartbeatAt)
            {
                return;
            }

            _nextHeartbeatAt = Time.unscaledTime + _heartbeatSeconds;

            string clock = _time == null
                ? "no-time-service"
                : $"{(_time.IsPaused ? "PAUSED" : "run")} scale={_time.TimeScale:0.00} t={_time.CombatTime:0.0}";

            string player = _player == null
                ? "player=MISSING"
                : $"player[{_player.CurrentStateName}] hp={Amount(_player.Health)} " +
                  $"sta={Amount(_player.Stamina)} post={Amount(_player.Posture)} " +
                  $"init={_player.IsInitialised} invuln={_player.IsInvulnerable} " +
                  $"guard={_player.IsGuarding} immortal={_player.IsImmortal}";

            string boss = _boss == null
                ? "boss=MISSING"
                : $"boss[{_boss.CurrentStateName}] hp={Amount(_boss.Health)} " +
                  $"poise={Amount(_boss.Poise)} init={_boss.IsInitialised}";

            Debug.Log($"[DIAG] {clock} | {player} | {boss} | {CountsSummary()}");
        }

        /// <summary>汇总计数，仅列出确实发生过的事件类型。</summary>
        /// <remarks>
        /// 使用独立构建器，而非面板的构建器。两者位于不同回调，目前不会冲突，
        /// 但共享临时缓冲会形成一种耦合，首次调整调用位置时就可能悄悄失效。
        /// </remarks>
        private string CountsSummary()
        {
            _countsBuilder.Clear();
            _countsBuilder.Append("events: ");

            bool any = false;

            for (int i = 0; i < _eventCounts.Length; i++)
            {
                if (_eventCounts[i] == 0)
                {
                    continue;
                }

                any = true;
                _countsBuilder.Append((CombatEventKind)i).Append('=').Append(_eventCounts[i]).Append(' ');
            }

            if (!any)
            {
                _countsBuilder.Append("NONE");
            }

            return _countsBuilder.ToString();
        }

        private void OnGUI()
        {
            // 玩家构建也可通过切换键启用。面板起初是调校辅助，也是诊断突然沉寂战斗的唯一途径；
            // 值得诊断的战斗通常来自玩家对发布构建的反馈，而非编辑器中复现的情况。
            if (!_isVisible && !_forced)
            {
                return;
            }

            EnsureStyle();

            GUILayout.BeginArea(
                new Rect(PanelMargin, PanelMargin, PanelWidth, Screen.height - PanelMargin * 2f),
                GUI.skin.box);

            GUILayout.Label(BuildReport(), _style);
            GUILayout.EndArea();
        }

        /// <summary>
        /// 将整个面板组合为一个字符串，逐帧重建且不分配内存。
        /// </summary>
        /// <remarks>
        /// 战斗诊断优先且无条件绘制。后续内容依赖完整构造的首领，
        /// 而首领构造失败恰恰是此面板必须展示的问题，因此适应部分降级为一行说明，
        /// 而非让整个叠加层变空。
        /// </remarks>
        private string BuildReport()
        {
            _builder.Clear();

            AppendClock();
            AppendCombatants();
            AppendEventTail();

            AdaptationManager adaptation = _boss != null ? _boss.GetAdaptation() : null;
            BossTuning tuning = _boss != null ? _boss.Tuning : null;

            if (adaptation == null || tuning == null)
            {
                _builder.AppendLine("<b>LEARNING</b>");
                _builder.AppendLine(_boss == null
                    ? "no boss in the scene."
                    : "boss has not finished initialising.");

                return _builder.ToString();
            }

            AppendProfile(adaptation.Profile);
            AppendTuning(tuning);
            AppendAdopted(adaptation);

            return _builder.ToString();
        }

        /// <summary>
        /// 报告时钟状态，因为它控制几乎所有其他系统。
        /// </summary>
        /// <remarks>
        /// 暂停或零缩放的时钟会在任何逻辑运行前阻止两个控制器的 <c>Update</c>，冻结全部进度条、
        /// 阻止死亡和结果界面，且不产生任何日志。这是成本最低的排查项，所以先展示。
        /// </remarks>
        private void AppendClock()
        {
            if (_time == null)
            {
                _builder.AppendLine("<b>CLOCK</b>   no time service");
                _builder.AppendLine();
                return;
            }

            _builder.AppendLine(
                $"<b>CLOCK</b>   {(_time.IsPaused ? "<b>PAUSED</b>" : "running")}   " +
                $"scale {_time.TimeScale:0.000}   dt {_time.DeltaTime:0.0000}");
            _builder.AppendLine($"combat time {_time.CombatTime:0.0}s");
            _builder.AppendLine();
        }

        /// <summary>
        /// 使用原始数值而非百分比报告双方状态。
        /// </summary>
        /// <remarks>
        /// 刻意显示当前值和最大值：卡在 100% 的进度条与完全未被驱动的进度条看起来一样，
        /// 原始数值能立即区分。旁边的状态标记覆盖所有可能悄悄吞掉命中的保护条件。
        /// </remarks>
        private void AppendCombatants()
        {
            if (_player == null)
            {
                _builder.AppendLine("<b>PLAYER</b>   not found in scene");
            }
            else
            {
                _builder.AppendLine(
                    $"<b>PLAYER</b>   {_player.CurrentStateName}" +
                    (_player.IsInitialised ? string.Empty : "   <b>UNINITIALISED</b>"));
                _builder.AppendLine(
                    $"hp {Amount(_player.Health)}   sta {Amount(_player.Stamina)}   " +
                    $"posture {Amount(_player.Posture)}");
                _builder.AppendLine(
                    $"invuln {Flag(_player.IsInvulnerable)}   guard {Flag(_player.IsGuarding)}   " +
                    $"immortal {Flag(_player.IsImmortal)}   alive {Flag(_player.IsAlive)}");
            }

            _builder.AppendLine();

            if (_boss == null)
            {
                _builder.AppendLine("<b>BOSS</b>   not found in scene");
                _builder.AppendLine();
                return;
            }

            _builder.AppendLine(
                $"<b>BOSS</b>   phase {_boss.PhaseIndex + 1}   {_boss.CurrentStateName}" +
                (_boss.IsInitialised ? string.Empty : "   <b>UNINITIALISED</b>"));
            _builder.AppendLine($"hp {Amount(_boss.Health)}   poise {Amount(_boss.Poise)}");

            BossContext context = _boss.GetContext();

            if (context != null)
            {
                _builder.AppendLine(
                    $"cooldown {context.AttackCooldownRemaining:0.00}   " +
                    $"sees player {Flag(context.HasPerceivedPlayer)}   " +
                    $"overbalanced {Flag(context.IsOverbalanced)}");
            }

            _builder.AppendLine($"phase invuln {_boss.PhaseTransitionInvulnRemaining:0.00}");
            _builder.AppendLine();
        }

        /// <summary>
        /// 列出最近的战斗事件。
        /// </summary>
        /// <remarks>
        /// 其他面板内容都为支持此诊断：完全无事件表示未发动攻击；连续空挥表示攻击未命中；
        /// 规避表示无敌保护吞掉命中；命中而进度条不动表示伤害真实存在，错误在界面。注意：
        /// 弹反或较晚格挡会产生执行器不发布的结果，因此被防御接下的命中不会留下记录。
        /// </remarks>
        private void AppendEventTail()
        {
            _builder.AppendLine($"<b>LAST {EventTailLength} COMBAT EVENTS</b>");

            if (_events == null)
            {
                _builder.AppendLine("no combat event bus.");
                _builder.AppendLine();
                return;
            }

            if (_recentCount == 0)
            {
                _builder.AppendLine("<b>nothing has happened yet.</b>");
                _builder.AppendLine();
                return;
            }

            for (int i = _recentCount - 1; i >= 0; i--)
            {
                CombatEvent recorded = _recentEvents[i];
                string actor = recorded.Actor == CombatantTeam.Player ? "you " : "boss";

                _builder.AppendLine(
                    $"{recorded.Timestamp,6:0.0}  {actor}  {recorded.Kind}" +
                    (recorded.Magnitude > 0f ? $"  {recorded.Magnitude:0.#}" : string.Empty));
            }

            _builder.AppendLine();
        }

        /// <summary>将资源池显示为当前值和最大值，以区分静止进度条与满条。</summary>
        private static string Amount(IResourcePool pool) =>
            pool == null ? "n/a" : $"{pool.Current:0}/{pool.Maximum:0}";

        /// <summary>紧凑显示状态标记，突出可能解释命中被吞掉的状态。</summary>
        private static string Flag(bool value) => value ? "<b>YES</b>" : "no";

        /// <summary>列出每项习惯的强度及证据充分程度。</summary>
        private void AppendProfile(BehaviorProfile profile)
        {
            _builder.AppendLine("<b>WHAT IT THINKS IT KNOWS</b>");
            _builder.AppendLine("<i>habit                 value  confidence</i>");

            foreach (BehaviorFeature feature in Enum.GetValues(typeof(BehaviorFeature)))
            {
                FeatureReading reading = profile.Get(feature);

                _builder.Append(feature.ToString().PadRight(22));
                _builder.Append(Bar(reading.Value));
                _builder.Append(' ');
                _builder.AppendLine(Percent(reading.Confidence));
            }

            _builder.AppendLine($"strongest read: {profile.StrongestFeature()}");
            _builder.AppendLine();
        }

        /// <summary>仅列出确实偏离基线的行为参数。</summary>
        private void AppendTuning(BossTuning tuning)
        {
            _builder.AppendLine("<b>HOW IT HAS CHANGED</b>");

            bool anyChanged = false;

            foreach (BossTuningParameter parameter in Enum.GetValues(typeof(BossTuningParameter)))
            {
                if (tuning.DeviationFromBaseline(parameter) < 0.01f)
                {
                    continue;
                }

                anyChanged = true;

                _builder.Append(parameter.ToString().PadRight(24));
                _builder.Append(tuning.Get(parameter).ToString("0.00"));
                _builder.Append("  -> ");
                _builder.AppendLine(tuning.GetTarget(parameter).ToString("0.00"));
            }

            if (!anyChanged)
            {
                _builder.AppendLine("(nothing yet - fighting from its baseline)");
            }

            _builder.AppendLine();
        }

        /// <summary>按形成顺序列出首领已经形成的应对。</summary>
        private void AppendAdopted(AdaptationManager adaptation)
        {
            _builder.AppendLine("<b>ANSWERS DEVELOPED</b>");

            if (adaptation.AdoptedStrategies.Count == 0)
            {
                _builder.AppendLine("(none)");
                return;
            }

            foreach (CounterStrategy strategy in adaptation.AdoptedStrategies)
            {
                if (strategy == null)
                {
                    continue;
                }

                _builder.AppendLine($"- {strategy.name}");

                if (!string.IsNullOrWhiteSpace(strategy.TellMessage))
                {
                    _builder.AppendLine($"    \"{strategy.TellMessage}\"");
                }
            }
        }

        /// <summary>将零至一的值显示为固定宽度的文字进度条。</summary>
        private static string Bar(float normalized)
        {
            int filled = Mathf.Clamp(Mathf.RoundToInt(normalized * BarSegments), 0, BarSegments);
            return new string('#', filled).PadRight(BarSegments, '.');
        }

        private static string Percent(float normalized) => $"{Mathf.RoundToInt(normalized * 100f),3}%";

        /// <summary>延迟创建面板样式，因为首次绘制前 GUI 样式不可用。</summary>
        private void EnsureStyle()
        {
            if (_style != null)
            {
                return;
            }

            _style = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                fontSize = 11,
                alignment = TextAnchor.UpperLeft,
                font = Font.CreateDynamicFontFromOSFont("Consolas", 11)
            };
        }
    }
}
