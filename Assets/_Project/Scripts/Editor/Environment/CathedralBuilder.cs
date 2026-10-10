using System.Linq;
using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Combat.Feel;
using AdaptiveBossArena.Core.Services;
using AdaptiveBossArena.Game;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Editor.Environment
{
    /// <summary>
    /// 构建战斗所在的废墟大教堂。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 战斗发生在破损中殿的交叉部：八根柱子围绕地板组成八边形，仍站立的柱子之间有过梁；外墙有高窗，
    /// 侧廊屋顶部分坍塌，交叉部向天空开放，使阳光成束照到地板。
    /// </para>
    /// <para>
    /// 这里不改变战斗。可玩半径、碰撞墙环和层保持竞技场构建器原样；教堂部件位于墙环外或平铺其下，
    /// 不带碰撞体。墙环自身盒体保持有效，仅用纹理网格遮住，AI 站位所依据的边界不变。
    /// </para>
    /// <para>
    /// 全部根据常量和固定种子摆放，理由与战斗不使用以下随机源相同：<c>UnityEngine.Random</c>：
    /// 两次重新生成必须产生相同场景。
    /// </para>
    /// </remarks>
    public static class CathedralBuilder
    {
        /// <summary>测试要查找的根对象名称。</summary>
        public const string RootName = "Cathedral";

        /// <summary>太阳旋转，与竞技场方向光共用，使光柱对齐。</summary>
        public static readonly Quaternion SunRotation = Quaternion.Euler(58f, -35f, 0f);

        private const uint LayoutSeed = 20260914u;

        private const int ColumnCount = 8;
        private const float ColumnRingOffset = 3.5f;
        private const float ColumnHeight = 14f;
        private const float ColumnWidth = 1.8f;
        private const float OuterWallApothemOffset = 12f;
        private const float OuterWallHeight = 18f;
        private const float OuterWallThickness = 1.5f;
        private const float WindowSill = 5f;
        /// <summary>窗户尖拱从竖直侧边开始的位置。</summary>
        private const float WindowSpring = 9.5f;

        /// <summary>拱顶相对起拱线的高度，与窗宽配合形成陡峭尖拱。</summary>
        private const float WindowArchRise = 0.75f;
        private const int DebrisCount = 34;
        private const int StainCount = 16;

        private const float BannerWidth = 1.5f;
        private const float BannerHeight = 4.8f;

        /// <summary>旗帜顶边高度，足够低以悬挂在墙环上方的摄像机视野内。</summary>
        private const float BannerTop = 7.6f;

        /// <summary>布料悬挂在柱面前方的距离。</summary>
        private const float BannerStandOff = 0.06f;

        /// <summary>地板污渍高于石面的高度：避免重叠，并低于 0.02 的伤痕层。</summary>
        private const float StainLift = 0.006f;
        private const int ShaftCount = 5;
        private const float ParapetCrownHeight = 0.9f;
        private const int ParapetCrownPieces = 5;
        private const int ColumnPieces = 10;

        /// <summary>屋顶阴影任何位置允许透过的最低阳光比例。</summary>
        public const float MinimumSun = 0.5f;

        /// <summary>
        /// 相比冲击自身范围，柱子允许从更远处被打碎的距离：柱子位于墙环后三米半，扣除余量，
        /// 确保只有正对柱前方的打击才计入。
        /// </summary>
        private const float ColumnReachBonus = 3.2f;

        /// <summary>活动碎片上限：计划中 WebGL 物理预算为 40 个刚体，桌面放宽为 150 个。</summary>
        private const int WebDebrisCapacity = 40;

        private const int DesktopDebrisCapacity = 150;

        private static readonly Color StoneTint = new Color(0.78f, 0.76f, 0.74f);
        private static readonly Color BrickTint = new Color(0.72f, 0.70f, 0.70f);
        private static readonly Color BlockTint = new Color(0.82f, 0.80f, 0.78f);
        private static readonly Color CandleFlame = new Color(3.2f, 1.6f, 0.55f);
        private static readonly Color CandleLight = new Color(1f, 0.66f, 0.34f);
        private static readonly Color ShaftColour = new Color(1f, 0.93f, 0.78f, 0.11f);

        /// <summary>低于桌面光柱强度的一半；浏览器把相同叠加平面渲染得亮得多。</summary>
        private static readonly Color WebShaftColour = new Color(1f, 0.93f, 0.78f, 0.045f);
        private static readonly Color DustColour = new Color(1f, 0.95f, 0.85f, 0.5f);

        private static Material _floor;
        private static Material _brick;
        private static Material _block;

        /// <summary>第 2 和第 5 根柱子已倒。只有两根且不相邻，让柱环仍能读作环形。</summary>
        private static bool IsCollapsed(int column) => column == 2 || column == 5;

        /// <summary>上层墙体和对应屋顶区间已坍塌的外墙面。</summary>
        private static bool IsRuinedBay(int face) => face == 1 || face == 4 || face == 6;

        /// <summary>围绕竞技场构建大教堂。</summary>
        /// <param name="config">竞技场尺寸。</param>
        /// <param name="arenaRoot">已存在地板和墙环的竞技场根对象。</param>
        public static void Build(ArenaConfig config, Transform arenaRoot)
        {
            _floor = MaterialLibrary.GetOrCreateTexturedLit("CathedralFloor", "stone_tiles_02", StoneTint);
            _brick = MaterialLibrary.GetOrCreateTexturedLit("CathedralBrick", "castle_brick_07", BrickTint);
            _block = MaterialLibrary.GetOrCreateTexturedLit("CathedralBlock", "medieval_blocks_02", BlockTint);

            var root = new GameObject(RootName);
            root.transform.SetParent(arenaRoot, false);

            float radius = config.Radius;

            BuildFloor(root.transform, arenaRoot, radius);
            BuildFloorStains(root.transform, radius);
            ReskinWallRing(root.transform, arenaRoot);
            BuildColumns(root.transform, radius);
            BuildOuterWalls(root.transform, radius);
            BuildDebris(root.transform, radius);
            BuildShafts(root.transform);
            BuildDust(root.transform, radius);

            // 网页构建会减少尘埃和蜡烛，将它们交给 BuildShafts刚在教堂旁添加的平台照明组件。
            if (root.transform.parent.TryGetComponent(out PlatformLighting lighting))
            {
                Transform dust = root.transform.Find("Dust");
                Light[] candles = root.GetComponentsInChildren<Light>(true)
                    .Where(light => light.type == LightType.Point)
                    .ToArray();

                lighting.BindSceneBudget(dust != null ? dust.GetComponent<ParticleSystem>() : null, candles);
            }

            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                // 静态批处理在构建中将整个废墟合并为少量绘制调用，
                // 让百余块石材保持在 WebGL draw-call 预算内。
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticEditorFlags.BatchingStatic);
            }
        }

        /// <summary>
        /// 在中殿铺设平铺石地板，并隐藏平坦占位圆盘。
        /// </summary>
        /// <remarks>
        /// 圆盘 UV 把一张纹理铺满整个 32 米地板，会让每块石板像汽车一样大。其碰撞体是独立对象，
        /// 保持原样。
        /// </remarks>
        private static void BuildFloor(Transform root, Transform arenaRoot, float radius)
        {
            Transform disc = arenaRoot.Find("Floor");

            if (disc != null && disc.TryGetComponent(out MeshRenderer discRenderer))
            {
                discRenderer.enabled = false;
            }

            float span = (radius + OuterWallApothemOffset + OuterWallThickness) * 2f + 4f;
            Block(root, "NaveFloor", new Vector3(span, 0.3f, span), 2.5f, _floor,
                new Vector3(0f, -0.15f, 0f), Quaternion.identity);
        }

        /// <summary>
        /// 中殿旧污渍，以及地板与墙环交界处积聚的污垢。
        /// </summary>
        /// <remarks>
        /// 地板原本是均匀光照下的干净平铺平面，既占屏幕最大面积，也最明显像程序生成。
        /// 使用种子保证每次重建相同污渍；平坦、无碰撞体的四边形略高于石面，低于上方绘制的伤痕和警告。
        /// </remarks>
        private static void BuildFloorStains(Transform root, float radius)
        {
            var stains = new GameObject("FloorStains").transform;
            stains.SetParent(root, false);

            Material stain = GetOrCreateStain("FloorStain", StainTexture(), new Color(0.05f, 0.035f, 0.03f, 0.6f));
            Material grime = GetOrCreateStain("FloorGrime", GrimeRingTexture(), new Color(0.02f, 0.02f, 0.025f, 0.75f));
            var random = new XorShiftRandomProvider(LayoutSeed + 2u);
            Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

            for (int i = 0; i < StainCount; i++)
            {
                float distance = Mathf.Sqrt(random.NextFloat(0f, 1f)) * (radius - 1f);
                float size = random.NextFloat(1.4f, 4.2f);

                GameObject patch = Place(stains, $"Stain_{i:D2}", quad, stain,
                    OnRing(random.NextFloat(0f, 360f), distance) + Vector3.up * (StainLift + i * 0.0005f),
                    Quaternion.Euler(90f, random.NextFloat(0f, 360f), 0f));
                patch.transform.localScale = new Vector3(size, size * random.NextFloat(0.6f, 1f), 1f);
                patch.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // 覆盖整块地板的四边形，纹理中间透明、向墙环逐渐变暗。
            GameObject ring = Place(stains, "WallGrime", quad, grime, Vector3.up * (StainLift * 0.5f), Quaternion.Euler(90f, 0f, 0f));
            ring.transform.localScale = new Vector3((radius + 1f) * 2f, (radius + 1f) * 2f, 1f);
            ring.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>不规则斑块：叠层噪声经柔和径向衰减裁切，中心更暗。</summary>
        private static Texture2D StainTexture() => GetOrCreateTexture("FloorStainBlotch", 256, 256, (u, v) =>
        {
            float x = u * 2f - 1f, y = v * 2f - 1f;
            float r = Mathf.Sqrt(x * x + y * y);
            float noise = Mathf.PerlinNoise(u * 5.3f + 11.7f, v * 5.3f + 3.1f) * 0.65f + Mathf.PerlinNoise(u * 17f + 2.3f, v * 17f + 8.9f) * 0.35f;
            float shape = 1f - Edge(0.35f, 0.95f, r + (noise - 0.5f) * 0.7f);
            return Mathf.Clamp01(shape * (0.55f + noise * 0.6f));
        });

        /// <summary>战斗地板保持透明，在墙环处聚集成暗带。</summary>
        private static Texture2D GrimeRingTexture() => GetOrCreateTexture("FloorGrimeRing", 512, 512, (u, v) =>
        {
            float x = u * 2f - 1f, y = v * 2f - 1f;
            float r = Mathf.Sqrt(x * x + y * y);
            float noise = Mathf.PerlinNoise(u * 9f + 4.4f, v * 9f + 1.3f);
            return Mathf.Clamp01(Edge(0.62f, 0.97f, r + (noise - 0.5f) * 0.12f) * (0.7f + noise * 0.3f));
        });

        private static Material GetOrCreateStain(string materialName, Texture2D texture, Color colour)
        {
            string path = EditorMenus.GeneratedMaterialFolder + "/" + materialName + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

            if (material == null)
            {
                material = new Material(shader) { name = materialName };
                AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedMaterialFolder);
                AssetDatabase.CreateAsset(material, path);
            }

            // Alpha 混合表现物质而非光：污渍加深光照后的石材颜色。
            material.shader = shader;
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");

            // 先于其他透明物绘制，让伤痕和警告始终位于污渍上方。
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 50;
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", colour);
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>
        /// 用砖材遮住碰撞墙环灰盒，保持所有碰撞体位置。
        /// </summary>
        /// <remarks>
        /// 用独立对象替换可见盒体，而不原地修改；原对象缩放决定碰撞体尺寸，平铺网格则要求缩放为一。
        /// </remarks>
        private static void ReskinWallRing(Transform root, Transform arenaRoot)
        {
            Transform walls = arenaRoot.Find("Walls");

            if (walls == null)
            {
                return;
            }

            var parapet = new GameObject("Parapet").transform;
            parapet.SetParent(root, false);

            foreach (Transform wall in walls)
            {
                if (wall.TryGetComponent(out MeshRenderer renderer))
                {
                    renderer.enabled = false;
                }

                BuildBreakableParapet(parapet, wall);
            }

            // 放在教堂旁而非内部：池化碎片带碰撞体，而装饰要求不带。
            // 碎片位于战斗角色不接触的 debris 层。
            var destruction = new GameObject("Destruction");
            destruction.transform.SetParent(arenaRoot, false);

            DebrisPool pool = destruction.AddComponent<DebrisPool>();
            pool.Bind(_brick, WebDebrisCapacity, DesktopDebrisCapacity);
            destruction.AddComponent<DestructibleField>().Bind(pool);
        }

        /// <summary>
        /// 一段护墙：下方实心底座，上方石冠可被砸地或撞墙身体击落。两者都是覆盖在不变碰撞体上的装饰。
        /// </summary>
        private static void BuildBreakableParapet(Transform parent, Transform wall)
        {
            Vector3 size = wall.localScale;
            float baseHeight = Mathf.Max(0.5f, size.y - ParapetCrownHeight);

            Block(parent, wall.name + "_Stone", new Vector3(size.x, baseHeight, size.z), 2f, _brick,
                wall.position + Vector3.down * (ParapetCrownHeight * 0.5f), wall.rotation);

            var crownSize = new Vector3(size.x, ParapetCrownHeight, size.z);
            GameObject crown = Block(parent, wall.name + "_Crown", crownSize, 2f, _brick,
                wall.position + Vector3.up * (baseHeight * 0.5f), wall.rotation);

            crown.AddComponent<Destructible>().Bind(new Renderer[] { crown.GetComponent<MeshRenderer>() }, crownSize, ParapetCrownPieces);
        }

        private static void BuildColumns(Transform root, float radius)
        {
            var columns = new GameObject("Columns").transform;
            columns.SetParent(root, false);

            float ringRadius = radius + ColumnRingOffset;
            float step = 360f / ColumnCount;
            var random = new XorShiftRandomProvider(LayoutSeed);

            for (int i = 0; i < ColumnCount; i++)
            {
                float angle = step * 0.5f + step * i;
                Vector3 foot = OnRing(angle, ringRadius);
                Quaternion facing = Quaternion.Euler(0f, -angle, 0f);

                Block(columns, $"Plinth_{i}", new Vector3(2.6f, 1f, 2.6f), 1.5f, _block, foot + Vector3.up * 0.5f, facing);

                if (IsCollapsed(i))
                {
                    BuildFallenColumn(columns, i, foot, angle, random);
                    continue;
                }

                var shaftSize = new Vector3(ColumnWidth, ColumnHeight, ColumnWidth);
                GameObject shaft = Block(columns, $"Shaft_{i}", shaftSize, 1.5f, _block,
                    foot + Vector3.up * (1f + ColumnHeight * 0.5f), facing);
                GameObject capital = Block(columns, $"Capital_{i}", new Vector3(2.4f, 0.8f, 2.4f), 1.5f, _block,
                    foot + Vector3.up * (1.4f + ColumnHeight), facing);

                // 柱基保留；柱子倒塌后留下基座，与已有两根倒柱一致。
                shaft.AddComponent<Destructible>().Bind(
                    new Renderer[] { shaft.GetComponent<MeshRenderer>(), capital.GetComponent<MeshRenderer>() },
                    shaftSize, ColumnPieces, ColumnReachBonus);

                if (i % 2 == 0)
                {
                    BuildCandles(columns, i, foot, angle);
                }

                // 通往下一根柱子的过梁，仅在两柱都站立支撑它时存在。
                int next = (i + 1) % ColumnCount;

                if (!IsCollapsed(next))
                {
                    float midAngle = angle + step * 0.5f;
                    float chord = 2f * ringRadius * Mathf.Sin(step * 0.5f * Mathf.Deg2Rad);
                    Vector3 middle = OnRing(midAngle, ringRadius * Mathf.Cos(step * 0.5f * Mathf.Deg2Rad));

                    Block(columns, $"Lintel_{i}", new Vector3(1.4f, 1.6f, chord), 1.5f, _block,
                        middle + Vector3.up * (2.6f + ColumnHeight), Quaternion.Euler(0f, -midAngle, 0f));
                }
            }

            BuildBanners(columns, ringRadius);
            GiveColumnsTheirLintels(columns);
        }

        /// <summary>
        /// 将每根站立柱子两侧的过梁加入柱子破碎时消失的部件。
        /// </summary>
        /// <remarks>
        /// 过梁由两柱支撑，若仍跨向已倒柱就会悬空。在所有柱和过梁创建后执行，
        /// 因为左侧过梁在上轮循环生成，右侧则在本轮之后生成。
        /// </remarks>
        private static void GiveColumnsTheirLintels(Transform columns)
        {
            for (int i = 0; i < ColumnCount; i++)
            {
                Transform shaft = columns.Find($"Shaft_{i}");

                if (shaft == null || !shaft.TryGetComponent(out Destructible destructible))
                {
                    continue;
                }

                var parts = new System.Collections.Generic.List<Renderer>
                {
                    shaft.GetComponent<MeshRenderer>(),
                    columns.Find($"Capital_{i}").GetComponent<MeshRenderer>()
                };

                foreach (string lintel in new[] { $"Lintel_{i}", $"Lintel_{(i + ColumnCount - 1) % ColumnCount}", $"Banner_{i}" })
                {
                    Transform found = columns.Find(lintel);

                    if (found != null)
                    {
                        parts.Add(found.GetComponent<MeshRenderer>());
                    }
                }

                destructible.Bind(parts.ToArray(), new Vector3(ColumnWidth, ColumnHeight, ColumnWidth), ColumnPieces, ColumnReachBonus);
            }
        }

        /// <summary>
        /// 断桩、向外倒离战斗地板的柱身，以及散落旁边的过梁碎片。
        /// </summary>
        private static void BuildFallenColumn(
            Transform parent, int index, Vector3 foot, float angle, XorShiftRandomProvider random)
        {
            const float Length = 9f;

            float stump = index == 2 ? 3.2f : 5.4f;
            Quaternion facing = Quaternion.Euler(0f, -angle, 0f);

            Block(parent, $"Stump_{index}", new Vector3(ColumnWidth, stump, ColumnWidth), 1.5f, _block,
                foot + Vector3.up * (1f + stump * 0.5f), facing * Quaternion.Euler(0f, 0f, 3f));

            Vector3 outward = OnRing(angle, 1f);
            Vector3 across = Vector3.Cross(Vector3.up, outward);
            Vector3 centre = foot + outward * (Length * 0.5f + 1.2f) + across * 1.1f + Vector3.up * 1.1f;
            Quaternion lying = Quaternion.LookRotation(outward + across * 0.25f, Vector3.up) * Quaternion.Euler(6f, 0f, 0f);

            Block(parent, $"FallenShaft_{index}", new Vector3(ColumnWidth, ColumnWidth, Length), 1.5f, _block, centre, lying);

            for (int piece = 0; piece < 3; piece++)
            {
                float size = random.NextFloat(0.9f, 1.5f);
                Vector3 at = foot + outward * random.NextFloat(1.8f, 4f) - across * random.NextFloat(1f, 3f);

                Block(parent, $"LintelPiece_{index}_{piece}", new Vector3(1.4f, 1.6f, size * 2.4f), 1.5f, _block,
                    at + Vector3.up * 0.75f,
                    Quaternion.Euler(random.NextFloat(-12f, 12f), random.NextFloat(0f, 360f), random.NextFloat(-8f, 8f)));
            }
        }

        /// <summary>柱脚的蜡烛，是建筑中剩下的唯一暖光。</summary>
        private static void BuildCandles(Transform parent, int index, Vector3 foot, float angle)
        {
            Material wax = MaterialLibrary.GetOrCreateSurface("CandleWax", new Color(0.86f, 0.82f, 0.70f), 0f, 0.3f);
            Material flame = MaterialLibrary.GetOrCreateSurface("CandleFlame", Color.black, 0f, 0f, CandleFlame);

            // 放在朝向战斗的一侧，让光照到地板而非柱后。
            Vector3 front = foot - OnRing(angle, 1.8f);
            float[] heights = { 0.45f, 0.3f, 0.6f };

            for (int c = 0; c < heights.Length; c++)
            {
                Vector3 at = front + Quaternion.Euler(0f, c * 120f + angle, 0f) * new Vector3(0.22f, 0f, 0f);

                Block(parent, $"Candle_{index}_{c}", new Vector3(0.09f, heights[c], 0.09f), 1f, wax,
                    at + Vector3.up * (heights[c] * 0.5f), Quaternion.identity);
                Block(parent, $"Flame_{index}_{c}", new Vector3(0.05f, 0.09f, 0.05f), 1f, flame,
                    at + Vector3.up * (heights[c] + 0.05f), Quaternion.identity);
            }

            var lightObject = new GameObject($"CandleLight_{index}");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.position = front + Vector3.up * 0.8f;

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = CandleLight;
            light.intensity = 2.2f;
            light.range = 8f;
            light.shadows = LightShadows.None;
        }

        /// <summary>
        /// 八边形外墙：每面两墩，中间高窗，侧廊屋顶向内延伸到柱环。破损区只保留较低的墩，移除其余部分。
        /// </summary>
        private static void BuildOuterWalls(Transform root, float radius)
        {
            var walls = new GameObject("OuterWalls").transform;
            walls.SetParent(root, false);

            float apothem = radius + OuterWallApothemOffset;
            float faceWidth = 2f * apothem * Mathf.Tan(Mathf.PI / ColumnCount);
            float pier = faceWidth * 0.34f;
            float window = faceWidth - pier * 2f;
            float step = 360f / ColumnCount;

            for (int face = 0; face < ColumnCount; face++)
            {
                float angle = step * face;
                bool ruined = IsRuinedBay(face);
                Quaternion facing = Quaternion.Euler(0f, -angle, 0f);
                Vector3 centre = OnRing(angle, apothem + OuterWallThickness * 0.5f);
                Vector3 along = facing * Vector3.forward;
                float pierHeight = ruined ? OuterWallHeight * 0.62f : OuterWallHeight;

                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 at = centre + along * (side * (window + pier) * 0.5f);

                    Block(walls, $"Pier_{face}_{(side < 0 ? "L" : "R")}",
                        new Vector3(OuterWallThickness, pierHeight, pier + 0.05f), 2f, _brick,
                        at + Vector3.up * (pierHeight * 0.5f), facing);
                }

                Block(walls, $"Sill_{face}", new Vector3(OuterWallThickness, WindowSill, window), 2f, _brick,
                    centre + Vector3.up * (WindowSill * 0.5f), facing);

                if (ruined)
                {
                    continue;
                }

                float headHeight = OuterWallHeight - WindowSpring;

                // 窗上使用尖拱而非平过梁；矩形开口像仓库，尖拱是在战斗地板视角下辨认大教堂的关键。
                Place(walls, $"Head_{face}",
                    TiledMeshes.PointedArchHead(OuterWallThickness, headHeight, window, window * WindowArchRise, 2f),
                    _brick, centre + Vector3.up * (WindowSpring + headHeight * 0.5f), facing);

                float bayDepth = apothem - (radius + ColumnRingOffset) + 1f;
                Vector3 bayCentre = OnRing(angle, apothem - bayDepth * 0.5f);

                Block(walls, $"AisleRoof_{face}", new Vector3(bayDepth, 0.6f, faceWidth), 2f, _block,
                    bayCentre + Vector3.up * (OuterWallHeight + 0.3f), facing);
            }
        }

        /// <summary>
        /// 站立柱朝向战斗的一侧悬挂腐朽旗帜。
        /// </summary>
        /// <remarks>
        /// 墙环与屋顶之间原先只有裸石。在靠近、受光柱照明且始终在视野内的柱子上，
        /// 破损褪色褶皱布料讲述旧主和久远历史。挂到柱子下成为可破碎部件，随柱倒下。使用裁切而非混合，
        /// 使排序和阴影与石材一致。
        /// </remarks>
        private static void BuildBanners(Transform columns, float ringRadius)
        {
            float step = 360f / ColumnCount;
            Material cloth = GetOrCreateBannerMaterial();
            Mesh mesh = BannerMesh();

            for (int i = 0; i < ColumnCount; i++)
            {
                if (IsCollapsed(i))
                {
                    continue;
                }

                float angle = step * 0.5f + step * i;
                Vector3 inward = -OnRing(angle, 1f);
                Vector3 hang = OnRing(angle, ringRadius) + inward * (ColumnWidth * 0.5f + BannerStandOff)
                    + Vector3.up * (BannerTop - BannerHeight * 0.5f);

                GameObject banner = Place(columns, $"Banner_{i}", mesh, cloth, hang, Quaternion.LookRotation(-inward, Vector3.up));
                banner.transform.localScale = new Vector3(BannerWidth, BannerHeight, 1f);
            }
        }

        /// <summary>旗布：单位尺寸网格，纵向带浅褶皱。</summary>
        private static Mesh BannerMesh()
        {
            string path = TiledMeshes.MeshFolder + "/BannerCloth.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (existing != null)
            {
                return existing;
            }

            const int Columns = 6, Rows = 14;
            var vertices = new Vector3[(Columns + 1) * (Rows + 1)];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[Columns * Rows * 6];

            for (int row = 0; row <= Rows; row++)
            {
                for (int column = 0; column <= Columns; column++)
                {
                    float u = column / (float)Columns, v = row / (float)Rows;

                    // 褶皱朝自由底边加深，那里布料最松。
                    float fold = Mathf.Sin(u * Mathf.PI * 3f) * 0.05f * (1.2f - v);
                    vertices[row * (Columns + 1) + column] = new Vector3(u - 0.5f, v - 0.5f, fold);
                    uvs[row * (Columns + 1) + column] = new Vector2(u, v);
                }
            }

            int t = 0;

            for (int row = 0; row < Rows; row++)
            {
                for (int column = 0; column < Columns; column++)
                {
                    int a = row * (Columns + 1) + column, b = a + 1, c = a + Columns + 1, d = c + 1;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                }
            }

            var mesh = new Mesh { name = "BannerCloth", vertices = vertices, uv = uvs, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            AssetAuthoring.EnsureFolderExists(TiledMeshes.MeshFolder);
            AssetDatabase.CreateAsset(mesh, path);

            return mesh;
        }

        /// <summary>
        /// 褪色深红布，带骨白符号、褶皱、向下摆加深的污垢和撕裂边缘。
        /// </summary>
        private static Texture2D BannerTexture()
        {
            const int Width = 128, Height = 384;
            string path = EditorMenus.GeneratedMaterialFolder + "/BannerCloth.asset";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            bool created = texture == null;

            if (created)
            {
                texture = new Texture2D(Width, Height, TextureFormat.RGBA32, true) { name = "BannerCloth" };
            }

            texture.wrapMode = TextureWrapMode.Clamp;

            var crimson = new Color(0.46f, 0.07f, 0.055f);
            var bone = new Color(0.78f, 0.7f, 0.56f);

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    float u = (x + 0.5f) / Width, v = (y + 0.5f) / Height;

                    float weave = Mathf.PerlinNoise(u * 50f, v * 140f);
                    float fade = Mathf.PerlinNoise(u * 4f + 5.5f, v * 5f + 1.7f);
                    Color colour = crimson * (0.7f + weave * 0.3f) * (0.75f + fade * 0.45f);

                    // 上三分之一处，环内有向下的剑；立起这些旗帜的人早已消失。
                    float cx = (u - 0.5f) * 2f, cy = (v - 0.7f) * 2f * (Height / (float)Width) * 0.5f;
                    float ring = Mathf.Abs(Mathf.Sqrt(cx * cx + cy * cy) - 0.55f);
                    bool blade = Mathf.Abs(cx) < 0.05f && cy > -0.75f && cy < 0.6f;
                    bool guard = Mathf.Abs(cy - 0.3f) < 0.05f && Mathf.Abs(cx) < 0.3f;

                    if (ring < 0.045f || blade || guard)
                    {
                        colour = Color.Lerp(colour, bone * (0.8f + fade * 0.3f), 0.75f);
                    }

                    // 从下摆向上蔓延的污垢。
                    colour *= Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(v * 2.2f));

                    // 下摆撕成参差布条，上部还蚀穿几个孔。
                    float tear = 0.12f + Mathf.PerlinNoise(u * 9f, 3.3f) * 0.22f + Mathf.Abs(Mathf.Sin(u * Mathf.PI * 2.5f)) * 0.08f;
                    bool hole = Mathf.PerlinNoise(u * 11f + 20f, v * 9f + 40f) > 0.8f && v < 0.6f;
                    float alpha = v < tear || hole ? 0f : 1f;

                    colour.a = alpha;
                    texture.SetPixel(x, y, colour);
                }
            }

            texture.Apply(true);

            // 原地重写，让布料调参作用于已有资产。
            if (created)
            {
                AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedMaterialFolder);
                AssetDatabase.CreateAsset(texture, path);
            }
            else
            {
                EditorUtility.SetDirty(texture);
            }

            return texture;
        }

        private static Material GetOrCreateBannerMaterial()
        {
            string path = EditorMenus.GeneratedMaterialFolder + "/BannerCloth.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");

            if (material == null)
            {
                material = new Material(shader) { name = "BannerCloth" };
                AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedMaterialFolder);
                AssetDatabase.CreateAsset(material, path);
            }

            // 裁切并双面可见：布料自由悬挂，撕裂下摆必须显示为孔洞。
            material.shader = shader;
            material.SetTexture("_BaseMap", BannerTexture());
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.06f);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>柱环与外墙之间散落石块，不放在战斗地板上。</summary>
        private static void BuildDebris(Transform root, float radius)
        {
            var debris = new GameObject("Debris").transform;
            debris.SetParent(root, false);

            var random = new XorShiftRandomProvider(LayoutSeed + 1u);

            for (int i = 0; i < DebrisCount; i++)
            {
                float angle = random.NextFloat(0f, 360f);
                float distance = radius + random.NextFloat(1.2f, OuterWallApothemOffset - 1f);
                float size = random.NextFloat(0.3f, 1.3f);
                var dimensions = new Vector3(size * random.NextFloat(0.7f, 1.6f), size, size * random.NextFloat(0.7f, 1.4f));

                Block(debris, $"Stone_{i:D2}", dimensions, 1.5f, i % 3 == 0 ? _brick : _block,
                    OnRing(angle, distance) + Vector3.up * (size * 0.35f),
                    Quaternion.Euler(random.NextFloat(-25f, 25f), random.NextFloat(0f, 360f), random.NextFloat(-25f, 25f)));
            }
        }

        /// <summary>
        /// 用柔和叠加平面模拟穿过开放交叉部的阳光。
        /// </summary>
        /// <remarks>
        /// URP 没有体积光，即使有 WebGL 预算也承担不起。三个交叉平面配柔边渐变，
        /// 从摄像机任何角度都像光柱，仅需数十三角形且不写深度，不会遮住预警或角色。
        /// </remarks>
        private static void BuildShafts(Transform root)
        {
            const float Length = 26f;

            var shafts = new GameObject("LightShafts").transform;
            shafts.SetParent(root, false);

            Material material = GetOrCreateAdditive("LightShaft", ShaftGradient(), ShaftColour);
            Material webMaterial = GetOrCreateAdditive("LightShaftWeb", ShaftGradient(), WebShaftColour);
            var renderers = new Renderer[ShaftCount];
            Mesh mesh = GetOrCreateCrossedPlanes();
            Vector3 down = SunRotation * Vector3.forward;
            var random = new XorShiftRandomProvider(LayoutSeed + 2u);

            for (int i = 0; i < ShaftCount; i++)
            {
                var landing = new Vector3(random.NextFloat(-9f, 9f), 0f, random.NextFloat(-9f, 9f));
                float width = random.NextFloat(2.2f, 4f);

                var shaft = new GameObject($"Shaft_{i}");
                shaft.transform.SetParent(shafts, false);
                shaft.transform.position = landing - down * (Length * 0.5f);
                shaft.transform.rotation = Quaternion.FromToRotation(Vector3.up, -down) * Quaternion.Euler(0f, i * 37f, 0f);
                shaft.transform.localScale = new Vector3(width, Length, width);

                shaft.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = shaft.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderers[i] = renderer;
            }

            root.parent.gameObject.AddComponent<PlatformLighting>().BindShafts(renderers, webMaterial);
        }

        /// <summary>
        /// 灯光存在后，将太阳和破损屋顶的阴影图案交给平台照明。
        /// </summary>
        /// <param name="sun">竞技场方向光。</param>
        public static void BindSun(Light sun)
        {
            PlatformLighting lighting = Object.FindAnyObjectByType<PlatformLighting>();

            if (lighting == null || sun == null)
            {
                return;
            }

            // Cookie 尺寸保存在 URP 灯光数据上，代码创建的灯光尚未带该数据。
            if (!sun.TryGetComponent(out UnityEngine.Rendering.Universal.UniversalAdditionalLightData _))
            {
                sun.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            }

            lighting.BindSun(sun, RoofShadowCookie());
        }

        /// <summary>
        /// 废墟屋顶投影：开阔天空下四根椽条，以及参差残存屋顶。
        /// </summary>
        /// <remarks>
        /// 可平铺，因为方向光 Cookie 在整块地板上重复。残片来自环绕网格上的value noise；使用固定种子，
        /// 保证每次生成相同图案。
        /// </remarks>
        private static Texture2D RoofShadowCookie()
        {
            const int Size = 256;
            const int Cells = 8;
            const string Name = "RoofShadowCookie";

            string path = EditorMenus.GeneratedMaterialFolder + "/" + Name + ".asset";
            var random = new XorShiftRandomProvider(LayoutSeed + 3u);
            var lattice = new float[Cells, Cells];

            for (int y = 0; y < Cells; y++)
            {
                for (int x = 0; x < Cells; x++)
                {
                    lattice[x, y] = random.NextFloat01();
                }
            }

            // 每次原地重写而非仅创建一次，让图案修改无需删除资产就可进入场景，同时保留所有引用。
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            bool created = texture == null;

            if (created)
            {
                texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = Name };
            }

            texture.wrapMode = TextureWrapMode.Repeat;

            for (int py = 0; py < Size; py++)
            {
                for (int px = 0; px < Size; px++)
                {
                    float u = (float)px / Size;
                    float v = (float)py / Size;

                    // 平滑插值、可环绕的格点噪声，表现坍塌与残存屋顶。
                    float gx = u * Cells, gy = v * Cells;
                    int x0 = Mathf.FloorToInt(gx) % Cells, y0 = Mathf.FloorToInt(gy) % Cells;
                    int x1 = (x0 + 1) % Cells, y1 = (y0 + 1) % Cells;
                    float fx = Mathf.SmoothStep(0f, 1f, gx - Mathf.Floor(gx));
                    float fy = Mathf.SmoothStep(0f, 1f, gy - Mathf.Floor(gy));
                    float noise = Mathf.Lerp(
                        Mathf.Lerp(lattice[x0, y0], lattice[x1, y0], fx),
                        Mathf.Lerp(lattice[x0, y1], lattice[x1, y1], fx), fy);

                    float roof = Edge(0.72f, 0.8f, noise);

                    // 四根椽条，每块平铺纹理数量为整数，使接缝对齐。
                    float rafter = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 4f));
                    float beam = Edge(0.04f, 0.1f, rafter);

                    // 只点缀而不覆盖。交叉部向天空开放，战斗地板应受阳光照明，仅有少量阴影。
                    // 初版在各处把阳光减至约五分之一，使全帧平均亮度从 0.38降到 0.21；
                    // 这里任何位置都不能低于 MinimumSun。
                    float light = Mathf.Max(MinimumSun, Mathf.Lerp(1f, 0.6f, roof) * Mathf.Lerp(0.65f, 1f, beam));
                    texture.SetPixel(px, py, new Color(light, light, light, 1f));
                }
            }

            texture.Apply(true);

            if (created)
            {
                AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedMaterialFolder);
                AssetDatabase.CreateAsset(texture, path);
            }
            else
            {
                EditorUtility.SetDirty(texture);
            }

            return texture;
        }

        /// <summary>光中漂浮微粒。使用 unscaled time，让 hit-stop 期间空气仍流动。</summary>
        private static void BuildDust(Transform root, float radius)
        {
            var dustObject = new GameObject("Dust");
            dustObject.transform.SetParent(root, false);
            dustObject.transform.position = new Vector3(0f, 4f, 0f);

            ParticleSystem system = dustObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.prewarm = true;
            main.useUnscaledTime = true;
            main.startLifetime = 14f;
            main.startSpeed = 0.04f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startColor = DustColour;
            main.maxParticles = 260;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 18f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(radius * 1.6f, 8f, radius * 1.6f);

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.15f;

            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f)
                });
            fade.color = gradient;

            var renderer = dustObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = GetOrCreateAdditive("DustMote", DotTexture(), Color.white);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 否则微粒经过镜头会成为泛光白团，占据十分之一屏幕。
            renderer.maxParticleSize = 0.006f;
        }

        private static GameObject Block(
            Transform parent, string name, Vector3 size, float metresPerTile, Material material,
            Vector3 position, Quaternion rotation) =>
            Place(parent, name, TiledMeshes.Box(size, metresPerTile), material, position, rotation);

        private static GameObject Place(
            Transform parent, string name, Mesh mesh, Material material, Vector3 position, Quaternion rotation)
        {
            var block = new GameObject(name);
            block.transform.SetParent(parent, false);
            block.transform.SetPositionAndRotation(position, rotation);

            block.AddComponent<MeshFilter>().sharedMesh = mesh;
            block.AddComponent<MeshRenderer>().sharedMaterial = material;

            return block;
        }

        private static Vector3 OnRing(float angleDegrees, float distance)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(radians) * distance, 0f, Mathf.Sin(radians) * distance);
        }

        private static Material GetOrCreateAdditive(string materialName, Texture2D texture, Color colour)
        {
            string path = EditorMenus.GeneratedMaterialFolder + "/" + materialName + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

                if (shader == null)
                {
                    Debug.LogWarning("[Adaptive Boss Arena] The URP particle shader is missing; " + materialName + " will not render.");
                    return null;
                }

                material = new Material(shader) { name = materialName };
                AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedMaterialFolder);
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", colour);
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>光柱横向柔和，靠天空最亮，在到达地板前淡出。</summary>
        private static Texture2D ShaftGradient() => GetOrCreateTexture("LightShaftGradient", 32, 128, (u, v) =>
        {
            float across = Mathf.Sin(u * Mathf.PI);
            float along = Mathf.SmoothStep(0f, 1f, v / 0.35f) * Mathf.SmoothStep(0f, 1f, (1f - v) / 0.15f);
            return across * across * along;
        });

        private static Texture2D DotTexture() => GetOrCreateTexture("DustMoteDot", 32, 32, (u, v) =>
        {
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            return Mathf.Clamp01(1f - d * d);
        });

        /// <summary>当以下数值从一侧边缘跨到另一侧时，产生平滑的零到一渐变： <paramref name="x"/> 。</summary>
        /// <remarks>
        /// 采用 GLSL 的 <c>smoothstep(edge0, edge1, x)</c>，不是 <see cref="Mathf.SmoothStep"/>；
        /// 后者参数外观相似但含义不同：它在前两个数值之间插值，传入边界只会得到边界间的数值而非遮罩。
        /// 这个错误曾让首版屋顶 Cookie 将整个竞技场阳光削至约五分之一。
        /// </remarks>
        /// <param name="edge0">渐变起点。</param>
        /// <param name="edge1">渐变终点。</param>
        /// <param name="x">参与渐变的值。</param>
        /// <returns>低于第一边缘为零，高于第二边缘为一，中间平滑变化。</returns>
        public static float Edge(float edge0, float edge1, float x) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, x));

        /// <summary>
        /// Alpha 由函数生成、保存为资产的白色纹理。
        /// </summary>
        /// <remarks>
        /// 保存为 Unity 纹理资产而不编码为 PNG，因为项目未包含图像编码所在的引擎模块。
        /// </remarks>
        private static Texture2D GetOrCreateTexture(string name, int width, int height, System.Func<float, float, float> alpha)
        {
            string path = EditorMenus.GeneratedMaterialFolder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

            if (existing != null)
            {
                return existing;
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp
            };

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float a = alpha((x + 0.5f) / width, (y + 0.5f) / height);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            texture.Apply(true);
            AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedMaterialFolder);
            AssetDatabase.CreateAsset(texture, path);

            return texture;
        }

        /// <summary>三个单位平面绕竖轴以 60 度交叉，y 从 -0.5 到 0.5。</summary>
        private static Mesh GetOrCreateCrossedPlanes()
        {
            string path = TiledMeshes.MeshFolder + "/CrossedPlanes.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (existing != null)
            {
                return existing;
            }

            var vertices = new Vector3[12];
            var uvs = new Vector2[12];
            var triangles = new int[18];

            for (int p = 0; p < 3; p++)
            {
                Vector3 across = Quaternion.Euler(0f, p * 60f, 0f) * new Vector3(0.5f, 0f, 0f);
                int v = p * 4;

                vertices[v] = -across + Vector3.down * 0.5f;
                vertices[v + 1] = across + Vector3.down * 0.5f;
                vertices[v + 2] = across + Vector3.up * 0.5f;
                vertices[v + 3] = -across + Vector3.up * 0.5f;

                uvs[v] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(1f, 0f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(0f, 1f);

                int t = p * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 2;
                triangles[t + 2] = v + 1;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 3;
                triangles[t + 5] = v + 2;
            }

            var mesh = new Mesh { name = "CrossedPlanes", vertices = vertices, uv = uvs, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            AssetAuthoring.EnsureFolderExists(TiledMeshes.MeshFolder);
            AssetDatabase.CreateAsset(mesh, path);

            return mesh;
        }
    }
}
