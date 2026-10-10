using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Editor.Environment
{
    /// <summary>
    /// 纹理按世界尺寸重复、而非拉伸填满的盒形网格。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unity 立方体在每面映射整张纹理，导致 14 米柱子和半米碎石显示相同砖块却尺寸悬殊，
    /// 长墙上的砖像木板。共享材质无法修复，因为材质平铺影响所有使用者。按各面真实尺寸缩放 UV 可以解决，
    /// 也让整个教堂共用三种材质。
    /// </para>
    /// <para>
    /// 每种不同尺寸保存一个资产，让场景引用而非嵌入副本，重新生成场景不会增加网格数量。
    /// </para>
    /// </remarks>
    public static class TiledMeshes
    {
        /// <summary>生成网格的保存文件夹。</summary>
        public const string MeshFolder = "Assets/_Project/Meshes";

        /// <summary>加载或创建指定尺寸、居中放置的盒体。</summary>
        /// <param name="size">宽、高、深，单位为米。</param>
        /// <param name="metresPerTile">一次纹理重复覆盖的米数。</param>
        /// <returns>网格资产。</returns>
        public static Mesh Box(Vector3 size, float metresPerTile)
        {
            string name = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "TiledBox_{0:0.##}x{1:0.##}x{2:0.##}_{3:0.##}", size.x, size.y, size.z, metresPerTile);
            string path = MeshFolder + "/" + name + ".asset";

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (existing != null)
            {
                return existing;
            }

            Mesh mesh = BuildBox(size, Mathf.Max(0.01f, metresPerTile));
            mesh.name = name;

            AssetAuthoring.EnsureFolderExists(MeshFolder);
            AssetDatabase.CreateAsset(mesh, path);

            return mesh;
        }

        /// <summary>
        /// 加载或创建尖窗上方的墙：底面为哥特式尖拱的墙板。
        /// </summary>
        /// <remarks>
        /// 布局类似 <see cref="Box"/> ，尺寸为（厚度、高度、跨度）且居中，可替换原平底块的位置。
        /// 矩形窗顶像仓库，尖拱是在战斗地板视角下辨识大教堂的关键。
        /// </remarks>
        /// <param name="thickness">沿 x 的墙厚，单位为米。</param>
        /// <param name="height">沿 y 从起拱线到顶面的墙板高度。</param>
        /// <param name="span">沿 z 的窗宽，单位为米。</param>
        /// <param name="rise">拱尖高于起拱线的高度，至少为跨度的一半。</param>
        /// <param name="metresPerTile">一次纹理重复覆盖的米数。</param>
        /// <returns>网格资产。</returns>
        public static Mesh PointedArchHead(float thickness, float height, float span, float rise, float metresPerTile)
        {
            string name = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "ArchHead_{0:0.##}x{1:0.##}x{2:0.##}_r{3:0.##}_{4:0.##}", thickness, height, span, rise, metresPerTile);
            string path = MeshFolder + "/" + name + ".asset";

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (existing != null)
            {
                return existing;
            }

            Mesh mesh = BuildPointedArchHead(thickness, height, span, rise, metresPerTile);
            mesh.name = name;

            AssetAuthoring.EnsureFolderExists(MeshFolder);
            AssetDatabase.CreateAsset(mesh, path);

            return mesh;
        }

        /// <summary>指定横向位置处，尖拱底面高于起拱线的高度。</summary>
        /// <remarks>
        /// 两个等半径圆弧，各自圆心位于中点另一侧，在尖端相交。半径按要求的拱高确定。
        /// </remarks>
        /// <param name="z">横跨跨度的位置，从负半跨度到正半跨度。</param>
        /// <param name="span">窗宽。</param>
        /// <param name="rise">拱尖高度。</param>
        /// <returns>底面高度。</returns>
        public static float PointedArchHeight(float z, float span, float rise)
        {
            float half = span * 0.5f;
            float radius = (rise * rise + half * half) / span;
            float centre = radius - half;
            float fromCentre = Mathf.Abs(z) + centre;

            return Mathf.Sqrt(Mathf.Max(0f, radius * radius - fromCentre * fromCentre));
        }

        /// <summary>构建拱顶但不保存，参见 <see cref="PointedArchHead"/>.</summary>
        /// <param name="thickness">沿 x 的墙厚，单位为米。</param>
        /// <param name="height">沿 y 从起拱线到顶面的墙板高度。</param>
        /// <param name="span">沿 z 的窗宽，单位为米。</param>
        /// <param name="rise">拱尖高于起拱线的高度。</param>
        /// <param name="metresPerTile">一次纹理重复覆盖的米数。</param>
        /// <returns>由调用者拥有的新网格。</returns>
        public static Mesh BuildPointedArchHead(float thickness, float height, float span, float rise, float metresPerTile)
        {
            rise = Mathf.Clamp(rise, span * 0.5f, height * 0.95f);
            float tile = Mathf.Max(0.01f, metresPerTile);
            const int Slices = 24;

            var vertices = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var triangles = new System.Collections.Generic.List<int>();

            float bottom = -height * 0.5f, top = height * 0.5f, halfT = thickness * 0.5f;

            Vector3 Underside(int i, float x)
            {
                float z = -span * 0.5f + span * i / Slices;
                return new Vector3(x, bottom + PointedArchHeight(z, span, rise), z);
            }

            // 墙的两个面：每段是从拱线延伸到顶面的四边形。
            foreach (float x in new[] { halfT, -halfT })
            {
                var outward = new Vector3(Mathf.Sign(x), 0f, 0f);

                for (int i = 0; i < Slices; i++)
                {
                    Vector3 a = Underside(i, x), b = Underside(i + 1, x);
                    AddQuad(a, b, new Vector3(x, top, b.z), new Vector3(x, top, a.z), outward,
                        p => new Vector2(p.z / tile, p.y / tile), vertices, uvs, triangles);
                }
            }

            // 拱底面，朝下进入开口并朝向开口中心。
            float along = 0f;

            for (int i = 0; i < Slices; i++)
            {
                Vector3 a = Underside(i, halfT), b = Underside(i + 1, halfT);
                float length = Vector3.Distance(a, b);
                Vector3 midpoint = (a + b) * 0.5f;
                Vector3 away = new Vector3(0f, bottom - 1f, 0f) - new Vector3(0f, midpoint.y, midpoint.z * 0.5f);
                float start = along;

                AddQuad(a, b, Underside(i + 1, -halfT), Underside(i, -halfT), away,
                    p => new Vector2(Mathf.Approximately(p.z, a.z) ? start / tile : (start + length) / tile, (p.x + halfT) / tile),
                    vertices, uvs, triangles);

                along += length;
            }

            // 顶面，以备高位摄像机俯视。
            AddQuad(new Vector3(halfT, top, -span * 0.5f), new Vector3(halfT, top, span * 0.5f),
                new Vector3(-halfT, top, span * 0.5f), new Vector3(-halfT, top, -span * 0.5f), Vector3.up,
                p => new Vector2(p.z / tile, p.x / tile), vertices, uvs, triangles);

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>添加独立顶点四边形，绕序使正面朝指定方向。</summary>
        private static void AddQuad(
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, System.Func<Vector3, Vector2> uv,
            System.Collections.Generic.List<Vector3> vertices, System.Collections.Generic.List<Vector2> uvs,
            System.Collections.Generic.List<int> triangles)
        {
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            uvs.Add(uv(a)); uvs.Add(uv(b)); uvs.Add(uv(c)); uvs.Add(uv(d));

            // Unity 正面从外部看为顺时针，此时 Cross(b - a, c - a) 指向外侧。
            bool outward = Vector3.Dot(Vector3.Cross(b - a, c - a), facing) > 0f;

            if (outward)
            {
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            else
            {
                triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }
        }

        private static Mesh BuildBox(Vector3 size, float tile)
        {
            Vector3 half = size * 0.5f;
            var vertices = new Vector3[24];
            var normals = new Vector3[24];
            var uvs = new Vector2[24];
            var triangles = new int[36];

            // 各面依次为：法线、纹理 u 方向轴（normal x v，使绕序从外侧可见），再是 v。
            AddFace(0, Vector3.right, Vector3.forward, Vector3.up, half, size.z, size.y, tile, vertices, normals, uvs, triangles);
            AddFace(1, Vector3.left, Vector3.back, Vector3.up, half, size.z, size.y, tile, vertices, normals, uvs, triangles);
            AddFace(2, Vector3.up, Vector3.right, Vector3.forward, half, size.x, size.z, tile, vertices, normals, uvs, triangles);
            AddFace(3, Vector3.down, Vector3.right, Vector3.back, half, size.x, size.z, tile, vertices, normals, uvs, triangles);
            AddFace(4, Vector3.forward, Vector3.left, Vector3.up, half, size.x, size.y, tile, vertices, normals, uvs, triangles);
            AddFace(5, Vector3.back, Vector3.right, Vector3.up, half, size.x, size.y, tile, vertices, normals, uvs, triangles);

            var mesh = new Mesh
            {
                vertices = vertices,
                normals = normals,
                uv = uvs,
                triangles = triangles
            };

            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            return mesh;
        }

        private static void AddFace(
            int face, Vector3 normal, Vector3 uAxis, Vector3 vAxis, Vector3 half,
            float uLength, float vLength, float tile,
            Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] triangles)
        {
            int v = face * 4;
            Vector3 centre = Vector3.Scale(normal, half);
            Vector3 u = Vector3.Scale(uAxis, half);
            Vector3 w = Vector3.Scale(vAxis, half);
            float uTiles = uLength / tile;
            float vTiles = vLength / tile;

            vertices[v] = centre - u - w;
            vertices[v + 1] = centre + u - w;
            vertices[v + 2] = centre + u + w;
            vertices[v + 3] = centre - u + w;

            uvs[v] = new Vector2(0f, 0f);
            uvs[v + 1] = new Vector2(uTiles, 0f);
            uvs[v + 2] = new Vector2(uTiles, vTiles);
            uvs[v + 3] = new Vector2(0f, vTiles);

            for (int i = 0; i < 4; i++)
            {
                normals[v + i] = normal;
            }

            // 从外侧看为顺时针，即 Unity 正面。
            int t = face * 6;
            triangles[t] = v;
            triangles[t + 1] = v + 2;
            triangles[t + 2] = v + 1;
            triangles[t + 3] = v;
            triangles[t + 4] = v + 3;
            triangles[t + 5] = v + 2;
        }
    }
}
