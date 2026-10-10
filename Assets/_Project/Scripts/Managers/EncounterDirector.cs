using System.Text;
using AdaptiveBossArena.AI;
using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Combat.Movement;
using AdaptiveBossArena.Core.Constants;
using AdaptiveBossArena.Core.Events;
using AdaptiveBossArena.Core.Services;
using AdaptiveBossArena.Learning;
using AdaptiveBossArena.UI;
using UnityEngine;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 管理单次尝试的生命周期：开始、结果、记录与重试。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 玩家与首领都知道如何重置自己，但都不决定何时重置。决定属于能同时看见双方的这里，
    /// 因此重试使用重置而非重新加载场景；加载会丢弃服务注册表和随机种子，
    /// 而无法跨重试保留的种子不利于复现战斗。
    /// </para>
    /// <para>
    /// 同时记录本次尝试。值得保存的是首领形成了多少反制策略，因为这衡量玩家有多容易被读懂，
    /// 而非行动速度有多快。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class EncounterDirector : MonoBehaviour
    {
        [Header("Combatants")]
        [SerializeField]
        [Tooltip("The player. Found automatically when left empty.")]
        private Player.PlayerController _player;

        [SerializeField]
        [Tooltip("The boss. Found automatically when left empty.")]
        private BossController _boss;

        [Header("Configuration")]
        [SerializeField]
        [Tooltip("Arena configuration supplying the spawn positions.")]
        private ArenaConfig _arenaConfig;

        [Header("Interface")]
        [SerializeField]
        [Tooltip("Screen shown once the fight is decided.")]
        private EndScreen _endScreen;

        [SerializeField]
        [Tooltip("Pause menu, suppressed once the fight ends.")]
        private PauseMenu _pauseMenu;

        [SerializeField]
        [Tooltip("The ready–fight beat played before each attempt. Optional; without it the fight " +
                 "simply begins immediately.")]
        private RoundIntro _roundIntro;

        [Header("Event Channels")]
        [SerializeField]
        [Tooltip("Raised when the player is defeated.")]
        private VoidEventChannel _playerDiedChannel;

        [SerializeField]
        [Tooltip("Raised when the boss is defeated.")]
        private VoidEventChannel _bossDefeatedChannel;

        [SerializeField]
        [Tooltip("Raised on a clean deflect, relayed into posture damage on the boss.")]
        private VoidEventChannel _deflectChannel;

        [SerializeField]
        [Tooltip("Raised on a perfect dodge, counted for the post-fight report.")]
        private VoidEventChannel _perfectDodgeChannel;

        [SerializeField]
        [Tooltip("Raised when the boss parries, relayed into posture damage on the player.")]
        private VoidEventChannel _bossParriedChannel;

        /// <summary>致命一击播放速度，足够慢以形成节拍，而非卡顿。</summary>
        private const float OutcomeSlowMotionScale = 0.35f;

        /// <summary>结果慢动作的持续时间，大致匹配结算界面延迟。</summary>
        private const float OutcomeSlowMotionSeconds = 1.1f;

        /// <summary>处决使用更慢的速度，表现首领防御崩溃时死亡。</summary>
        private const float ExecutionSlowMotionScale = 0.14f;

        /// <summary>处决保持更久，让玩家感受终结一击。</summary>
        private const float ExecutionSlowMotionSeconds = 1.8f;

        /// <summary>处决增加的相机震动强度，使终结更有重量。</summary>
        private const float ExecutionTrauma = 0.8f;

        private ITimeService _time;
        private IScreenShake _screenShake;
        private ISaveService _saveService;
        private RecordsData _records;

        private float _attemptStartedAt;
        private bool _isDecided;

        // 本次尝试中为战后档案累计，开始新尝试时重置。
        private int _deflectsThisAttempt;

        /// <summary>上帧从玩家指向首领的方向，用于捕捉双方穿过彼此的情况。</summary>
        private Vector3 _lastBodyDirection;

        private int _parriedByBossThisAttempt;
        private int _perfectDodgesThisAttempt;

        /// <summary>当前尝试已持续的秒数。</summary>
        public float AttemptDuration =>
            _time == null ? 0f : Mathf.Max(0f, _time.CombatTime - _attemptStartedAt);

        private void Awake()
        {
            if (_player == null)
            {
                _player = FindAnyObjectByType<Player.PlayerController>();
            }

            if (_boss == null)
            {
                _boss = FindAnyObjectByType<BossController>();
            }
        }

        private void OnEnable()
        {
            if (_playerDiedChannel != null)
            {
                _playerDiedChannel.Raised += OnPlayerDied;
            }

            if (_bossDefeatedChannel != null)
            {
                _bossDefeatedChannel.Raised += OnBossDefeated;
            }

            if (_bossParriedChannel != null)
            {
                _bossParriedChannel.Raised += OnParriedByBoss;
            }

            if (_deflectChannel != null)
            {
                _deflectChannel.Raised += OnDeflected;
            }

            if (_perfectDodgeChannel != null)
            {
                _perfectDodgeChannel.Raised += OnPerfectDodge;
            }
        }

        private void OnDisable()
        {
            if (_playerDiedChannel != null)
            {
                _playerDiedChannel.Raised -= OnPlayerDied;
            }

            if (_bossDefeatedChannel != null)
            {
                _bossDefeatedChannel.Raised -= OnBossDefeated;
            }

            if (_bossParriedChannel != null)
            {
                _bossParriedChannel.Raised -= OnParriedByBoss;
            }

            if (_deflectChannel != null)
            {
                _deflectChannel.Raised -= OnDeflected;
            }

            if (_perfectDodgeChannel != null)
            {
                _perfectDodgeChannel.Raised -= OnPerfectDodge;
            }
        }

        private ArenaCameraRig _cameraRig;

        [SerializeField]
        [Tooltip("Shown while the boss kneels within reach. Assigned by the interface generator.")]
        private GameObject _ripostePrompt;

        private void Start()
        {
            ServiceRegistry.Current.TryGet(out _time);
            ServiceRegistry.Current.TryGet(out _screenShake);

            _saveService = ResolveSaveService();
            _records = LoadRecords();

            if (_endScreen != null)
            {
                _endScreen.RestartRequested += RestartAttempt;
            }

            if (_pauseMenu != null)
            {
                _pauseMenu.RestartRequested += RestartAttempt;
            }

            ApplyRunModifiers();
            StartAttemptWithIntro();
        }

        /// <summary>
        /// 转发每方最低限度需要了解的对方信息。
        /// </summary>
        /// <remarks>
        /// 导演是唯一同时看到双方的对象，因此桥接应放在这里。玩家获知首领站位及防御是否崩溃；
        /// 首领不直接获得任何信息，继续只通过延迟观察通道感知玩家。
        /// </remarks>
        private void LateUpdate()
        {
            if (_player == null || _boss == null)
            {
                return;
            }

            _player.SetThreat(_boss.transform);

            // 只提供机会，不自动执行；惩罚动作现在等待玩家按下攻击。
            if (_ripostePrompt != null && _ripostePrompt.activeSelf != _player.RiposteOffered)
            {
                _ripostePrompt.SetActive(_player.RiposteOffered);
            }

            // 只有相机知道玩家是否请求锁定。
            if (_cameraRig == null)
            {
                _cameraRig = FindAnyObjectByType<ArenaCameraRig>();
            }

            _player.SetLockedOn(_cameraRig != null && _cameraRig.IsLockedOn);
            _player.SetThreatPostureBroken(_boss.Poise != null && _boss.Poise.IsBroken);

            SeparateBodies();
            TryClash();
        }

        /// <summary>
        /// 双方身体重叠时将其推开，较轻的一方推得更远。
        /// </summary>
        /// <remarks>
        /// 在双方完成本帧移动后执行，因为只有导演能同时看到并操作双方。
        /// 位置只通过<see cref="IPushBody"/> 接口访问，因此双方都不会获取对方信息。
        /// </remarks>
        private void SeparateBodies()
        {
            IPushBody player = _player;
            IPushBody boss = _boss;

            if (!player.IsSolid || !boss.IsSolid)
            {
                return;
            }

            BodySeparation.Result result = BodySeparation.Resolve(
                player.BodyPosition, player.BodyRadius, player.BodyMass,
                boss.BodyPosition, boss.BodyRadius, boss.BodyMass,
                _lastBodyDirection);

            _lastBodyDirection = result.Direction;

            if (!result.Overlapped)
            {
                return;
            }

            player.Displace(result.PushFirst);
            boss.Displace(result.PushSecond);
        }

        /// <summary>
        /// 两把有效刀刃碰到彼此而非目标时，拒绝双方挥击。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 这里检测，因为只有导演能同时看到双方挥击，且 <c>LateUpdate</c> 位于任一攻击执行器的阶段推
        /// 进循环之外。这是招架处理中唯一可以安全同步停止挥击的位置，但仍沿用双方已经使用的延迟后仰，
        /// 避免同时存在两种停止挥击方式。
        /// </para>
        /// <para>
        /// 自动避免重复触发：后仰会结束双方挥击，所以下一帧双方都不会报告有效刀刃。
        /// </para>
        /// </remarks>
        private void TryClash()
        {
            if (_arenaConfig == null || !_arenaConfig.ClashEnabled)
            {
                return;
            }

            float playerReach = _player.LiveSwingReach;
            float bossReach = _boss.LiveSwingReach;

            Transform playerTransform = _player.transform;
            Transform bossTransform = _boss.transform;

            bool clash = ClashResolver.ShouldClash(
                playerReach > 0f,
                bossReach > 0f,
                Vector3.Distance(playerTransform.position, bossTransform.position),
                playerReach + bossReach,
                ClashResolver.FacingDot(
                    playerTransform.position, playerTransform.forward,
                    bossTransform.position, bossTransform.forward));

            if (!clash)
            {
                return;
            }

            _player.RecoilFromClash();
            _boss.RecoilFromClash();
        }

        /// <summary>
        /// 将精准弹反转为首领受到的姿态伤害。
        /// </summary>
        /// <remarks>
        /// 与上方朝向转发相同，经导演处理，因为玩家不能持有首领引用。玩家报告弹反，
        /// 导演决定它对另一交战者的影响。
        /// </remarks>
        private void OnDeflected()
        {
            _deflectsThisAttempt++;

            if (_player == null || _boss == null)
            {
                return;
            }

            _boss.ApplyDeflectPosture(_player.DeflectPostureDamage);
        }

        /// <summary>
        /// 将首领招架转为玩家受到的姿态伤害。
        /// </summary>
        /// <remarks>
        /// 这是 <see cref="OnDeflected"/> 的完全对称处理，同样经过导演，因为双方不能持有彼此引用。
        /// 此前玩家姿态池只因较晚格挡受损，从不格挡的玩家就永远不会防御崩溃；过度投入毫无代价，
        /// 过度格挡却承担一切。现在两种错误采用相同方式惩罚。
        /// </remarks>
        private void OnParriedByBoss()
        {
            _parriedByBossThisAttempt++;

            if (_player == null || _boss == null)
            {
                return;
            }

            _player.ApplyParriedPosture(_boss.ParryPostureDamage);
        }

        /// <summary>为战后报告统计一次完美闪避。</summary>
        private void OnPerfectDodge() => _perfectDodgesThisAttempt++;

        /// <summary>
        /// 播放准备与开战提示，提示结束后开始本次尝试。
        /// </summary>
        /// <remarks>
        /// 提示期间冻结时间，避免首领在玩家尚未获得提示时抢先攻击；禁止暂停菜单，
        /// 避免它的输入与冻结时钟冲突。未配置提示时直接开始战斗。
        /// </remarks>
        private void StartAttemptWithIntro()
        {
            _endScreen?.Hide();

            if (_roundIntro == null)
            {
                BeginAttempt();
                return;
            }

            _pauseMenu?.SetSuppressed(true);
            _time?.SetPaused(true);

            // 一轮首次尝试先展示首领姓名、咆哮及聚焦镜头；重试则跳过，直接播放简短准备与开战提示，
            // 因为玩家已经见过首领。
            if (!_entranceShown)
            {
                _entranceShown = true;
                PlayEntrance();
                return;
            }

            _roundIntro.Begin(OnIntroComplete);
        }

        /// <summary>首次遇见时显示的首领名称。</summary>
        private const string BossTitle = "空洞巨兽";

        /// <summary>入场咆哮伴随的震动。</summary>
        private const float EntranceRoarTrauma = 0.35f;

        /// <summary>本轮是否已经播放过首领入场。</summary>
        private bool _entranceShown;

        /// <summary>显示首领名称并咆哮时保持聚焦，随后交还战斗控制。</summary>
        private void PlayEntrance()
        {
            _roundIntro.BeginEntrance(BossTitle, OnIntroComplete);

            if (_cameraRig == null)
            {
                _cameraRig = FindAnyObjectByType<ArenaCameraRig>();
            }

            _cameraRig?.PlayEntrance(_roundIntro.HoldSeconds);

            if (ServiceRegistry.Current != null && ServiceRegistry.Current.TryGet(out IAudioService audio))
            {
                audio.PlayCue2D(AudioService.Cues.BossRoar);
            }

            _screenShake?.AddTrauma(EntranceRoarTrauma);
        }

        /// <summary>任意键或点击可跳过入场，避免已看过的玩家被强制等待。</summary>
        private void Update()
        {
            if (_roundIntro == null || !_roundIntro.IsRunning || !SkipPressed())
            {
                return;
            }

            _cameraRig?.EndEntrance();
            _roundIntro.Skip();
        }

        /// <summary>本帧是否按下按键或鼠标左键。</summary>
        private static bool SkipPressed()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            var mouse = UnityEngine.InputSystem.Mouse.current;

            return (keyboard != null && keyboard.anyKey.wasPressedThisFrame) ||
                   (mouse != null && mouse.leftButton.wasPressedThisFrame);
        }

        /// <summary>入场结束后释放战斗。</summary>
        private void OnIntroComplete()
        {
            _time?.SetPaused(false);
            BeginAttempt();
        }

        /// <summary>开始新的尝试。</summary>
        public void BeginAttempt()
        {
            _isDecided = false;
            _attemptStartedAt = _time?.CombatTime ?? 0f;

            _deflectsThisAttempt = 0;
            _parriedByBossThisAttempt = 0;
            _perfectDodgesThisAttempt = 0;

            _pauseMenu?.SetSuppressed(false);
            _endScreen?.Hide();
        }

        /// <summary>重置双方并开始下一次尝试。</summary>
        public void RestartAttempt()
        {
            Vector3 playerSpawn = _arenaConfig != null ? _arenaConfig.PlayerSpawn : Vector3.zero;
            Vector3 bossSpawn = _arenaConfig != null
                ? _arenaConfig.BossSpawn
                : Vector3.forward * GameplayConstants.BossPhaseCount;

            _player?.ResetForNewAttempt(playerSpawn);
            _boss?.ResetForNewAttempt(bossSpawn);

            // 每次尝试都从同一房间开始：破碎石材恢复完整，碎片清除。
            FindAnyObjectByType<DestructibleField>()?.RestoreAll();

            _time?.ClearTimeEffects();
            _time?.ResetCombatClock();

            StartAttemptWithIntro();
        }

        /// <summary>
        /// 将本轮挑战修饰项应用到双方及练习叠加层。
        /// </summary>
        /// <remarks>
        /// 导演是组合根，也是唯一同时看到双方的位置，因此标题界面选择的修饰项在这里转为玩家与首领的数
        /// 值。<c>AI</c> 与 <c>Learning</c> 完全无需知道修饰项存在；每项只缩放既有数值，
        /// 或设置这些系统本来就遵守的标记。
        /// </remarks>
        private void ApplyRunModifiers()
        {
            RunModifiers modifiers = RunSettings.Current ?? new RunModifiers();

            _player?.ApplyRunModifiers(
                modifiers.HealingEnabled, modifiers.IncomingDamageMultiplier, modifiers.TrainingMode);
            _boss?.SetAdaptationRateScale(modifiers.AdaptationRateScale);

            if (modifiers.TrainingMode)
            {
                FindAnyObjectByType<AdaptationDebugOverlay>()?.SetForcedVisible(true);
            }
        }

        private void OnPlayerDied() => Decide(won: false);

        private void OnBossDefeated() => Decide(won: true);

        /// <summary>结束尝试、记录结果并展示结算。</summary>
        private void Decide(bool won)
        {
            // 双方可能在同一帧互换致命一击。先到的事件决定结果；若无此保护，第二个事件会覆盖结果，
            // 并重复记录本次尝试。
            if (_isDecided)
            {
                return;
            }

            _isDecided = true;
            _pauseMenu?.SetSuppressed(true);

            // 首领不能直接看到玩家死亡，防火墙使它从不直接获知玩家内部信息，因此必须由这里叫停。
            // 否则结算界面背后它仍会攻击尸体。
            _boss?.SetFightConcluded(true);

            // 处决，即首领防御崩溃期间被击杀，应该比普通击杀获得更深的电影化节拍；
            // 终结技的意义在于让人感觉回报实至名归，因此世界更慢，相机也强化这一击。
            bool executed = won && _boss?.Poise != null && _boss.Poise.IsBroken;

            if (executed)
            {
                _time?.RequestSlowMotion(ExecutionSlowMotionScale, ExecutionSlowMotionSeconds);

                // 唯一值得从骑士身后之外观察的时刻。
                if (_cameraRig == null)
                {
                    _cameraRig = FindAnyObjectByType<ArenaCameraRig>();
                }

                _cameraRig?.PlayFinisher(ExecutionSlowMotionSeconds);
                _screenShake?.AddTrauma(ExecutionTrauma);
                _screenShake?.Punch(ExecutionTrauma);
            }
            else
            {
                // 决定性一击时让世界进入慢动作。败者仍按未缩放时间倒下，呈现世界屏息注视其倒下的节拍。
                _time?.RequestSlowMotion(OutcomeSlowMotionScale, OutcomeSlowMotionSeconds);
            }

            float duration = AttemptDuration;
            AdaptationManager adaptation = _boss?.GetAdaptation();
            int adaptations = adaptation?.AdoptedStrategies.Count ?? 0;
            float remainingHealth = _player?.Health?.Normalized ?? 0f;

            // 不会死亡的训练玩家否则会永久创造最快胜利及最少适应记录，而且无法撤销。
            bool countsTowardRecords = RunSettings.Current?.CountsTowardRecords ?? true;
            bool isNewRecord = won && countsTowardRecords && IsImprovement(duration, adaptations);

            if (countsTowardRecords)
            {
                _records.RecordAttempt(won, duration, remainingHealth, adaptations);
                _saveService?.Save(SaveKeys.Records, _records);
            }

            string dossier = BuildDossier(adaptation, remainingHealth);
            _endScreen?.Show(won, duration, adaptations, isNewRecord, dossier);
        }

        /// <summary>
        /// 组合首领战后档案：识破的玩家习惯、采取的应对，以及展示战斗实际进程的统计。
        /// </summary>
        /// <remarks>
        /// 习惯描述来自首领推理所用的同一画像，报告不会宣称首领知道它未据以行动的信息。
        /// 这里重述已采纳策略的提示，使被动类习惯也被涵盖；这些习惯在文字列表中被省略，
        /// 因为它们代表行为缺失，而非强烈倾向。
        /// </remarks>
        private string BuildDossier(AdaptationManager adaptation, float remainingHealth)
        {
            var builder = new StringBuilder();

            if (adaptation != null)
            {
                System.Collections.Generic.IReadOnlyList<string> habits =
                    ProfileReport.HabitLines(adaptation.Profile);

                if (habits.Count > 0)
                {
                    builder.AppendLine("<b>它看穿了你的习惯：</b>");
                    foreach (string habit in habits)
                    {
                        builder.Append("• ").AppendLine(habit);
                    }

                    builder.AppendLine();
                }

                AppendAnswers(builder, adaptation);
            }

            int healthPercent = Mathf.RoundToInt(Mathf.Clamp01(remainingHealth) * 100f);
            // 对称统计是决斗报告最有用的信息：只统计弹反只说明玩家防御水平，
            // 无法说明首领多少次反过来识破了玩家。
            builder.Append(
                $"成功弹反 {_deflectsThisAttempt} 次   ·   被首领招架 {_parriedByBossThisAttempt} 次" +
                $"   ·   完美闪避 {_perfectDodgesThisAttempt} 次   ·   剩余生命 {healthPercent}%");

            RunModifiers modifiers = RunSettings.Current;
            if (modifiers != null && modifiers.AnyActive)
            {
                builder.AppendLine().Append("<b>挑战：</b> ")
                    .Append(string.Join(", ", modifiers.ActiveLabels()));
            }

            return builder.ToString();
        }

        /// <summary>附加首领形成的反制策略及各自面向玩家的提示。</summary>
        private static void AppendAnswers(StringBuilder builder, AdaptationManager adaptation)
        {
            if (adaptation.AdoptedStrategies.Count == 0)
            {
                return;
            }

            builder.AppendLine("<b>它采取的应对：</b>");

            foreach (CounterStrategy strategy in adaptation.AdoptedStrategies)
            {
                if (strategy != null && !string.IsNullOrWhiteSpace(strategy.TellMessage))
                {
                    builder.Append("• \"").Append(strategy.TellMessage).AppendLine("\"");
                }
            }

            builder.AppendLine();
        }

        /// <summary>本次尝试超过已保存最佳记录时为 true。</summary>
        private bool IsImprovement(float duration, int adaptations) =>
            !_records.HasWon ||
            duration < _records.FastestVictorySeconds ||
            adaptations < _records.FewestAdaptationsAllowed;

        private ISaveService ResolveSaveService() =>
            ServiceRegistry.Current.TryGet(out ISaveService service) ? service : null;

        private RecordsData LoadRecords()
        {
            if (_saveService != null && _saveService.TryLoad(SaveKeys.Records, out RecordsData loaded))
            {
                return loaded;
            }

            return new RecordsData();
        }

        /// <summary>分配引用，供场景生成器使用。</summary>
        /// <param name="arenaConfig">提供出生位置的竞技场配置。</param>
        /// <param name="endScreen">结果界面。</param>
        /// <param name="pauseMenu">暂停菜单。</param>
        /// <param name="roundIntro">准备与开战节拍；为 null 时立即开始战斗。</param>
        /// <param name="playerDied">玩家死亡通道。</param>
        /// <param name="bossDefeated">首领被击败通道。</param>
        /// <param name="deflect">精准弹反通道。</param>
        /// <param name="perfectDodge">完美闪避通道，供战后报告统计。</param>
        /// <param name="bossParried">首领招架通道，转为玩家受到的姿态伤害。</param>
        public void Bind(
            ArenaConfig arenaConfig,
            EndScreen endScreen,
            PauseMenu pauseMenu,
            RoundIntro roundIntro,
            VoidEventChannel playerDied,
            VoidEventChannel bossDefeated,
            VoidEventChannel deflect,
            VoidEventChannel perfectDodge,
            VoidEventChannel bossParried,
            GameObject ripostePrompt = null)
        {
            _ripostePrompt = ripostePrompt;
            _arenaConfig = arenaConfig;
            _endScreen = endScreen;
            _pauseMenu = pauseMenu;
            _roundIntro = roundIntro;
            _playerDiedChannel = playerDied;
            _bossDefeatedChannel = bossDefeated;
            _deflectChannel = deflect;
            _perfectDodgeChannel = perfectDodge;
            _bossParriedChannel = bossParried;
        }
    }
}
