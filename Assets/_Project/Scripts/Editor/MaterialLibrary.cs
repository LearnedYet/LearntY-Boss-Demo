using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 创建并复用生成占位美术所需的纯色材质。
    /// </summary>
    /// <remarks>
    /// 场景与预制体生成器共用，使颜色只定义一次，使用者都引用同一资产。
    /// 每对象创建材质会产生大量近似资产，无收益地破坏批处理。
    /// </remarks>
    internal static class MaterialLibrary
    {
        private const string UniversalLitShader = "Universal Render Pipeline/Lit";
        private const string FallbackShader = "Standard";

        private const string UniversalColorProperty = "_BaseColor";
        private const string FallbackColorProperty = "_Color";

        /// <summary>按名称加载生成材质，不存在时创建。</summary>
        /// <param name="materialName">不含扩展名的文件名。</param>
        /// <param name="color">创建时设置的基础色。</param>
        /// <returns>材质；无法找到受光着色器时返回 null。</returns>
        public static Material GetOrCreate(string materialName, Color color)
        {
            string path = $"{EditorMenus.GeneratedMaterialFolder}/{materialName}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (existing != null)
            {
                return existing;
            }

            Shader shader = ResolveLitShader();

            if (shader == null)
            {
                Debug.LogWarning(
                    "[Adaptive Boss Arena] No lit shader could be resolved, so generated objects will " +
                    "use the default material. This usually means package import has not finished.");
                return null;
            }

            var material = new Material(shader) { name = materialName };
            material.SetColor(ColorPropertyFor(shader), color);

            AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedMaterialFolder);
            AssetDatabase.CreateAsset(material, path);

            return material;
        }

        /// <summary>
        /// 加载或创建竞技场天空。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 此前摄像机清为纯暗色，世界在墙顶处戛然而止。天空只需一个材质，就能让竞技场有地平线。
        /// </para>
        /// <para>
        /// 使用 Unity 程序天空而非立方体贴图，无需发布或生成图像，完全由数值驱动，
        /// 因此可在运行时让色调随首领情绪变化。
        /// </para>
        /// </remarks>
        /// <param name="materialName">不含扩展名的文件名。</param>
        /// <param name="skyTint">天空总体颜色。</param>
        /// <param name="groundColor">地平线以下颜色。</param>
        /// <param name="exposure">总体亮度；低值保持阴暗氛围。</param>
        /// <returns>材质；无法找到天空着色器时返回 null。</returns>
        public static Material GetOrCreateSkybox(
            string materialName, Color skyTint, Color groundColor, float exposure)
        {
            string path = $"{EditorMenus.GeneratedMaterialFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;

            if (created)
            {
                Shader shader = Shader.Find("Skybox/Procedural");

                if (shader == null)
                {
                    Debug.LogWarning(
                        "[Adaptive Boss Arena] The procedural sky shader could not be resolved, so the " +
                        "camera will keep clearing to a flat colour.");
                    return null;
                }

                material = new Material(shader) { name = materialName };
            }

            // 每次写入，让天空修改作用于已有资产。
            material.SetColor("_SkyTint", skyTint);
            material.SetColor("_GroundColor", groundColor);
            material.SetFloat("_Exposure", exposure);

            // 大气厚度高于默认，使地平线偏红，适合压迫竞技场的天空。
            material.SetFloat("_AtmosphereThickness", 1.7f);

            // 不显示太阳圆盘：太阳在云后，透窗亮盘会泛光成白色。
            material.SetFloat("_SunDisk", 0f);
            material.DisableKeyword("_SUNDISK_SIMPLE");
            material.DisableKeyword("_SUNDISK_HIGH_QUALITY");
            material.EnableKeyword("_SUNDISK_NONE");

            if (created)
            {
                AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedMaterialFolder);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        /// <summary>加载或创建显示生成纹理的非金属受光材质。</summary>
        /// <param name="materialName">不含扩展名的文件名。</param>
        /// <param name="albedo">表面颜色纹理。</param>
        /// <param name="smoothness">表面光泽程度。</param>
        /// <returns>材质；缺少受光着色器或纹理时返回 null。</returns>
        public static Material GetOrCreateTexturedSurface(string materialName, Texture2D albedo, float smoothness)
        {
            Shader shader = ResolveLitShader();

            if (shader == null || albedo == null)
            {
                return null;
            }

            string path = $"{EditorMenus.GeneratedMaterialFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(shader) { name = materialName };
                AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedMaterialFolder);
                AssetDatabase.CreateAsset(material, path);
            }

            // 每次重写，让重新生成的纹理或调整后的表面效果作用于已有资产。
            material.shader = shader;
            material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>
        /// 加载或创建具有金属度、粗糙度和可选发光的表面材质。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 此前生成内容都是平坦、默认粗糙的塑料：只有基础色。方向光无可呈现的细节，
        /// 整场像未贴纹理的基础模型。金属度和平滑度让光有表现空间，轮廓不再像平面。
        /// </para>
        /// <para>
        /// 自发光是这里成本最低的戏剧效果。Bloom 阈值 0.9，超过此亮度即可泛光，让火盆、
        /// 符文镶嵌和首领弱点真正发光而非仅呈浅色。
        /// </para>
        /// </remarks>
        /// <param name="materialName">不含扩展名的文件名。</param>
        /// <param name="color">基础色。</param>
        /// <param name="metallic">布料或石材为零，护甲和刀刃接近一。</param>
        /// <param name="smoothness">粗糙为零，抛光为一。</param>
        /// <param name="emission">发光颜色；不发光表面传入黑色。</param>
        /// <returns>材质；无法找到受光着色器时返回 null。</returns>
        public static Material GetOrCreateSurface(
            string materialName,
            Color color,
            float metallic = 0f,
            float smoothness = 0.35f,
            Color emission = default)
        {
            string path = $"{EditorMenus.GeneratedMaterialFolder}/{materialName}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (existing != null)
            {
                return existing;
            }

            Shader shader = ResolveLitShader();

            if (shader == null)
            {
                Debug.LogWarning(
                    "[Adaptive Boss Arena] No lit shader could be resolved, so generated surfaces will " +
                    "use the default material. This usually means package import has not finished.");
                return null;
            }

            var material = new Material(shader) { name = materialName };
            material.SetColor(ColorPropertyFor(shader), color);

            // 通过属性字符串命名，因为管线与内置着色器的属性不同；设置不存在属性不会生效，但不会报错。
            material.SetFloat("_Metallic", Mathf.Clamp01(metallic));
            material.SetFloat("_Smoothness", Mathf.Clamp01(smoothness));
            material.SetFloat("_Glossiness", Mathf.Clamp01(smoothness));

            if (emission.maxColorComponent > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedMaterialFolder);
            AssetDatabase.CreateAsset(material, path);

            return material;
        }

        /// <summary>
        /// 从颜色、法线和 URP 遮罩贴图加载或创建受光材质。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 遮罩按 URP Lit 读取方式打包：红色为金属度、绿色为遮蔽、Alpha 为平滑度，
        /// 一张同时填充金属和遮蔽槽。直接使用公开 ARM 图会把粗糙度放到遮蔽位置，让石材像从内部发光。
        /// </para>
        /// <para>
        /// 平铺由网格 UV 决定，不在此处设，使一个材质适用于任意尺寸墙柱。每次刷新而非仅缺失时创建；
        /// 这些材质没有手调值，下层纹理重建否则会断开。
        /// </para>
        /// </remarks>
        /// <param name="materialName">不含扩展名的文件名。</param>
        /// <param name="textureSet">贴图组的文件夹和文件前缀，例如 <c>stone_tiles_02</c>.</param>
        /// <param name="tint">乘入颜色贴图，使贴图组靠近竞技场配色。</param>
        /// <returns>材质；缺少纹理或受光着色器时返回 null。</returns>
        public static Material GetOrCreateTexturedLit(string materialName, string textureSet, Color tint)
        {
            string folder = "Assets/_Project/Art/ThirdParty/PolyHaven/" + textureSet + "/" + textureSet;
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "_diff_1k.jpg");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "_nor_gl_1k.jpg");
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "_mask_1k.png");
            Shader shader = ResolveLitShader();

            if (albedo == null || shader == null)
            {
                Debug.LogWarning("[Adaptive Boss Arena] Texture set " + textureSet + " is missing; a flat surface stands in.");
                return GetOrCreateSurface(materialName, tint, 0f, 0.2f);
            }

            string path = $"{EditorMenus.GeneratedMaterialFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;

            if (created)
            {
                material = new Material(shader) { name = materialName };
            }

            material.shader = shader;
            material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", tint);

            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", 1f);
                material.EnableKeyword("_NORMALMAP");
            }

            if (mask != null)
            {
                material.SetTexture("_MetallicGlossMap", mask);
                material.SetTexture("_OcclusionMap", mask);
                material.SetFloat("_Metallic", 1f);
                material.SetFloat("_Smoothness", 1f);
                material.SetFloat("_OcclusionStrength", 1f);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.EnableKeyword("_OCCLUSIONMAP");
            }

            if (created)
            {
                AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedMaterialFolder);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        /// <summary>
        /// 加载或创建攻击覆盖图共用的透明材质。
        /// </summary>
        /// <remarks>
        /// 使用精灵着色器而非管线受光或 Unlit 着色器。它在各管线默认不受光且 Alpha 混合；
        /// URP Unlit 透明需设置版本间不同的多个表面关键字。覆盖图只需平面半透明颜色，
        /// 因此采用最简单可靠方案。
        /// </remarks>
        /// <returns>覆盖图材质；没有合适着色器时返回 null。</returns>
        public static Material GetOrCreateAttackOverlay()
        {
            const string materialName = "AttackOverlay";
            string path = $"{EditorMenus.GeneratedMaterialFolder}/{materialName}.mat";

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

            if (shader == null)
            {
                Debug.LogWarning(
                    "[Adaptive Boss Arena] Could not resolve a transparent shader; attack overlays " +
                    "will not render.");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(shader) { name = materialName };
                AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedMaterialFolder);
                AssetDatabase.CreateAsset(material, path);
            }

            // 使用粒子着色器而非普通 Unlit，因为它乘入顶点色；覆盖网格用顶点色表现亮边、淡内侧。
            // 已有资产也重新设置。
            material.shader = shader;
            ConfigureParticleBlend(material, additive: false);
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            material.SetFloat("_ColorMode", 0f);
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>
        /// 加载或创建武器轨迹共用材质。
        /// </summary>
        /// <remarks>
        /// 使用支持顶点色的粒子着色器，让轨迹随年龄淡出。管线普通 Unlit 忽略顶点色，
        /// 会画出不淡出的卡片。
        /// </remarks>
        /// <returns>轨迹材质；没有合适着色器时返回 null。</returns>
        public static Material GetOrCreateWeaponTrail()
        {
            const string materialName = "WeaponTrail";
            string path = $"{EditorMenus.GeneratedMaterialFolder}/{materialName}.mat";

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

            if (shader == null)
            {
                Debug.LogWarning(
                    "[Adaptive Boss Arena] Could not resolve the particle shader; weapon trails will not render.");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(shader) { name = materialName };
                AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedMaterialFolder);
                AssetDatabase.CreateAsset(material, path);
            }

            // 已有资产也重新设置，替换旧单点丝带所用精灵材质。
            material.shader = shader;
            ConfigureParticleBlend(material, additive: true);
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            material.SetTexture("_BaseMap", BladeSmearGradient());
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>
        /// 拖影明暗：剑刃底部透明，向边缘增强到细亮线。
        /// </summary>
        /// <remarks>
        /// U 沿拖影年龄，V 横过刀刃。均匀亮带像实体卡片；明亮边缘向内淡出，更像钢刃轨迹。
        /// </remarks>
        private static Texture2D BladeSmearGradient()
        {
            const int Width = 64, Height = 64;
            const string Name = "BladeSmearGradient";
            string path = EditorMenus.GeneratedMaterialFolder + "/" + Name + ".asset";

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            bool created = texture == null;

            if (created)
            {
                texture = new Texture2D(Width, Height, TextureFormat.RGBA32, true) { name = Name };
            }

            texture.wrapMode = TextureWrapMode.Clamp;

            for (int y = 0; y < Height; y++)
            {
                float across = (y + 0.5f) / Height;
                float body = Mathf.Pow(across, 2.5f) * 0.55f;
                float edge = Mathf.Exp(-Mathf.Pow((across - 0.9f) / 0.05f, 2f));
                float fadeOut = 1f - Environment.CathedralBuilder.Edge(0.92f, 1f, across);

                for (int x = 0; x < Width; x++)
                {
                    float age = (x + 0.5f) / Width;
                    float value = Mathf.Clamp01((body + edge) * fadeOut * (1f - age * 0.6f));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, value));
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

        /// <summary>
        /// 获取或创建绘制命中火花的叠加材质。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 改用真实资产，而非原来的运行时材质。运行时按名称找着色器，构建仅包含被引用者；
        /// 没有资源引用粒子着色器时，WebGL 可将其剔除，让火花悄然无法渲染。场景通过此资产引用，
        /// 确保着色器随构建发布。
        /// </para>
        /// <para>
        /// 采用叠加混合，让火花像光而非彩纸，并触发泛光。
        /// </para>
        /// </remarks>
        /// <returns>火花材质；没有合适着色器时返回 null。</returns>
        public static Material GetOrCreateImpactSparks()
        {
            const string materialName = "ImpactSparks";
            string path = EditorMenus.GeneratedMaterialFolder + "/" + materialName + ".mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (existing != null)
            {
                return DressSparks(existing);
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

            if (shader == null)
            {
                Debug.LogWarning("[Adaptive Boss Arena] The URP particle shader is missing; impact sparks will not render.");
                return null;
            }

            var material = new Material(shader) { name = materialName };

            ConfigureParticleBlend(material, additive: true);
            DressSparks(material);

            AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedMaterialFolder);
            AssetDatabase.CreateAsset(material, path);

            return material;
        }

        /// <summary>加载或创建绘制血液和尘土的材质。</summary>
        /// <remarks>
        /// 与火花不同，采用 Alpha 混合：血液和尘土是覆盖背景的物质。叠加绘制会发光，让打击身体像放烟花。
        /// </remarks>
        /// <returns>物质材质；缺少粒子着色器时返回 null。</returns>
        public static Material GetOrCreateImpactMatter()
        {
            const string materialName = "ImpactMatter";
            string path = EditorMenus.GeneratedMaterialFolder + "/" + materialName + ".mat";

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

                if (shader == null)
                {
                    return null;
                }

                material = new Material(shader) { name = materialName };
                ConfigureParticleBlend(material, additive: false);
                AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedMaterialFolder);
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetTexture("_BaseMap", SoftParticle());
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>加载或创建地面残留血迹材质。</summary>
        /// <remarks>
        /// Alpha 混合暗血色，在旧污渍后、伤痕和警告前绘制，让新血覆盖旧污垢，
        /// 又位于仍影响战斗的提示下方。
        /// </remarks>
        /// <returns>血溅材质；缺少粒子着色器时返回 null。</returns>
        public static Material GetOrCreateBloodSplatter()
        {
            const string materialName = "BloodSplatter";
            string path = EditorMenus.GeneratedMaterialFolder + "/" + materialName + ".mat";

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

            if (shader == null)
            {
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(shader) { name = materialName };
                AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedMaterialFolder);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            ConfigureParticleBlend(material, additive: false);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 40;
            material.SetTexture("_BaseMap", BloodSplatterTexture());
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>
        /// 血溅：底部聚集血团，条痕和液滴向上飞溅。
        /// </summary>
        /// <remarks>
        /// 纹理上方朝向打击方向，池按打击方向铺放；固定种子保证每次生成相同血溅。
        /// </remarks>
        private static Texture2D BloodSplatterTexture()
        {
            const int Size = 256;
            const string Name = "BloodSplatterTexture";
            string path = EditorMenus.GeneratedMaterialFolder + "/" + Name + ".asset";

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            bool created = texture == null;

            if (created)
            {
                texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = Name };
            }

            texture.wrapMode = TextureWrapMode.Clamp;

            var random = new Core.Services.XorShiftRandomProvider(4410u);
            var drops = new System.Collections.Generic.List<(Vector2 Centre, float Radius)>();

            // 向纹理上方抛出的液滴，飞得越远越小。
            for (int i = 0; i < 38; i++)
            {
                float along = random.NextFloat(0f, 1f);
                float spread = random.NextFloat(-0.35f, 0.35f) * (0.4f + along);
                drops.Add((new Vector2(0.5f + spread, 0.3f + along * 0.62f), Mathf.Lerp(0.045f, 0.008f, along) * random.NextFloat(0.6f, 1.3f)));
            }

            var dark = new Color(0.16f, 0.008f, 0.01f);
            var fresh = new Color(0.34f, 0.02f, 0.02f);

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    var p = new Vector2((x + 0.5f) / Size, (y + 0.5f) / Size);

                    // 主体血泊：纹理低处的噪声斑块。
                    Vector2 fromPool = new Vector2((p.x - 0.5f) / 0.2f, (p.y - 0.3f) / 0.16f);
                    float noise = Mathf.PerlinNoise(p.x * 14f + 3.7f, p.y * 14f + 8.2f);
                    float pool = 1f - Environment.CathedralBuilder.Edge(0.7f, 1.05f, fromPool.magnitude + (noise - 0.5f) * 0.6f);

                    float drop = 0f;

                    foreach ((Vector2 centre, float radius) in drops)
                    {
                        float d = Vector2.Distance(p, centre) / radius;
                        drop = Mathf.Max(drop, 1f - Environment.CathedralBuilder.Edge(0.75f, 1f, d));
                    }

                    float alpha = Mathf.Clamp01(Mathf.Max(pool, drop) * (0.8f + noise * 0.25f));
                    Color colour = Color.Lerp(dark, fresh, noise);
                    colour.a = alpha;
                    texture.SetPixel(x, y, colour);
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

        /// <summary>为火花材质提供圆形衰减和足以触发泛光的炽亮颜色。</summary>
        /// <remarks>
        /// 无纹理火花原先是拉长方块，颜色强度为一时达不到泛光阈值，像橙色油漆。每次刷新，
        /// 让已有资产应用改动。
        /// </remarks>
        private static Material DressSparks(Material material)
        {
            material.SetTexture("_BaseMap", SoftParticle());
            material.SetColor("_BaseColor", new Color(2.4f, 1.5f, 0.7f, 1f));
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>设置粒子材质为透明，可选择增加光或覆盖背景。</summary>
        private static void ConfigureParticleBlend(Material material, bool additive)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 1f : 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive
                ? UnityEngine.Rendering.BlendMode.One
                : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        /// <summary>所有粒子材质共用、带柔和衰减的白色圆盘。</summary>
        private static Texture2D SoftParticle()
        {
            const int Size = 64;
            const string Name = "SoftParticle";
            string path = EditorMenus.GeneratedMaterialFolder + "/" + Name + ".asset";

            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

            if (existing != null)
            {
                return existing;
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = Name, wrapMode = TextureWrapMode.Clamp };

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float r = new Vector2((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f).magnitude;
                    float alpha = 1f - Environment.CathedralBuilder.Edge(0.25f, 1f, r);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * alpha));
                }
            }

            texture.Apply(true);
            AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedMaterialFolder);
            AssetDatabase.CreateAsset(texture, path);

            return texture;
        }

        /// <summary>
        /// 获取或创建绘制地面危险区域的 Alpha 混合材质。
        /// </summary>
        /// <remarks>
        /// 保存为资产的理由与以下方法相同： <see cref="GetOrCreateImpactSparks"/>：
        /// 运行时按名称查找的着色器可能被构建剔除。采用 Alpha 混合而非叠加，
        /// 使伤痕像加深地面的污渍而非光。
        /// </remarks>
        /// <returns>危险区域材质；没有合适着色器时返回 null。</returns>
        public static Material GetOrCreateHazardDisc()
        {
            const string materialName = "HazardDisc";
            string path = EditorMenus.GeneratedMaterialFolder + "/" + materialName + ".mat";

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = CreateTransparentUnlit(materialName);

                if (material == null)
                {
                    return null;
                }

                AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedMaterialFolder);
                AssetDatabase.CreateAsset(material, path);
            }

            // 使用带余烬裂缝的焦坑而非平红圆盘：圆盘像躺在地上的 UI 标记，不像被砸坏的地面。每次刷新，
            // 让修改作用于资产。
            material.SetTexture("_BaseMap", ScorchCracks());
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>
        /// 地面伤痕：焦黑中心向边缘淡出，发光裂缝从冲击点放射。
        /// </summary>
        /// <remarks>
        /// 颜色承载余烬、Alpha 承载范围，危险区域色调和淡出仍由各实例颜色决定。裂缝按固定种子生成，
        /// 保证每次相同。
        /// </remarks>
        private static Texture2D ScorchCracks()
        {
            const int Size = 384;
            const string Name = "HazardScorchCracks";
            string path = EditorMenus.GeneratedMaterialFolder + "/" + Name + ".asset";

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            bool created = texture == null;

            if (created)
            {
                texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = Name };
            }

            texture.wrapMode = TextureWrapMode.Clamp;

            var segments = new System.Collections.Generic.List<(Vector2 A, Vector2 B, float Width)>();
            var random = new Core.Services.XorShiftRandomProvider(9173u);

            // 主裂缝从冲击点曲折向外延伸；部分分叉一次。
            for (int crack = 0; crack < 9; crack++)
            {
                float angle = crack * (Mathf.PI * 2f / 9f) + random.NextFloat(-0.25f, 0.25f);
                Vector2 point = Vector2.zero;
                float width = random.NextFloat(0.007f, 0.012f);

                for (int step = 0; step < 11; step++)
                {
                    angle += random.NextFloat(-0.35f, 0.35f);
                    Vector2 next = point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * random.NextFloat(0.06f, 0.1f);
                    segments.Add((point, next, width * (1f - step / 12f)));

                    if (step == 4 && random.NextBool(0.6f))
                    {
                        float forkAngle = angle + random.NextFloat(0.5f, 0.9f) * (random.NextBool() ? 1f : -1f);
                        Vector2 fork = next;

                        for (int f = 0; f < 5; f++)
                        {
                            forkAngle += random.NextFloat(-0.3f, 0.3f);
                            Vector2 forkNext = fork + new Vector2(Mathf.Cos(forkAngle), Mathf.Sin(forkAngle)) * 0.06f;
                            segments.Add((fork, forkNext, width * 0.6f * (1f - f / 6f)));
                            fork = forkNext;
                        }
                    }

                    point = next;
                }
            }

            var scorch = new Color(0.05f, 0.03f, 0.02f);
            var ember = new Color(0.95f, 0.28f, 0.05f);

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    var p = new Vector2((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f);
                    float r = p.magnitude;

                    float crackStrength = 0f;

                    foreach ((Vector2 a, Vector2 b, float width) in segments)
                    {
                        Vector2 ab = b - a;
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                        float distance = Vector2.Distance(p, a + ab * t);
                        crackStrength = Mathf.Max(crackStrength, Mathf.Exp(-(distance * distance) / (width * width)));
                    }

                    float body = 1f - Environment.CathedralBuilder.Edge(0.3f, 1f, r);
                    float cracks = crackStrength * (1f - Environment.CathedralBuilder.Edge(0.7f, 1f, r));
                    float heart = Mathf.Exp(-(r * r) / 0.008f);
                    float glow = Mathf.Max(cracks, heart * 0.8f);

                    Color colour = Color.Lerp(scorch, ember, glow);
                    colour.a = Mathf.Clamp01(Mathf.Max(body * 0.85f, glow));
                    texture.SetPixel(x, y, colour);
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

        /// <summary>
        /// 为当前渲染管线构建 Unlit Alpha 混合材质。
        /// </summary>
        /// <remarks>
        /// <para>
        /// URP Unlit 材质不会仅因颜色 Alpha而透明。表面模式、混合模式、深度写入、
        /// 渲染队列和着色器关键字必须同时设置，否则会悄然完全不透明或完全不渲染。
        /// </para>
        /// <para>
        /// 非 URP 项目回退到默认不受光且 Alpha 混合的内置精灵着色器。
        /// </para>
        /// </remarks>
        private static Material CreateTransparentUnlit(string materialName)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
            {
                Shader fallback = Shader.Find("Sprites/Default");
                return fallback == null ? null : new Material(fallback) { name = materialName };
            }

            var material = new Material(shader) { name = materialName };

            const float transparentSurface = 1f;
            const float alphaBlend = 0f;

            material.SetFloat("_Surface", transparentSurface);
            material.SetFloat("_Blend", alphaBlend);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");

            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            return material;
        }

        /// <summary>为基础模型设置生成材质，容许着色器查找失败。</summary>
        /// <param name="target">要设置渲染器材质的对象。</param>
        /// <param name="materialName">不含扩展名的文件名。</param>
        /// <param name="color">创建时设置的基础色。</param>
        public static void Apply(GameObject target, string materialName, Color color)
        {
            Material material = GetOrCreate(materialName, color);
            var renderer = target.GetComponent<MeshRenderer>();

            if (material != null && renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        /// <summary>
        /// 解析渲染管线受光着色器，回退到内置版本。
        /// </summary>
        /// <remarks>
        /// 包导入完成前运行时回退很重要。光照异常场景比中途终止的生成器更容易发现和纠正。
        /// </remarks>
        private static Shader ResolveLitShader()
        {
            Shader shader = Shader.Find(UniversalLitShader);
            return shader != null ? shader : Shader.Find(FallbackShader);
        }

        /// <summary>解析不同管线之间不同的基础色属性名。</summary>
        private static string ColorPropertyFor(Shader shader) =>
            shader.name.StartsWith(UniversalLitShader) ? UniversalColorProperty : FallbackColorProperty;
    }
}
