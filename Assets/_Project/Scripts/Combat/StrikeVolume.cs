using UnityEngine;

namespace AdaptiveBossArena.Combat
{
    /// <summary>
    /// 可打击的身体部位：沿此变换前方从根部到尖端形成胶囊体。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 挂在所属骨骼或武器上，与玩家看到的动作完全一致：动画挥动的刀刃、打出的拳头。
    /// 伤害来自实际碰到玩家的部位，而非无论身体怎样移动，都由攻击者前方扇形决定命中。
    /// </para>
    /// <para>
    /// 沿前方的跨度与武器拖尾采样一致，因此可见拖痕与命中胶囊是同一条线。表现绝不回传到判定，
    /// 这里只回答部位的位置。
    /// </para>
    /// </remarks>
    public sealed class StrikeVolume : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Which part of the body this is.")]
        private StrikerParts _part = StrikerParts.Weapon;

        [SerializeField]
        [Tooltip("Distance along this transform's forward where the striking part begins.")]
        private float _baseDistance;

        [SerializeField]
        [Tooltip("Distance along this transform's forward where it ends: the tip of the blade, the knuckles.")]
        private float _tipDistance = 0.5f;

        [SerializeField]
        [Tooltip("Thickness of the part, in metres.")]
        private float _radius = 0.1f;

        /// <summary>这是哪一身体部位。</summary>
        public StrikerParts Part => _part;

        /// <summary>部位厚度。</summary>
        public float Radius => _radius;

        /// <summary>
        /// 部位最后一次使用有效攻击打击的游戏时间，从未打击时为负。
        /// </summary>
        /// <remarks>仅由判定盒视图读取，显示当前有效的部位。</remarks>
        public float LastLiveTime { get; private set; } = -1f;

        /// <summary>记录部位当前正用有效攻击打击。</summary>
        /// <param name="time">当前游戏时间。</param>
        internal void MarkLive(float time) => LastLiveTime = time;

        /// <summary>
        /// 此部位是否可用于打击。
        /// </summary>
        /// <remarks>
        /// 对象隐藏时为 false，例如巨兽狂暴阶段丢掉巨剑后的刀刃。
        /// </remarks>
        public bool IsAvailable => isActiveAndEnabled;

        /// <summary>配置部位形状，供预制体生成器使用。</summary>
        /// <param name="part">这是哪一身体部位。</param>
        /// <param name="baseDistance">沿前方的起始距离。</param>
        /// <param name="tipDistance">沿前方的结束距离。</param>
        /// <param name="radius">部位厚度。</param>
        public void Configure(StrikerParts part, float baseDistance, float tipDistance, float radius)
        {
            _part = part;
            _baseDistance = baseDistance;
            _tipDistance = Mathf.Max(baseDistance, tipDistance);
            _radius = Mathf.Max(0.01f, radius);
        }

        /// <summary>部位当前位置，用胶囊体两端表示。</summary>
        /// <param name="start">世界空间根部。</param>
        /// <param name="end">世界空间尖端。</param>
        public void Segment(out Vector3 start, out Vector3 end)
        {
            Vector3 origin = transform.position;
            Vector3 along = transform.forward;
            start = origin + along * _baseDistance;
            end = origin + along * _tipDistance;
        }

        private void OnDrawGizmosSelected()
        {
            Segment(out Vector3 start, out Vector3 end);
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(start, _radius);
            Gizmos.DrawWireSphere(end, _radius);
            Gizmos.DrawLine(start, end);
        }
    }
}
