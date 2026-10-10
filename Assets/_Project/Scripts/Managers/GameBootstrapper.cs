using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Constants;
using AdaptiveBossArena.Core.Services;
using UnityEngine;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 创建游戏长期服务并驱动时钟。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 组合根。只有这里决定每个服务接口对应的具体实现，使其他系统都可以依赖抽象，无需自行创建服务。
    /// </para>
    /// <para>
    /// 执行顺序固定在所有系统之前，原因有二：服务必须在使用者的 <c>Start</c> 运行前存在，
    /// 时间服务必须先推进，其他系统才能在同一帧读取 <see cref="ITimeService.DeltaTime"/>。
    /// 使用者若在时钟推进前读取，就会落后一帧；这类问题很容易逃过审查，却足以破坏战斗时序。
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(ExecutionOrder)]
    [DisallowMultipleComponent]
    public sealed class GameBootstrapper : MonoBehaviour
    {
        /// <summary>远早于玩法执行，确保服务存在且时钟已推进。</summary>
        public const int ExecutionOrder = -10000;

        [Header("Determinism")]
        [SerializeField]
        [Tooltip("Seed for all gameplay randomness. Zero draws a fresh seed from the system clock. " +
                 "Set a specific value to reproduce a fight exactly.")]
        private int _randomSeed;

        [SerializeField]
        [Tooltip("Log the seed in use at startup, so a fight worth reproducing can be recovered " +
                 "from the player log.")]
        private bool _logSeed = true;

        private TimeService _timeService;
        private CombatEventBus _combatEvents;
        private ServiceRegistry _registry;

        /// <summary>所有玩法随机性启动时采用的种子。</summary>
        public uint ActiveSeed { get; private set; }

        private void Awake()
        {
            _registry = new ServiceRegistry();

            _timeService = new TimeService(GameplayConstants.FixedTimeStep);
            _registry.Register<ITimeService>(_timeService);

            ActiveSeed = ResolveSeed();
            _registry.Register<IRandomProvider>(new XorShiftRandomProvider(ActiveSeed));

            // 在此注册，而非由任一交战者拥有，因为首领通过该通道了解玩家已发生的行为，
            // 同时双方都无需持有对方引用。
            _combatEvents = new CombatEventBus();
            _registry.Register<ICombatEventBus>(_combatEvents);

            _registry.Register<ISaveService>(new JsonSaveService());

            ServiceRegistry.SetCurrent(_registry);

            if (_logSeed)
            {
                Debug.Log($"[Adaptive Boss Arena] Gameplay random seed: {ActiveSeed}", this);
            }
        }

        private void Update() => _timeService.Tick(Time.unscaledDeltaTime);

        private void OnDestroy()
        {
            // 时间缩放是引擎全局状态，必须主动释放，不能遗留；若停在顿帧数值，
            // 冻结状态会带入下一场景或返回编辑器。服务拥有并恢复该状态，
            // 因为只有它有权直接修改引擎时间缩放。
            if (_timeService != null)
            {
                _timeService.SetPaused(false);
                _timeService.ClearTimeEffects();
            }

            // 订阅者是与此对象一起销毁的场景对象。清除订阅可防止销毁过程中旧委托调用已经销毁的目标。
            _combatEvents?.Clear();
            _registry?.Clear();
        }

        /// <summary>选择种子，未指定时从系统时钟取得。</summary>
        private uint ResolveSeed() =>
            _randomSeed != 0
                ? unchecked((uint)_randomSeed)
                : unchecked((uint)System.DateTime.Now.Ticks);
    }
}
