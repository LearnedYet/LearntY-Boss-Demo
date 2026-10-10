using UnityEngine;

namespace AdaptiveBossArena.Combat
{
    /// <summary>
    /// 查找攻击体积当前重叠的受击盒。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 检测是攻击生效期间执行的查询，而不是触发器回调。触发器事件在物理步中触发，
    /// 未必与攻击生效窗口所跨越的帧对齐，这使得短暂的生效窗口不可靠，且几乎无法测试。
    /// 查询让时序完全由攻击控制。
    /// </para>
    /// <para>
    /// 缓冲区只分配一次并重复使用。另一种方案会在垃圾回收最容易被察觉的帧中分配内存。
    /// </para>
    /// </remarks>
    public sealed class AttackHitDetector
    {
        /// <summary>每次查询考虑的碰撞体数量上限，远高于决斗的实际需求。</summary>
        public const int MaxOverlaps = 16;

        private readonly Collider[] _overlapBuffer = new Collider[MaxOverlaps];
        private readonly Hurtbox[] _resultBuffer = new Hurtbox[MaxOverlaps];

        /// <summary>最近一次查询发现的受击盒，有效范围由返回数量指定。</summary>
        public Hurtbox[] Results => _resultBuffer;

        /// <summary>
        /// 查询攻击体积内的受击盒。
        /// </summary>
        /// <param name="attack">提供体积形状和尺寸的攻击。</param>
        /// <param name="origin">攻击体积定位和定向所相对的 Transform。</param>
        /// <param name="layerMask">需要检测的层。</param>
        /// <returns>写入 <see cref="Results"/> 的受击盒数量。</returns>
        public int Query(AttackDefinition attack, Transform origin, int layerMask)
        {
            int overlapCount = QueryColliders(attack, origin, layerMask);
            int resultCount = 0;

            for (int i = 0; i < overlapCount; i++)
            {
                var hurtbox = _overlapBuffer[i].GetComponent<Hurtbox>();

                if (hurtbox == null || !hurtbox.IsValid)
                {
                    continue;
                }

                if (attack.Shape == AttackShape.Arc && !IsInsideArc(attack, origin, _overlapBuffer[i]))
                {
                    continue;
                }

                resultCount = AddPreferringWeakPoint(hurtbox, resultCount);
            }

            return resultCount;
        }

        /// <summary>
        /// 记录受击盒，每个战斗参与者只保留价值最高的一个。
        /// </summary>
        /// <remarks>
        /// 带有弱点的首领具有多个受击盒，宽幅挥击可能同时重叠多个受击盒。
        /// 执行器已命中集合以所属对象为键，因此只有第一个受击盒会生效——而物理重叠查询没有确定的返回
        /// 顺序，这会让命中弱点成为掷硬币般的随机结果，而不是玩家决策。在此优先选择最高倍率，
        /// 使瞄准核心有实际价值：只要挥击触及核心，就算命中核心。
        /// </remarks>
        /// <param name="hurtbox">新发现的受击盒。</param>
        /// <param name="resultCount">目前已记录的结果数量。</param>
        /// <returns>更新后的结果数量。</returns>
        private int AddPreferringWeakPoint(Hurtbox hurtbox, int resultCount)
        {
            for (int i = 0; i < resultCount; i++)
            {
                if (!ReferenceEquals(_resultBuffer[i].Owner, hurtbox.Owner))
                {
                    continue;
                }

                if (hurtbox.DamageMultiplier > _resultBuffer[i].DamageMultiplier)
                {
                    _resultBuffer[i] = hurtbox;
                }

                return resultCount;
            }

            _resultBuffer[resultCount++] = hurtbox;

            return resultCount;
        }

        /// <summary>执行与形状对应的物理重叠查询。</summary>
        private int QueryColliders(AttackDefinition attack, Transform origin, int layerMask)
        {
            Vector3 center = origin.TransformPoint(attack.Offset);

            switch (attack.Shape)
            {
                case AttackShape.Box:
                    return Physics.OverlapBoxNonAlloc(
                        center,
                        attack.BoxHalfExtents,
                        _overlapBuffer,
                        origin.rotation,
                        layerMask,
                        QueryTriggerInteraction.Collide);

                case AttackShape.Arc:
                    // 扇形先以攻击者为中心做球形查询，再按角度缩小范围。如果改为以偏移位置为中心，
                    // 扇形顶点就会位于角色前方，让攻击能够触及角色身后。
                    return Physics.OverlapSphereNonAlloc(
                        origin.position + Vector3.up * attack.Offset.y,
                        attack.Range,
                        _overlapBuffer,
                        layerMask,
                        QueryTriggerInteraction.Collide);

                default:
                    return Physics.OverlapSphereNonAlloc(
                        center,
                        attack.Range,
                        _overlapBuffer,
                        layerMask,
                        QueryTriggerInteraction.Collide);
            }
        }

        /// <summary>
        /// 在实际检测位置准确绘制扇形。
        /// </summary>
        /// <remarks>
        /// 过去，扇形会进入球形分支，在攻击的偏移处绘制——即角色前方一米——而查询以角色自身为中心。
        /// 图形和命中盒相差整整一米，因此根据辅助图形调整攻击距离的人实际上是在依据虚假的图形调参。
        /// 这里也绘制扇形边界线，因为扇形宽度是决定能否命中的主要因素。
        /// </remarks>
        private static void DrawArcGizmo(AttackDefinition attack, Transform origin)
        {
            Vector3 centre = origin.position + Vector3.up * attack.Offset.y;
            Gizmos.DrawWireSphere(centre, attack.Range);

            float halfArc = attack.ArcDegrees * 0.5f;

            Vector3 forward = origin.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < Mathf.Epsilon)
            {
                return;
            }

            forward.Normalize();

            Vector3 leftEdge = Quaternion.Euler(0f, -halfArc, 0f) * forward;
            Vector3 rightEdge = Quaternion.Euler(0f, halfArc, 0f) * forward;

            Gizmos.DrawRay(centre, leftEdge * attack.Range);
            Gizmos.DrawRay(centre, rightEdge * attack.Range);
        }

        /// <summary>检测碰撞体是否位于扇形角宽内。</summary>
        /// <remarks>
        /// 只在水平面测量。如果包含高度，挥击会漏掉略高于或低于攻击者的目标，
        /// 表现为攻击从目标身体中穿过。
        /// </remarks>
        private static bool IsInsideArc(AttackDefinition attack, Transform origin, Collider target)
        {
            Vector3 toTarget = target.bounds.center - origin.position;
            toTarget.y = 0f;

            Vector3 forward = origin.forward;
            forward.y = 0f;

            // 身体的水平半宽。双方使用的胶囊体，其两个水平方向范围都等于胶囊半径。
            Vector3 extents = target.bounds.extents;
            float targetRadius = Mathf.Max(extents.x, extents.z);

            return IsWithinArc(forward, toTarget, targetRadius, attack.ArcDegrees);
        }

        /// <summary>
        /// 判断给定宽度的身体是否落在挥击扇形内。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 角度按目标中心测量，但检测会考虑目标自身具有宽度。
        /// 只比较中心角和扇形——最初的实现正是如此——会隐式要求目标的<em>轴线</em>位于扇形内，
        /// 并丢弃轴线到身体近侧边缘之间的所有部分。
        /// </para>
        /// <para>
        /// 这不是一个小误差。首领受击盒半径为 0.85 米，在 2.5 米距离时，它占据约 39 度；
        /// 过去每一侧最多有 19 度被丢弃。挥击扇形明明与首领重叠，却因为中心不在其中而被拒绝，
        /// 这正是“明明打中了”的抱怨来源；窄幅攻击和最大的目标受影响最严重。
        /// </para>
        /// <para>
        /// 采用纯函数和静态方法，让几何计算无需场景、碰撞体或物理步即可直接测试。
        /// </para>
        /// </remarks>
        /// <param name="forward">攻击者朝向，投影到水平面。</param>
        /// <param name="toTarget">从攻击者到目标中心的方向，投影到水平面。</param>
        /// <param name="targetRadius">目标身体的半宽。</param>
        /// <param name="arcDegrees">挥击的完整角宽。</param>
        /// <returns>身体的任意部分位于扇形内时为 true。</returns>
        public static bool IsWithinArc(
            Vector3 forward, Vector3 toTarget, float targetRadius, float arcDegrees)
        {
            float distance = toTarget.magnitude;

            // 与攻击者重叠的位置视为命中：此时没有有意义的方向可用于排除目标，
            // 而贴身距离的未命中会被认为是缺陷。
            if (distance < Mathf.Epsilon)
            {
                return true;
            }

            float radius = Mathf.Max(0f, targetRadius);

            // 身体半宽大于自身距离时，会包围攻击者，因此朝任何方向挥击都不可能避开它。
            // 如果只依赖下方计算，结果会饱和到四分之一圈，导致一个紧贴攻击者且略在其身后的目标被排除，
            // 即使它明显与攻击者重叠。
            if (radius >= distance)
            {
                return true;
            }

            // 身体近侧边缘偏离轴线的角度。
            float subtendedHalfAngle = Mathf.Asin(Mathf.Clamp01(radius / distance)) * Mathf.Rad2Deg;

            return Vector3.Angle(forward, toTarget) <= (arcDegrees * 0.5f) + subtendedHalfAngle;
        }

        /// <summary>仅在编辑器中绘制攻击体积用于调试。</summary>
        /// <param name="attack">提供攻击体积的攻击。</param>
        /// <param name="origin">攻击体积所相对的 Transform。</param>
        public static void DrawGizmo(AttackDefinition attack, Transform origin)
        {
#if UNITY_EDITOR
            if (attack == null || origin == null)
            {
                return;
            }

            Gizmos.color = attack.TelegraphColor;

            switch (attack.Shape)
            {
                case AttackShape.Box:
                    Gizmos.matrix = Matrix4x4.TRS(
                        origin.TransformPoint(attack.Offset), origin.rotation, Vector3.one);
                    Gizmos.DrawWireCube(Vector3.zero, attack.BoxHalfExtents * 2f);
                    Gizmos.matrix = Matrix4x4.identity;
                    break;

                case AttackShape.Arc:
                    DrawArcGizmo(attack, origin);
                    break;

                default:
                    Gizmos.DrawWireSphere(origin.TransformPoint(attack.Offset), attack.Range);
                    break;
            }
#endif
        }
    }
}
