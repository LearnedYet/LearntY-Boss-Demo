using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Editor.Art
{
    /// <summary>
    /// 将双方的剑、骑士的盾牌和巨兽的砍刀构建为真实形体，并安装到骨骼上。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用拉长的盒子做武器最容易让角色显得像原型：从任何角度看都像木板。这里改用网格：
    /// 剑刃采用向尖端收窄的菱形截面，让光线沿脊线分开；鸢形盾像锻打金属一样弯曲。每件仅需数百个三角形。
    /// </para>
    /// <para>
    /// 安装方向从各自骨骼读取，不使用固定偏移，因为手骨轴向由模型作者决定：剑刃横过指关节，
    /// 盾牌贴在前臂外侧且尖端朝向手部。
    /// </para>
    /// </remarks>
    public static class ArmsBuilder
    {
        /// <summary>保存生成武器网格的文件夹。</summary>
        public const string MeshFolder = "Assets/_Project/Meshes/Arms";

        /// <summary>加载或创建从底部沿 +Z 延伸、逐渐收窄至尖端的双刃剑。</summary>
        /// <param name="length">剑刃从底部到尖端的长度，单位为米。</param>
        /// <param name="width">底部宽度。</param>
        /// <param name="thickness">中央脊线的厚度。</param>
        /// <param name="tipFraction">尖端占总长度的比例。</param>
        /// <returns>网格资产。</returns>
        public static Mesh Blade(float length, float width, float thickness, float tipFraction)
        {
            string name = Invariant($"Blade_{length:0.##}x{width:0.###}x{thickness:0.###}_{tipFraction:0.##}");

            return LoadOrSave(name, () =>
            {
                float tipStart = length * (1f - tipFraction);

                // 剑刃上的截面位置：底部为完整宽度，略微收窄到尖端起点，最后在尖端归零。
                var stations = new[]
                {
                    (z: 0f, halfWidth: width * 0.5f, halfThickness: thickness * 0.5f),
                    (z: tipStart, halfWidth: width * 0.44f, halfThickness: thickness * 0.45f),
                    (z: length, halfWidth: 0f, halfThickness: 0f)
                };

                var builder = new FacetedMesh(new Vector3(0f, 0f, length * 0.5f));

                for (int i = 0; i < stations.Length - 1; i++)
                {
                    Vector3[] a = Diamond(stations[i]);
                    Vector3[] b = Diamond(stations[i + 1]);

                    for (int k = 0; k < 4; k++)
                    {
                        builder.Quad(a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]);
                    }
                }

                Vector3[] baseRing = Diamond(stations[0]);
                builder.Quad(baseRing[0], baseRing[3], baseRing[2], baseRing[1]);

                return builder.Build();
            });
        }

        /// <summary>加载或创建位于 XY 平面、正面朝 +Z、尖端朝 -Y 的鸢形盾。</summary>
        /// <param name="width">顶部宽度。</param>
        /// <param name="height">从顶边到尖端的高度。</param>
        /// <param name="thickness">边缘厚度。</param>
        /// <param name="bulge">盾面中心向前凸出的距离。</param>
        /// <returns>网格资产。</returns>
        public static Mesh HeaterShield(float width, float height, float thickness, float bulge)
        {
            // “uv”：盾牌带有用于涂装正面的平面纹理坐标；早期网格没有此数据。
            string name = Invariant($"HeaterShield_{width:0.##}x{height:0.##}x{thickness:0.###}_{bulge:0.###}_uv");

            return LoadOrSave(name, () =>
            {
                List<Vector2> outline = HeaterOutline(width, height, 24);
                var builder = new FacetedMesh(Vector3.zero);

                var frontCentre = new Vector3(0f, 0f, thickness * 0.5f + bulge);
                var backCentre = new Vector3(0f, 0f, -thickness * 0.5f);

                for (int i = 0; i < outline.Count; i++)
                {
                    Vector2 p = outline[i];
                    Vector2 q = outline[(i + 1) % outline.Count];

                    var frontP = new Vector3(p.x, p.y, thickness * 0.5f);
                    var frontQ = new Vector3(q.x, q.y, thickness * 0.5f);
                    var backP = new Vector3(p.x, p.y, -thickness * 0.5f);
                    var backQ = new Vector3(q.x, q.y, -thickness * 0.5f);

                    builder.Triangle(frontCentre, frontP, frontQ);
                    builder.Triangle(backCentre, backQ, backP);
                    builder.Quad(frontP, backP, backQ, frontQ);
                }

                return builder.Build(planarUv: true);
            });
        }

        /// <summary>骑士盾牌宽度，单位为米。</summary>
        public const float ShieldWidth = 0.52f;

        /// <summary>骑士盾牌高度，单位为米。</summary>
        public const float ShieldHeight = 0.72f;

        /// <summary>
        /// 鸢形盾正面：磨损露木的涂装木板、铁边、盾心凸起和铆钉。
        /// </summary>
        /// <remarks>
        /// 没有纹理时，盾牌仅是一片纯色平面，在首领的光照下像绑在骑士手臂上的彩色卡片。
        /// 纹理按与网格相同的轮廓生成，让铁边跟随外沿；通过以下方法提供的平面坐标映射到盾牌的宽高：
        /// <see cref="HeaterShield"/>。
        /// </remarks>
        /// <param name="width">传给以下方法的盾牌宽度，单位为米： <see cref="HeaterShield"/>.</param>
        /// <param name="height">传给以下方法的盾牌高度，单位为米： <see cref="HeaterShield"/>.</param>
        /// <returns>纹理资产。</returns>
        public static Texture2D ShieldFace(float width, float height)
        {
            const int Width = 256, Height = 352;
            const int Planks = 5;
            string path = $"{MeshFolder}/ShieldFace.asset";

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            bool created = texture == null;

            if (created)
            {
                texture = new Texture2D(Width, Height, TextureFormat.RGBA32, true) { name = "ShieldFace" };
            }

            List<Vector2> outline = HeaterOutline(width, height, 24);
            var paint = new Color(0.34f, 0.07f, 0.05f);
            var wood = new Color(0.24f, 0.16f, 0.1f);
            var iron = new Color(0.17f, 0.16f, 0.16f);

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    float u = (x + 0.5f) / Width, v = (y + 0.5f) / Height;
                    var point = new Vector2((u - 0.5f) * width, (v - 0.5f) * height);
                    float edge = DistanceToOutline(point, outline);

                    // 木纹沿木板纵向延伸；磨损以噪声斑块形式露出漆层下的木材。
                    int plank = Mathf.Min(Planks - 1, (int)(u * Planks));
                    float grain = Mathf.PerlinNoise(u * 40f + plank * 7.3f, v * 3f);
                    float wear = Mathf.PerlinNoise(u * 6f + 3.1f, v * 6f + 9.7f);
                    Color colour = Color.Lerp(paint, wood, Mathf.Clamp01((wear - 0.52f) * 5f));
                    colour *= 0.8f + grain * 0.35f + (plank % 2) * 0.05f;

                    float seam = Mathf.Abs(u * Planks - Mathf.Round(u * Planks));
                    colour *= seam < 0.03f ? 0.35f : 1f;

                    float boss = Vector2.Distance(point, new Vector2(0f, height * 0.08f));
                    bool onRim = edge < 0.03f;
                    bool onBoss = boss < 0.065f;
                    bool rivet = edge > 0.03f && edge < 0.05f && Mathf.Repeat((point.x + point.y) * 30f, 1f) < 0.12f;

                    if (onRim || onBoss || rivet)
                    {
                        float pitting = Mathf.PerlinNoise(u * 60f, v * 60f);
                        colour = iron * (0.75f + pitting * 0.5f) * (onBoss ? 1f + (0.065f - boss) * 6f : 1f);
                    }

                    // 污垢向边缘逐渐加深，表现手部接触和打击磨损。
                    colour *= Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(edge / 0.12f));
                    colour.a = 1f;
                    texture.SetPixel(x, y, colour);
                }
            }

            texture.Apply(true);

            if (created)
            {
                AssetAuthoring.EnsureFolderExists(MeshFolder);
                AssetDatabase.CreateAsset(texture, path);
            }
            else
            {
                EditorUtility.SetDirty(texture);
            }

            return texture;
        }

        /// <summary>点到闭合轮廓最近边缘的距离。</summary>
        private static float DistanceToOutline(Vector2 point, List<Vector2> outline)
        {
            float nearest = float.MaxValue;

            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 a = outline[i], b = outline[(i + 1) % outline.Count];
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
                nearest = Mathf.Min(nearest, Vector2.Distance(point, a + ab * t));
            }

            return nearest;
        }

        /// <summary>
        /// 从骨骼自身的指骨确定武器在握拳中的姿态。
        /// </summary>
        /// <remarks>
        /// 拳中握剑时，剑刃横跨指关节，从小指侧伸过食指，因此这条线就是剑刃前向，剑刃平面沿手指展开。
        /// 武器沿 +Z构建，所以结果采用朝向旋转。没有指骨时保留手骨本身的方向。
        /// </remarks>
        /// <param name="rig">骨架的 Humanoid Animator。</param>
        /// <param name="right">右手时为 true。</param>
        /// <returns>该手握持武器时的世界旋转。</returns>
        public static Quaternion GripRotation(Animator rig, bool right)
        {
            Transform hand = rig.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            Transform index = rig.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
            Transform little = rig.GetBoneTransform(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
            Transform middle = rig.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);

            if (hand == null || index == null || little == null || middle == null)
            {
                return hand != null ? hand.rotation : Quaternion.identity;
            }

            Vector3 alongBlade = index.position - little.position;
            Vector3 alongFingers = middle.position - hand.position;

            return alongBlade.sqrMagnitude < 1e-6f || alongFingers.sqrMagnitude < 1e-6f
                ? hand.rotation
                : Quaternion.LookRotation(alongBlade.normalized, alongFingers.normalized);
        }

        /// <summary>将盾牌绑在左前臂外侧，尖端朝向手部。</summary>
        /// <param name="rig">骨架的 Humanoid Animator。</param>
        /// <param name="material">盾牌材质。</param>
        public static void MountShield(Animator rig, Material material)
        {
            Transform forearm = rig.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Transform hand = rig.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform chest = rig.GetBoneTransform(HumanBodyBones.Chest);

            if (chest == null)
            {
                chest = rig.GetBoneTransform(HumanBodyBones.Spine);
            }

            if (forearm == null || hand == null || chest == null)
            {
                return;
            }

            Vector3 along = (hand.position - forearm.position).normalized;
            Vector3 outward = Vector3.ProjectOnPlane(forearm.position - chest.position, along).normalized;
            float armLength = Vector3.Distance(forearm.position, hand.position);

            var shield = new GameObject("Shield");
            shield.transform.SetParent(forearm, true);
            shield.transform.position = forearm.position + along * (armLength * 0.55f) + outward * 0.07f;
            shield.transform.rotation = Quaternion.LookRotation(outward, -along);

            shield.AddComponent<MeshFilter>().sharedMesh = HeaterShield(ShieldWidth, ShieldHeight, 0.035f, 0.05f);
            shield.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>在骨架右手放置一把重砍刀。</summary>
        /// <param name="rig">骨架的 Humanoid Animator。</param>
        /// <param name="bladeMaterial">刀刃材质。</param>
        /// <param name="gripMaterial">握柄材质。</param>
        public static void MountCleaver(Animator rig, Material bladeMaterial, Material gripMaterial)
        {
            Transform hand = rig.GetBoneTransform(HumanBodyBones.RightHand);

            if (hand == null)
            {
                return;
            }

            var cleaver = new GameObject("Cleaver");
            cleaver.transform.SetParent(hand, false);
            cleaver.transform.localPosition = Vector3.zero;
            cleaver.transform.rotation = GripRotation(rig, right: true);

            // 抵消手部继承的缩放，使砍刀在任意骨架缩放下都保持按米计的尺寸。
            float scale = 1f / Mathf.Max(0.01f, hand.lossyScale.x);

            AddPart(cleaver.transform, "Grip", gripMaterial, Blade(0.42f, 0.05f, 0.05f, 0.02f), new Vector3(0f, 0f, -0.28f) * scale, scale);
            AddPart(cleaver.transform, "Guard", gripMaterial, Blade(0.12f, 0.4f, 0.08f, 0.02f), new Vector3(0f, 0f, 0.1f) * scale, scale);
            AddPart(cleaver.transform, "Blade", bladeMaterial, Blade(1.45f, 0.24f, 0.05f, 0.1f), new Vector3(0f, 0f, 0.18f) * scale, scale);
        }

        private static void AddPart(Transform parent, string name, Material material, Mesh mesh, Vector3 localPosition, float scale)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = Vector3.one * scale;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static Vector3[] Diamond((float z, float halfWidth, float halfThickness) station) => new[]
        {
            new Vector3(-station.halfWidth, 0f, station.z),
            new Vector3(0f, station.halfThickness, station.z),
            new Vector3(station.halfWidth, 0f, station.z),
            new Vector3(0f, -station.halfThickness, station.z)
        };

        /// <summary>鸢形盾轮廓：平直顶边、向外鼓起再向内收尖的两侧。</summary>
        private static List<Vector2> HeaterOutline(float width, float height, int segmentsPerSide)
        {
            float halfWidth = width * 0.5f;
            float top = height * 0.5f;
            float shoulder = height * 0.1f;
            var points = new List<Vector2> { new Vector2(-halfWidth, top), new Vector2(halfWidth, top) };

            for (int i = 0; i <= segmentsPerSide; i++)
            {
                float t = i / (float)segmentsPerSide;
                points.Add(new Vector2(halfWidth * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t)), Mathf.Lerp(shoulder, -top, t)));
            }

            for (int i = segmentsPerSide - 1; i >= 0; i--)
            {
                float t = i / (float)segmentsPerSide;
                points.Add(new Vector2(-halfWidth * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t)), Mathf.Lerp(shoulder, -top, t)));
            }

            return points;
        }

        private static Mesh LoadOrSave(string name, System.Func<Mesh> build)
        {
            string path = $"{MeshFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (existing != null)
            {
                return existing;
            }

            Mesh mesh = build();
            mesh.name = name;
            AssetAuthoring.EnsureFolderExists(MeshFolder);
            AssetDatabase.CreateAsset(mesh, path);

            return mesh;
        }

        private static string Invariant(System.FormattableString text) => System.FormattableString.Invariant(text);

        /// <summary>
        /// 收集平面着色面，每个面的绕序都使其背离实体内部的一点。
        /// </summary>
        /// <remarks>
        /// 面绕序根据内部参考点逐面决定，而不是依赖顶点顺序，因此可以按生成时最自然的顺序添加面，
        /// 同时保证从外部可见。Unity 正面从外部看为顺时针绕序，此时以下向量指向外部：
        /// 计算式为 <c>Cross(b - a, c - a)</c>。
        /// </remarks>
        private sealed class FacetedMesh
        {
            private readonly Vector3 _inside;
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<int> _triangles = new List<int>();

            public FacetedMesh(Vector3 inside)
            {
                _inside = inside;
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Triangle(a, b, c);
                Triangle(a, c, d);
            }

            public void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 normal = Vector3.Cross(b - a, c - a);

                if (normal.sqrMagnitude < 1e-12f)
                {
                    return;
                }

                bool alreadyOutward = Vector3.Dot(normal, (a + b + c) / 3f - _inside) > 0f;
                int start = _vertices.Count;

                _vertices.Add(a);
                _vertices.Add(alreadyOutward ? b : c);
                _vertices.Add(alreadyOutward ? c : b);
                _triangles.Add(start);
                _triangles.Add(start + 1);
                _triangles.Add(start + 2);
            }

            /// <param name="planarUv">
            /// 是否为网格生成覆盖其宽高的纹理坐标，适合正面观察的平面，例如盾面。
            /// </param>
            public Mesh Build(bool planarUv = false)
            {
                var mesh = new Mesh { vertices = _vertices.ToArray(), triangles = _triangles.ToArray() };
                mesh.RecalculateBounds();

                if (planarUv)
                {
                    Bounds bounds = mesh.bounds;
                    var uvs = new Vector2[_vertices.Count];

                    for (int i = 0; i < uvs.Length; i++)
                    {
                        uvs[i] = new Vector2(
                            Mathf.InverseLerp(bounds.min.x, bounds.max.x, _vertices[i].x),
                            Mathf.InverseLerp(bounds.min.y, bounds.max.y, _vertices[i].y));
                    }

                    mesh.uv = uvs;
                }

                mesh.RecalculateNormals();

                if (planarUv)
                {
                    mesh.RecalculateTangents();
                }

                return mesh;
            }
        }
    }
}
