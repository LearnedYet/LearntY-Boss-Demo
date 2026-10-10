using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 创建 Universal Render Pipeline 资产，并设为当前管线。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unity URP 模板已创建并赋予这些资产。本项目结构由代码生成而非模板，因此曾未创建它们，
    /// 症状严重且隐蔽：包已安装，材质正确使用 <c>Universal Render Pipeline/Lit</c>，
    /// 但未指定管线使 Unity 回退内置渲染器，无法编译这些着色器，竞技场所有表面都变为实心品红。
    /// </para>
    /// <para>
    /// 编译和测试套件都不会发现此问题，只能实际观察游戏。
    /// </para>
    /// <para>
    /// 可安全重复运行；复用已有资产而非替换，保留手调画质设置。
    /// </para>
    /// </remarks>
    public static class RenderPipelineConfigurator
    {
        private const string SettingsFolder = "Assets/_Project/Settings";

        /// <summary>网页层级。保留原路径以维持已有引用。</summary>
        public const string PipelineAssetPath = SettingsFolder + "/UniversalRenderPipeline.asset";

        /// <summary>网页层级的渲染器。</summary>
        public const string RendererAssetPath = SettingsFolder + "/UniversalRenderer.asset";

        /// <summary>桌面层级：完整图形的 Windows 构建。</summary>
        public const string DesktopPipelineAssetPath = SettingsFolder + "/UniversalRenderPipeline_Desktop.asset";

        /// <summary>带环境遮蔽的桌面渲染器。</summary>
        public const string DesktopRendererAssetPath = SettingsFolder + "/UniversalRenderer_Desktop.asset";

        /// <summary>浏览器 Medium 画质：软阴影分辨率为 Low 两倍。</summary>
        public const string WebMediumPipelineAssetPath = SettingsFolder + "/UniversalRenderPipeline_WebMedium.asset";

        /// <summary>浏览器 High 画质：四级联阴影，火盆和蜡烛投影。</summary>
        public const string WebHighPipelineAssetPath = SettingsFolder + "/UniversalRenderPipeline_WebHigh.asset";

        /// <summary>Desktop Low：供运行 Windows 构建的集显使用，不启用环境遮蔽。</summary>
        public const string DesktopLowPipelineAssetPath = SettingsFolder + "/UniversalRenderPipeline_DesktopLow.asset";

        /// <summary>Desktop Medium：使用包含环境遮蔽的桌面渲染器，但阴影图较小。</summary>
        public const string DesktopMediumPipelineAssetPath = SettingsFolder + "/UniversalRenderPipeline_DesktopMedium.asset";

        /// <summary>
        /// 浏览器 Low 到 High 管线；Low 保留网页层级原路径。
        /// </summary>
        /// <remarks>
        /// 全部使用没有环境遮蔽的网页渲染器。曾添加一次就使浏览器构建完全失效，因此包括 High 在内，
        /// 任何浏览器画质都不可访问它。
        /// </remarks>
        public static readonly string[] WebPipelinePaths =
        {
            PipelineAssetPath, WebMediumPipelineAssetPath, WebHighPipelineAssetPath
        };

        /// <summary>Windows 构建 Low 到 High 管线；High 保留桌面层级原路径。</summary>
        public static readonly string[] DesktopPipelinePaths =
        {
            DesktopLowPipelineAssetPath, DesktopMediumPipelineAssetPath, DesktopPipelineAssetPath
        };

        /// <summary>游戏画质设置匹配的六个质量等级名称。</summary>
        public static readonly string[] LevelNames =
        {
            "Browser Low", "Browser Medium", "Browser High", "Desktop Low", "Desktop Medium", "Desktop High"
        };

        /// <summary>从顶部计，属于桌面层级的画质等级数量。</summary>
        /// <remarks>
        /// 保留 Unity 默认六级而不替换，因为画质设置没有公开增删等级 API。下三级是浏览器 Low、Medium、
        /// High，上三级属于 Windows。构建仅保留自身三级，玩家无论运行哪种版本都只看到一组 Low、Medium、
        /// High。
        /// </remarks>
        public const int DesktopTierLevelCount = 3;

        /// <summary>用于画质排除项和默认项的构建目标组名。</summary>
        public const string StandalonePlatform = "Standalone";

        /// <summary>用于画质排除项和默认项的构建目标组名。</summary>
        public const string WebGLPlatform = "WebGL";

        /// <summary>某渲染层级对管线的要求。</summary>
        private readonly struct TierSettings
        {
            public float ShadowDistance { get; init; }
            public int ShadowCascades { get; init; }
            public int MainShadowResolution { get; init; }
            public int AdditionalShadowResolution { get; init; }
            public int SoftShadowQuality { get; init; }
            public int Msaa { get; init; }
            public bool SoftShadows { get; init; }
            public bool AdditionalLightShadows { get; init; }
            public int LightsPerObject { get; init; }
            public bool DepthTexture { get; init; }
        }

        /// <summary>
        /// 网页层级：笔记本集显在浏览器中维持六十帧所能承担的配置。
        /// </summary>
        /// <remarks>
        /// 原先是缩小阴影图的桌面设置，只因后处理悄然未运行才负担得起。
        /// 生效后浏览器实测独显 p95 为 21 毫秒，集显玩家评价卡顿。16 米竞技场两级硬边阴影仍清晰；
        /// 只有太阳投影，网页没有任何效果读取深度纹理。
        /// </remarks>
        private static readonly TierSettings WebTier = new TierSettings
        {
            ShadowDistance = 22f,
            ShadowCascades = 2,
            MainShadowResolution = 1024,
            AdditionalShadowResolution = 512,
            SoftShadowQuality = 1,
            Msaa = 1,
            SoftShadows = false,
            AdditionalLightShadows = false,
            LightsPerObject = 4,
            DepthTexture = false
        };

        /// <summary>Browser Medium：阴影分辨率是 Low 两倍，使用软阴影和少量多重采样。</summary>
        private static readonly TierSettings WebMediumTier = new TierSettings
        {
            ShadowDistance = 30f,
            ShadowCascades = 2,
            MainShadowResolution = 2048,
            AdditionalShadowResolution = 512,
            SoftShadowQuality = 1,
            Msaa = 2,
            SoftShadows = true,
            AdditionalLightShadows = false,
            LightsPerObject = 6,
            DepthTexture = false
        };

        /// <summary>
        /// Browser High：四级联，火盆和蜡烛投影。
        /// </summary>
        /// <remarks>
        /// 仍不使用深度纹理：浏览器没有读取者，唯一会读取的环境遮蔽永远不放入网页渲染器。
        /// </remarks>
        private static readonly TierSettings WebHighTier = new TierSettings
        {
            ShadowDistance = 40f,
            ShadowCascades = 4,
            MainShadowResolution = 2048,
            AdditionalShadowResolution = 1024,
            SoftShadowQuality = 2,
            Msaa = 4,
            SoftShadows = true,
            AdditionalLightShadows = true,
            LightsPerObject = 8,
            DepthTexture = false
        };

        /// <summary>Desktop Low：运行 Windows 构建的集显。</summary>
        private static readonly TierSettings DesktopLowTier = new TierSettings
        {
            ShadowDistance = 30f,
            ShadowCascades = 2,
            MainShadowResolution = 2048,
            AdditionalShadowResolution = 1024,
            SoftShadowQuality = 1,
            Msaa = 2,
            SoftShadows = true,
            AdditionalLightShadows = false,
            LightsPerObject = 6,
            DepthTexture = false
        };

        /// <summary>Desktop Medium：包含环境遮蔽和投影灯光，阴影图比 High 小。</summary>
        private static readonly TierSettings DesktopMediumTier = new TierSettings
        {
            ShadowDistance = 40f,
            ShadowCascades = 4,
            MainShadowResolution = 2048,
            AdditionalShadowResolution = 1024,
            SoftShadowQuality = 2,
            Msaa = 4,
            SoftShadows = true,
            AdditionalLightShadows = true,
            LightsPerObject = 8,
            DepthTexture = true
        };

        /// <summary>桌面层级：更远处有更锐利阴影，边缘多重采样。</summary>
        private static readonly TierSettings DesktopTier = new TierSettings
        {
            ShadowDistance = 40f,
            ShadowCascades = 4,
            MainShadowResolution = 4096,
            AdditionalShadowResolution = 2048,
            SoftShadowQuality = 3,
            Msaa = 4,
            SoftShadows = true,
            AdditionalLightShadows = true,
            LightsPerObject = 8,
            DepthTexture = true
        };

        /// <summary>
        /// 是否向渲染器添加屏幕空间环境遮蔽。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 关闭，因为添加后 WebGL 启动即报“InternalError: too much recursion”，
        /// 堆栈循环重复少量 wasm 帧，属于无限递归而非仅深递归，加大栈也无济于事。
        /// 同一构建在 Windows 图形玩家版可完整战斗直到处决和破防，
        /// 说明问题位于浏览器渲染路径而非游戏逻辑。
        /// </para>
        /// <para>
        /// 失去环境遮蔽确实有代价，接触暗部能避免物体悬浮感，因此设为可逆开关而非删除。
        /// 重新启用时应把 SSAO 放在不透明几何之后，去掉对depth-normals 预通道的依赖；
        /// 这是渲染图循环最可能来源。再次发布前必须浏览器验证，桌面玩家版无法复现。
        /// </para>
        /// </remarks>
        private const bool EnableAmbientOcclusion = false;

        /// <summary>创建尚不存在的管线资产，并全局赋值。</summary>
        [MenuItem(EditorMenus.Setup + "Configure Render Pipeline",
            priority = EditorMenus.SetupPriorityConfigureProject + 1)]
        public static void ConfigureRenderPipeline()
        {
            AssetAuthoring.EnsureFolderExists(SettingsFolder);

            UniversalRenderPipelineAsset web = LoadOrCreatePipelineAsset(PipelineAssetPath, RendererAssetPath);
            UniversalRenderPipelineAsset desktop =
                LoadOrCreatePipelineAsset(DesktopPipelineAssetPath, DesktopRendererAssetPath);

            // 中间层级：浏览器和 Desktop Low 共用无环境遮蔽的网页渲染器；
            // Desktop Medium 共用有环境遮蔽的桌面渲染器。
            UniversalRenderPipelineAsset webMedium = LoadOrCreatePipelineAsset(WebMediumPipelineAssetPath, RendererAssetPath);
            UniversalRenderPipelineAsset webHigh = LoadOrCreatePipelineAsset(WebHighPipelineAssetPath, RendererAssetPath);
            UniversalRenderPipelineAsset desktopLow = LoadOrCreatePipelineAsset(DesktopLowPipelineAssetPath, RendererAssetPath);
            UniversalRenderPipelineAsset desktopMedium =
                LoadOrCreatePipelineAsset(DesktopMediumPipelineAssetPath, DesktopRendererAssetPath);

            if (web == null || desktop == null || webMedium == null || webHigh == null ||
                desktopLow == null || desktopMedium == null)
            {
                Debug.LogError(
                    "[Adaptive Boss Arena] Could not create the Universal Render Pipeline assets. " +
                    "Everything will render magenta until a pipeline is assigned.");
                return;
            }

            // 两处赋值都需要。Graphics 设置是项目默认，Quality 按等级覆盖，留空也会悄然优先。
            // 默认采用保守网页层级，使回退能在各平台运行。
            GraphicsSettings.defaultRenderPipeline = web;
            AssignQualityTiers(
                new[] { web, webMedium, webHigh },
                new[] { desktopLow, desktopMedium, desktop });

            TunePipeline(web, WebTier);
            TunePipeline(webMedium, WebMediumTier);
            TunePipeline(webHigh, WebHighTier);
            TunePipeline(desktopLow, DesktopLowTier);
            TunePipeline(desktopMedium, DesktopMediumTier);
            TunePipeline(desktop, DesktopTier);
            EnsureAmbientOcclusion(RendererAssetPath, EnableAmbientOcclusion);
            EnsureAmbientOcclusion(DesktopRendererAssetPath, true);
            EnsurePostProcessData(RendererAssetPath);
            EnsurePostProcessData(DesktopRendererAssetPath);

            // 浏览器尽可能直接渲染到屏幕；“Always”曾每帧强制额外全屏目标和复制。
            // 桌面为了环境遮蔽保留该配置。
            SetIntermediateTexture(RendererAssetPath, IntermediateTextureMode.Auto);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[Adaptive Boss Arena] Universal Render Pipeline configured: web tier " +
                $"'{PipelineAssetPath}', desktop tier '{DesktopPipelineAssetPath}'.");
        }

        /// <summary>
        /// 写入管线默认未提供的画质设置。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>UniversalRenderPipelineAsset.Create</c> 生成出厂默认值，配置器此前全部接受。
        /// 最严重的是软阴影：竞技场创建方向光时请求 <c>LightShadows.Soft</c> ，管线却拒绝，
        /// 结果代码看似启用了软阴影，游戏中所有阴影边缘仍硬且有锯齿。
        /// </para>
        /// <para>
        /// 阴影距离也值得说明。默认 50 米把 204 8 贴图分布到比约 15 米竞技场大百倍的区域，
        /// 而且只有一级联，角色只有少量阴影像素。缩短距离并拆四级联不增加成本，却成倍提高有效分辨率。
        /// </para>
        /// <para>
        /// 通过以下方式写入： <see cref="SerializedObject"/> ，而非公开 API，
        /// 因为多数是包类型没有公开 setter 的序列化字段。
        /// </para>
        /// </remarks>
        private static void TunePipeline(UniversalRenderPipelineAsset pipeline, TierSettings tier)
        {
            var serialized = new SerializedObject(pipeline);

            // 阴影柔和、距离近、分级联，让角色获得实际分辨率。
            SetIfPresent(serialized, "m_SoftShadowsSupported", tier.SoftShadows);
            SetIfPresent(serialized, "m_SoftShadowQuality", tier.SoftShadowQuality);
            SetIfPresent(serialized, "m_ShadowDistance", tier.ShadowDistance);
            SetIfPresent(serialized, "m_ShadowCascadeCount", tier.ShadowCascades);
            SetIfPresent(serialized, "m_MainLightShadowmapResolution", tier.MainShadowResolution);
            SetIfPresent(serialized, "m_MSAA", tier.Msaa);

            // 火盆原先完全无法投影：灯光请求阴影，管线仍禁止。
            SetIfPresent(serialized, "m_AdditionalLightsRenderingMode", 1);
            SetIfPresent(serialized, "m_AdditionalLightShadowsSupported", tier.AdditionalLightShadows);
            SetIfPresent(serialized, "m_AdditionalLightsShadowmapResolution", tier.AdditionalShadowResolution);

            // 四个火盆、首领阶段光环和每次命中闪光，容易让同一表面同时需要超过四盏灯；
            // 超限部分会直接消失。
            SetIfPresent(serialized, "m_AdditionalLightsPerObjectLimit", tier.LightsPerObject);

            // 环境遮蔽和所有读取深度的效果都需要此项。此前关闭，所以即使添加渲染器功能也不会正常工作。
            // 网页没有这些效果，每帧复制深度是无谓成本。
            SetIfPresent(serialized, "m_RequireDepthTexture", tier.DepthTexture);

            // 在 HDR 中调色，让 ACES 曲线和强度大于一的发光符合预期，避免先裁切到显示范围。
            SetIfPresent(serialized, "m_ColorGradingMode", 1);
            SetIfPresent(serialized, "m_ColorGradingLutSize", 32);

            // 让金属反射探针，而非仅反射平坦天空。
            SetIfPresent(serialized, "m_ReflectionProbeBlending", true);
            SetIfPresent(serialized, "m_ReflectionProbeBoxProjection", true);

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
        }

        /// <summary>
        /// 向渲染器添加屏幕空间环境遮蔽。
        /// </summary>
        /// <remarks>
        /// 渲染器功能列表原先为空，游戏完全没有环境遮蔽：没有屏幕空间通道、烘焙或遮蔽贴图。
        /// 接触暗部帮助判断物体彼此支撑，缺失后整个竞技场物体都略显悬浮，
        /// 是生成几何看起来像原型的重要原因。
        /// </remarks>
        private static void EnsureAmbientOcclusion(string rendererAssetPath, bool enabled)
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererAssetPath);

            if (rendererData == null)
            {
                return;
            }

            bool alreadyPresent = false;

            foreach (ScriptableRendererFeature existing in rendererData.rendererFeatures)
            {
                if (existing is ScreenSpaceAmbientOcclusion)
                {
                    alreadyPresent = true;
                    break;
                }
            }

            // 添加和移除都必须处理。渲染器资产加载而非重建，仅跳过添加会让之前加入的功能永久保留。
            if (!enabled)
            {
                if (alreadyPresent)
                {
                    RemoveAmbientOcclusion(rendererData);
                }

                return;
            }

            if (alreadyPresent)
            {
                return;
            }

            var ambientOcclusion = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            ambientOcclusion.name = "ScreenSpaceAmbientOcclusion";

            // 保存为渲染器子资产，与 Inspector 添加方式相同。
            AssetDatabase.AddObjectToAsset(ambientOcclusion, rendererData);

            var serialized = new SerializedObject(rendererData);
            SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
            SerializedProperty featureMap = serialized.FindProperty("m_RendererFeatureMap");

            if (features == null || featureMap == null)
            {
                Debug.LogWarning(
                    "[Adaptive Boss Arena] The renderer's feature list could not be found, so ambient " +
                    "occlusion was not added. Surfaces will render without contact darkening.");
                return;
            }

            features.arraySize += 1;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = ambientOcclusion;

            // 映射保存每个功能的本地文件 ID，使列表重排后渲染器仍正确关联引用。
            featureMap.arraySize = features.arraySize;
            featureMap.GetArrayElementAtIndex(featureMap.arraySize - 1).longValue =
                LocalFileIdOf(ambientOcclusion);

            serialized.ApplyModifiedPropertiesWithoutUndo();

            TuneAmbientOcclusion(ambientOcclusion);

            EditorUtility.SetDirty(rendererData);
        }

        /// <summary>
        /// 从渲染器移除环境遮蔽。
        /// </summary>
        /// <remarks>
        /// 功能列表和 ID 映射必须同时清理，也删除子资产本身。遗留的孤立功能对象即使无人引用，
        /// 也会留在构建中。
        /// </remarks>
        private static void RemoveAmbientOcclusion(UniversalRendererData rendererData)
        {
            var serialized = new SerializedObject(rendererData);
            SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
            SerializedProperty featureMap = serialized.FindProperty("m_RendererFeatureMap");

            if (features == null || featureMap == null)
            {
                return;
            }

            for (int i = features.arraySize - 1; i >= 0; i--)
            {
                var feature =
                    features.GetArrayElementAtIndex(i).objectReferenceValue as ScreenSpaceAmbientOcclusion;

                if (feature == null)
                {
                    continue;
                }

                features.DeleteArrayElementAtIndex(i);

                if (i < featureMap.arraySize)
                {
                    featureMap.DeleteArrayElementAtIndex(i);
                }

                AssetDatabase.RemoveObjectFromAsset(feature);
                Object.DestroyImmediate(feature, allowDestroyingAssets: true);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rendererData);

            Debug.Log(
                "[Adaptive Boss Arena] Ambient occlusion removed from the renderer. It breaks the " +
                "WebGL build; see EnableAmbientOcclusion for the detail.");
        }

        /// <summary>设置适度遮蔽强度，表现接触而非污垢。</summary>
        private static void TuneAmbientOcclusion(ScreenSpaceAmbientOcclusion ambientOcclusion)
        {
            var serialized = new SerializedObject(ambientOcclusion);
            SerializedProperty settings = serialized.FindProperty("m_Settings");

            if (settings != null)
            {
                SetChildIfPresent(settings, "Intensity", 1.1f);
                SetChildIfPresent(settings, "Radius", 0.28f);
                SetChildIfPresent(settings, "Falloff", 90f);
                SetChildIfPresent(settings, "Downsample", true);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>读取 Unity 分配给新增子资产的本地文件 ID。</summary>
        private static long LocalFileIdOf(Object subAsset) =>
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(subAsset, out string _, out long localId)
                ? localId
                : 0L;

        /// <summary>
        /// 写入序列化字段；当前管线版本没有该字段时跳过。
        /// </summary>
        /// <remarks>
        /// 包类型私有字段名不受编译期契约保证。缺失时警告并跳过，避免包升级导致项目设置硬失败。
        /// </remarks>
        private static void SetIfPresent(SerializedObject serialized, string fieldName, object value)
        {
            SerializedProperty property = serialized.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogWarning(
                    $"[Adaptive Boss Arena] Render pipeline field '{fieldName}' was not found and has " +
                    "been skipped. The render pipeline package may have renamed it.");
                return;
            }

            Assign(property, value);
        }

        /// <summary>写入序列化结构体子字段，缺失时跳过。</summary>
        private static void SetChildIfPresent(SerializedProperty parent, string fieldName, object value)
        {
            SerializedProperty property = parent.FindPropertyRelative(fieldName);

            if (property != null)
            {
                Assign(property, value);
            }
        }

        /// <summary>根据属性的序列化类型赋予装箱值。</summary>
        private static void Assign(SerializedProperty property, object value)
        {
            switch (value)
            {
                case bool flag:
                    property.boolValue = flag;
                    break;

                case int number:
                    property.intValue = number;
                    break;

                case float number:
                    property.floatValue = number;
                    break;
            }
        }

        private static void SetIntermediateTexture(string rendererAssetPath, IntermediateTextureMode mode)
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererAssetPath);

            if (rendererData != null && rendererData.intermediateTextureMode != mode)
            {
                rendererData.intermediateTextureMode = mode;
                EditorUtility.SetDirty(rendererData);
            }
        }

        /// <summary>管线包附带的后处理着色器和纹理。</summary>
        public const string DefaultPostProcessDataPath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";

        /// <summary>
        /// 为渲染器提供后处理所需资源。
        /// </summary>
        /// <remarks>
        /// 脚本创建的渲染器不带这些资源，URP 会悄然跳过所有后处理，无论摄像机和 Volume 如何设置。
        /// 游戏曾因此没有 Bloom、色调映射、暗角和调色，而配置每部分看似正确。
        /// </remarks>
        private static void EnsurePostProcessData(string rendererAssetPath)
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererAssetPath);
            var data = AssetDatabase.LoadAssetAtPath<PostProcessData>(DefaultPostProcessDataPath);

            if (rendererData == null || data == null)
            {
                Debug.LogWarning("[Adaptive Boss Arena] Could not give a renderer its post-processing data; effects will not render.");
                return;
            }

            if (rendererData.postProcessData != data)
            {
                rendererData.postProcessData = data;
                EditorUtility.SetDirty(rendererData);
            }
        }

        /// <summary>加载管线资产，首次运行时创建它及其渲染器。</summary>
        private static UniversalRenderPipelineAsset LoadOrCreatePipelineAsset(
            string pipelineAssetPath, string rendererAssetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelineAssetPath);

            if (existing != null)
            {
                return existing;
            }

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererAssetPath);

            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, rendererAssetPath);
            }

            UniversalRenderPipelineAsset pipeline = UniversalRenderPipelineAsset.Create(rendererData);

            if (pipeline == null)
            {
                return null;
            }

            pipeline.name = Path.GetFileNameWithoutExtension(pipelineAssetPath);
            AssetDatabase.CreateAsset(pipeline, pipelineAssetPath);

            return pipeline;
        }

        /// <summary>
        /// 将画质等级分为网页和桌面层级。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 每级仍必须设置管线，留空会回退内置渲染器，仅该档位变品红。
        /// 顶部 <see cref="DesktopTierLevelCount"/>个等级使用桌面管线并排除 WebGL，
        /// 其余使用网页管线并排除桌面构建。
        /// </para>
        /// <para>
        /// 排除机制保证安全。环境遮蔽曾让 WebGL 完全崩溃，仅在浏览器不使用某等级，
        /// 仍会把渲染器和遮蔽通道带进构建。从 WebGL 排除桌面等级，才让其渲染器完全不进入浏览器。
        /// </para>
        /// <para>
        /// 通过以下方式写入： <see cref="SerializedObject"/> ，因为平台排除和默认项没有公开 API。
        /// </para>
        /// </remarks>
        private static void AssignQualityTiers(
            UniversalRenderPipelineAsset[] web, UniversalRenderPipelineAsset[] desktop)
        {
            SerializedObject quality = LoadQualitySettings();
            SerializedProperty levels = quality?.FindProperty("m_QualitySettings");

            if (levels == null)
            {
                Debug.LogError(
                    "[Adaptive Boss Arena] Quality levels could not be read, so the render tiers were not " +
                    "assigned. Levels without a pipeline render magenta.");
                return;
            }

            int firstDesktopLevel = FirstDesktopLevel(levels.arraySize);

            for (int i = 0; i < levels.arraySize; i++)
            {
                SerializedProperty level = levels.GetArrayElementAtIndex(i);
                bool isDesktop = i >= firstDesktopLevel;

                // 在各平台自身等级中从首项起计为 Low、Medium、High；多于六级的额外项使用该平台 High。
                UniversalRenderPipelineAsset[] tiers = isDesktop ? desktop : web;
                int step = Mathf.Min(isDesktop ? i - firstDesktopLevel : i, tiers.Length - 1);

                SerializedProperty pipeline = level.FindPropertyRelative("customRenderPipeline");

                if (pipeline != null)
                {
                    pipeline.objectReferenceValue = tiers[step];
                }

                SerializedProperty levelName = level.FindPropertyRelative("name");

                if (levelName != null && i < LevelNames.Length)
                {
                    levelName.stringValue = LevelNames[i];
                }

                SetExcludedPlatforms(
                    level.FindPropertyRelative("excludedTargetPlatforms"),
                    isDesktop ? WebGLPlatform : StandalonePlatform);
            }

            // 浏览器在测量设备承载能力前使用 Medium；Windows 用High，集显时会降级。
            SetPlatformDefault(quality, WebGLPlatform, Mathf.Min(1, firstDesktopLevel - 1));
            SetPlatformDefault(quality, StandalonePlatform, levels.arraySize - 1);

            quality.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>首个属于桌面层级的画质等级。</summary>
        /// <param name="levelCount">画质等级总数量。</param>
        /// <returns>首个桌面等级的索引，不低于一。</returns>
        public static int FirstDesktopLevel(int levelCount) =>
            Mathf.Max(1, levelCount - DesktopTierLevelCount);

        /// <summary>加载项目画质设置以进行序列化编辑。</summary>
        /// <returns>设置；无法加载资产时返回 null。</returns>
        public static SerializedObject LoadQualitySettings()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");

            return assets != null && assets.Length > 0 ? new SerializedObject(assets[0]) : null;
        }

        private static void SetExcludedPlatforms(SerializedProperty excluded, string platform)
        {
            if (excluded == null)
            {
                return;
            }

            excluded.arraySize = 1;
            excluded.GetArrayElementAtIndex(0).stringValue = platform;
        }

        /// <summary>设置某平台启动时使用的画质等级。</summary>
        /// <remarks>
        /// 各平台默认项是序列化映射，显示为键值对数组。
        /// </remarks>
        private static void SetPlatformDefault(SerializedObject quality, string platform, int level)
        {
            SerializedProperty defaults = quality.FindProperty("m_PerPlatformDefaultQuality");

            if (defaults == null)
            {
                Debug.LogWarning(
                    $"[Adaptive Boss Arena] Per-platform quality defaults not found; '{platform}' keeps its " +
                    "current default quality level.");
                return;
            }

            for (int i = 0; i < defaults.arraySize; i++)
            {
                SerializedProperty pair = defaults.GetArrayElementAtIndex(i);

                if (pair.FindPropertyRelative("first")?.stringValue == platform)
                {
                    pair.FindPropertyRelative("second").intValue = level;
                    return;
                }
            }

            defaults.arraySize += 1;
            SerializedProperty added = defaults.GetArrayElementAtIndex(defaults.arraySize - 1);
            added.FindPropertyRelative("first").stringValue = platform;
            added.FindPropertyRelative("second").intValue = level;
        }
    }
}
