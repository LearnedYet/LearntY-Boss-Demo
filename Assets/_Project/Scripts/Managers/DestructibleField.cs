using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Combat.Feel;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Services;
using UnityEngine;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 在战斗最重攻击落地的位置破坏竞技场石材。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 两类事件破坏石材：留下痕迹的地面冲击，例如砸击、冲击波和灾变波；以及身体撞墙。
    /// 它们已分别由危险区域和战斗事件总线报告，因此这里只监听。它位于组合根，因为需要两者，
    /// 而战斗代码与竞技场本身不应知道对方。
    /// </para>
    /// <para>
    /// 仅负责表现。这里可以监听即时总线，理由正是 AI 端禁止这样做的原因：首领决策不依赖它。它使用独立、
    /// 一次设置种子的随机流，而非战斗共享随机源；碎片若从首领随机流取数，
    /// 首领选择就会取决于恰好破坏了多少石材。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DestructibleField : MonoBehaviour
    {
        private const uint DebrisSeed = 772026u;

        /// <summary>攻击打碎首块石材及后续每块的震动，带上限。</summary>
        private const float FirstBreakTrauma = 0.12f;

        private const float ExtraBreakTrauma = 0.04f;
        private const float MaximumBreakTrauma = 0.3f;

        [SerializeField]
        [Tooltip("Where broken pieces come from.")]
        private DebrisPool _pool;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Extra reach beyond a ground scar's radius, so a slam against the parapet cracks it.")]
        private float _impactReachMargin = 0.8f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("How far along the wall a body driven into it breaks stone.")]
        private float _wallImpactReach = 2.2f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Typical launch speed of a broken piece, in metres per second.")]
        private float _pieceSpeed = 4.5f;

        private readonly XorShiftRandomProvider _random = new XorShiftRandomProvider(DebrisSeed);
        private IScreenShake _shake;

        private Destructible[] _destructibles = new Destructible[0];
        private ICombatEventBus _events;
        private HazardField _hazards;

        /// <summary>竞技场中的全部可破坏物。</summary>
        public Destructible[] Destructibles => _destructibles;

        /// <summary>分配对象池，供场景生成器使用。</summary>
        /// <param name="pool">破碎碎片的来源。</param>
        public void Bind(DebrisPool pool) => _pool = pool;

        private void Start()
        {
            _destructibles = FindObjectsByType<Destructible>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            if (ServiceRegistry.Current != null && ServiceRegistry.Current.TryGet(out _events))
            {
                _events.EventRecorded += OnCombatEvent;
            }

            ServiceRegistry.Current?.TryGet(out _shake);

            _hazards = FindAnyObjectByType<HazardField>();

            if (_hazards != null)
            {
                _hazards.Spawned += OnHazardSpawned;
            }
        }

        private void OnDestroy()
        {
            if (_events != null)
            {
                _events.EventRecorded -= OnCombatEvent;
            }

            if (_hazards != null)
            {
                _hazards.Spawned -= OnHazardSpawned;
            }
        }

        /// <summary>破坏冲击范围内的全部完整石材。</summary>
        /// <param name="impact">攻击落点。</param>
        /// <param name="reach">沿地面的破坏范围。</param>
        /// <returns>被打碎的石材数量。</returns>
        public int BreakWithin(Vector3 impact, float reach)
        {
            int broken = 0;

            foreach (Destructible destructible in _destructibles)
            {
                if (destructible != null &&
                    DestructionRules.Reaches(impact, reach + destructible.ReachBonus, destructible.Centre) &&
                    destructible.Break(impact, _pool, _random, _pieceSpeed))
                {
                    broken++;
                }
            }

            // 石材坍塌不仅可见，也可通过震动感到；数量越多震动越强，但有上限，
            // 避免灾变波打碎半圈矮墙时震动超过攻击本身。
            if (broken > 0)
            {
                _shake?.AddTrauma(Mathf.Min(MaximumBreakTrauma, FirstBreakTrauma + ExtraBreakTrauma * (broken - 1)));
            }

            return broken;
        }

        /// <summary>为重试恢复房间完整状态并清除碎片。</summary>
        public void RestoreAll()
        {
            foreach (Destructible destructible in _destructibles)
            {
                if (destructible != null)
                {
                    destructible.Restore();
                }
            }

            if (_pool != null)
            {
                _pool.Clear();
            }

            // 重新设置种子，使每次尝试的石材破坏方式一致。
            _random.Reseed(DebrisSeed);
        }

        private void OnCombatEvent(CombatEvent combatEvent)
        {
            if (combatEvent.Kind == CombatEventKind.WallImpact)
            {
                BreakWithin(combatEvent.Position, _wallImpactReach);
            }
        }

        private void OnHazardSpawned(Vector3 centre, float radius) => BreakWithin(centre, radius + _impactReachMargin);
    }
}
