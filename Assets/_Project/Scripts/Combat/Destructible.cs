using AdaptiveBossArena.Combat.Feel;
using AdaptiveBossArena.Core.Services;
using UnityEngine;

namespace AdaptiveBossArena.Combat
{
    /// <summary>
    /// 可被重击打碎为掉落石块的竞技场部件。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 破坏隐藏完整石材，并从碎片池向其原有空间外抛出碎片。绝不修改碰撞体：破坏仅是装饰，
    /// 所以战斗几何在破坏前后保持一致，损坏矮墙不会打开竞技场边界缺口。
    /// </para>
    /// <para>
    /// 每次重试恢复，使尝试都从同一房间开始。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class Destructible : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Every visible part of the intact stone, hidden when this breaks.")]
        private Renderer[] _intact = new Renderer[0];

        [SerializeField]
        [Min(0f)]
        [Tooltip("Extra reach for stone that stands back from where blows land, such as a column behind the wall.")]
        private float _reachBonus;

        [SerializeField]
        [Tooltip("Dimensions of the intact stone, in metres, which the pieces are cut from.")]
        private Vector3 _size = Vector3.one;

        [SerializeField]
        [Range(1, 16)]
        [Tooltip("How many pieces it breaks into.")]
        private int _pieceCount = 5;

        /// <summary>本次尝试中是否已经被破坏。</summary>
        public bool IsBroken { get; private set; }

        /// <summary>完整石材的中心。</summary>
        public Vector3 Centre => transform.position;

        /// <summary>超出冲击自身范围后仍能破坏此石材的额外距离。</summary>
        /// <remarks>
        /// 立柱在环墙后三米，那里不会有攻击落地。若无此值，只能被战斗无法产生的冲击打碎；加入后，
        /// 在柱前撞墙的冲锋就能使它倒塌。
        /// </remarks>
        public float ReachBonus => _reachBonus;

        /// <summary>分配石材及尺寸，供场景生成器使用。</summary>
        /// <param name="intact">完整石材的全部可见部件。</param>
        /// <param name="size">用于切分碎片的尺寸，单位为米。</param>
        /// <param name="pieceCount">破碎后的碎片数量。</param>
        /// <param name="reachBonus">后方石材相对于攻击落点的额外影响距离。</param>
        public void Bind(Renderer[] intact, Vector3 size, int pieceCount, float reachBonus = 0f)
        {
            _intact = intact ?? new Renderer[0];
            _size = size;
            _pieceCount = pieceCount;
            _reachBonus = reachBonus;
        }

        /// <summary>打碎石材，将碎片朝远离冲击点的方向抛出。</summary>
        /// <param name="impact">攻击落点。</param>
        /// <param name="pool">碎片来源。</param>
        /// <param name="random">所有散射使用的带种子随机源。</param>
        /// <param name="speed">碎片典型发射速度。</param>
        /// <returns>本次刚破坏时为 true，已被破坏时为 false。</returns>
        public bool Break(Vector3 impact, DebrisPool pool, IRandomProvider random, float speed)
        {
            if (IsBroken)
            {
                return false;
            }

            IsBroken = true;
            SetVisible(false);

            if (pool == null || random == null)
            {
                return true;
            }

            Vector3 half = _size * 0.5f;

            // 缩放碎片，使总体积大致等于原石材，而非 n 份原石材。
            float pieceScale = Mathf.Pow(1f / Mathf.Max(1, _pieceCount), 1f / 3f);

            for (int i = 0; i < _pieceCount; i++)
            {
                var local = new Vector3(
                    random.NextFloat(-half.x, half.x) * 0.6f,
                    random.NextFloat(-half.y, half.y) * 0.6f,
                    random.NextFloat(-half.z, half.z) * 0.6f);

                Vector3 start = transform.TransformPoint(local);
                Vector3 size = _size * (pieceScale * random.NextFloat(0.7f, 1f));
                var spin = new Vector3(random.NextFloat(-6f, 6f), random.NextFloat(-6f, 6f), random.NextFloat(-6f, 6f));

                pool.Launch(start, transform.rotation, size,
                    DestructionRules.PieceVelocity(random, impact, start, speed), spin);
            }

            return true;
        }

        /// <summary>为重试恢复完整石材。</summary>
        public void Restore()
        {
            IsBroken = false;
            SetVisible(true);
        }

        private void SetVisible(bool visible)
        {
            foreach (Renderer part in _intact)
            {
                if (part != null)
                {
                    part.enabled = visible;
                }
            }
        }
    }
}
