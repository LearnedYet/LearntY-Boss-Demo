using UnityEngine;

namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 构建与攻击命中体积匹配的平面地面网格。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 它们由命中检测使用的相同数值生成，因此玩家看到的就是实际检测的范围。
    /// 仅大致接近命中盒的视觉比没有视觉更糟，因为它会让玩家学到不真实的攻击距离。
    /// </para>
    /// <para>
    /// 网格是铺在地面的平面，而不是立体体积。对于俯视摄像机，地面平面是唯一能一眼看清范围的对象，
    /// 悬浮体积会遮住双方战斗者。
    /// </para>
    /// </remarks>
    public static class AttackShapeMesh
    {
        /// <summary>近似完整圆的分段数，越高越平滑。</summary>
        private const int SegmentsPerCircle = 48;

        /// <summary>
        /// 从形状中心到边缘的环，记录向外距离比例及其不透明度。
        /// </summary>
        /// <remarks>
        /// 内部暗淡，边缘内侧窄带明亮，边缘本身柔化。用单一不透明度填充的形状看起来像地面上的彩色圆盘；
        /// 发亮边缘看起来像边界，且仍准确显示危险范围的终点。
        /// </remarks>
        public static readonly (float Scale, float Alpha)[] Rings =
        {
            (0f, 0.08f), (0.78f, 0.16f), (0.93f, 1f), (1f, 0.35f)
        };

        /// <summary>以攻击者原点为中心，构建匹配攻击形状的网格。</summary>
        /// <param name="attack">提供形状和尺寸的攻击。</param>
        /// <returns>新创建的网格，由调用者拥有。</returns>
        public static Mesh Build(AttackDefinition attack)
        {
            if (attack == null)
            {
                return null;
            }

            switch (attack.Shape)
            {
                case AttackShape.Box:
                    return BuildBox(attack.BoxHalfExtents, attack.Offset);

                case AttackShape.Sphere:
                    return BuildFan(attack.Range, 360f, Vector3.zero);

                default:
                    // 扇形以攻击者为中心，与命中检测的处理一致。
                    return BuildFan(attack.Range, attack.ArcDegrees, Vector3.zero);
            }
        }

        /// <summary>构建铺在地面、沿局部前向展开的扇形。</summary>
        private static Mesh BuildFan(float radius, float arcDegrees, Vector3 centre)
        {
            arcDegrees = Mathf.Clamp(arcDegrees, 1f, 360f);

            int segments = Mathf.Max(3, Mathf.RoundToInt(SegmentsPerCircle * (arcDegrees / 360f)));
            bool fullCircle = arcDegrees >= 360f;

            // 扇形轮廓沿圆弧并经过顶点返回；圆形轮廓只有圆弧。
            int outlineCount = fullCircle ? segments : segments + 2;
            var outline = new Vector3[outlineCount];

            float startDegrees = -arcDegrees * 0.5f;
            float stepDegrees = arcDegrees / segments;
            int arcPoints = fullCircle ? segments : segments + 1;

            for (int i = 0; i < arcPoints; i++)
            {
                float radians = (startDegrees + stepDegrees * i) * Mathf.Deg2Rad;

                // 局部空间前方沿 +Z，因此从该轴测量扫掠。
                outline[i] = centre + new Vector3(Mathf.Sin(radians) * radius, 0f, Mathf.Cos(radians) * radius);
            }

            Vector3 middle = centre;

            if (!fullCircle)
            {
                outline[outlineCount - 1] = centre;

                // 各环向扇形中部而不是顶点收缩，让发亮边缘也沿两条直边延伸。
                middle = centre + new Vector3(0f, 0f, radius * 0.5f);
            }

            return BuildRinged(outline, middle, "AttackArc");
        }

        /// <summary>构建铺在地面的平面矩形。</summary>
        private static Mesh BuildBox(Vector3 halfExtents, Vector3 offset)
        {
            float x = halfExtents.x;
            float z = halfExtents.z;
            var centre = new Vector3(offset.x, 0f, offset.z);

            var outline = new[]
            {
                centre + new Vector3(-x, 0f, -z),
                centre + new Vector3(-x, 0f, z),
                centre + new Vector3(x, 0f, z),
                centre + new Vector3(x, 0f, -z)
            };

            return BuildRinged(outline, centre, "AttackBox");
        }

        /// <summary>
        /// 用向中点收缩的环填充闭合轮廓，各环通过顶点透明度携带不透明度。
        /// </summary>
        private static Mesh BuildRinged(Vector3[] outline, Vector3 middle, string name)
        {
            int n = outline.Length;
            var vertices = new Vector3[n * Rings.Length];
            var colours = new Color[vertices.Length];

            for (int ring = 0; ring < Rings.Length; ring++)
            {
                for (int i = 0; i < n; i++)
                {
                    vertices[ring * n + i] = Vector3.Lerp(middle, outline[i], Rings[ring].Scale);
                    colours[ring * n + i] = new Color(1f, 1f, 1f, Rings[ring].Alpha);
                }
            }

            var triangles = new int[(Rings.Length - 1) * n * 6];
            int t = 0;

            for (int ring = 0; ring < Rings.Length - 1; ring++)
            {
                for (int i = 0; i < n; i++)
                {
                    int inner = ring * n + i, innerNext = ring * n + (i + 1) % n;
                    int outer = inner + n, outerNext = innerNext + n;

                    // 绕序取决于轮廓方向；叠加材质绘制双面。
                    triangles[t++] = inner; triangles[t++] = outer; triangles[t++] = outerNext;
                    triangles[t++] = inner; triangles[t++] = outerNext; triangles[t++] = innerNext;
                }
            }

            return Finalise(vertices, colours, triangles, name);
        }

        /// <summary>组装网格并准备渲染。</summary>
        private static Mesh Finalise(Vector3[] vertices, Color[] colours, int[] triangles, string name)
        {
            var mesh = new Mesh
            {
                name = name,
                vertices = vertices,
                colors = colours,
                triangles = triangles
            };

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}
