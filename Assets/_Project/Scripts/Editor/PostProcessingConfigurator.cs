using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 创建并填充后处理 Volume Profile。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 竞技场由未贴纹理的基础模型组成，后处理承担多数通常由美术承担的工作。
    /// Bloom 让攻击覆盖图和首领弱点有发光感，暗角将注意力集中到决斗中心，
    /// 轻微色调映射和调色避免全场像原始无纹理灰模。
    /// </para>
    /// <para>
    /// 数值刻意克制。抽象形状加重后处理像屏保；目标是让竞技场看起来经过设计，而非过度加工。
    /// </para>
    /// </remarks>
    public static class PostProcessingConfigurator
    {
        private const string ProfilePath = "Assets/_Project/Settings/ArenaVolumeProfile.asset";

        /// <summary>
        /// 浏览器配置：保留同一调色，但去掉各需一次全屏处理的效果。
        /// </summary>
        /// <remarks>
        /// 后处理实际渲染后，集显笔记本的浏览器版本开始卡顿。胶片颗粒和色差各增加一次全分辨率处理，
        /// 视觉收益却难与调色区分；半分辨率加高质量滤波是最昂贵 Bloom。曝光、对比度、分离色调、
        /// 白平衡和色调映射都合并到一张查找表，成本不变，因此保留。
        /// </remarks>
        public const string WebProfilePath = "Assets/_Project/Settings/ArenaVolumeProfile_Web.asset";

        /// <summary>创建尚不存在的两个配置，并填充效果覆盖项。</summary>
        [MenuItem(EditorMenus.Setup + "Configure Post Processing",
            priority = EditorMenus.SetupPriorityConfigureProject + 2)]
        public static void ConfigurePostProcessing()
        {
            AssetAuthoring.EnsureFolderExists("Assets/_Project/Settings");

            VolumeProfile desktop = LoadOrCreate(ProfilePath);
            ConfigureBloom(desktop);
            ConfigureVignette(desktop);
            ConfigureColorAdjustments(desktop);
            ConfigureTonemapping(desktop);
            ConfigureChromaticAberration(desktop);
            ConfigureFilmGrain(desktop);
            ConfigureSplitToning(desktop);
            ConfigureWhiteBalance(desktop);
            EditorUtility.SetDirty(desktop);

            VolumeProfile web = LoadOrCreate(WebProfilePath);
            ConfigureBloom(web);
            ConfigureVignette(web);
            ConfigureColorAdjustments(web);
            ConfigureTonemapping(web);
            ConfigureSplitToning(web);
            ConfigureWhiteBalance(web);
            MakeCheapForTheBrowser(web);
            EditorUtility.SetDirty(web);

            AssetDatabase.SaveAssets();

            Debug.Log("[Adaptive Boss Arena] Post-processing profiles written: desktop and web.");
        }

        /// <summary>为场景构建器加载生成配置。</summary>
        /// <returns>配置；尚未生成时返回 null。</returns>
        public static VolumeProfile LoadProfile() =>
            GeneratedAssets.ForceImportAndLoad<VolumeProfile>(ProfilePath);

        /// <summary>为场景构建器加载浏览器配置。</summary>
        /// <returns>配置；尚未生成时返回 null。</returns>
        public static VolumeProfile LoadWebProfile() =>
            GeneratedAssets.ForceImportAndLoad<VolumeProfile>(WebProfilePath);

        private static VolumeProfile LoadOrCreate(string path)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);

            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            // 此前添加效果却未保存，导致条目为 null；参见 GetOrAdd。
            profile.components.RemoveAll(component => component == null);

            return profile;
        }

        /// <summary>
        /// 将浏览器 Bloom 设为低成本：四分之一分辨率、简单滤波、更少通道。
        /// </summary>
        /// <remarks>
        /// 此配置从不添加胶片颗粒和色差。这里不移除它们，因为从列表移除组件仍会留下已保存子资产。
        /// </remarks>
        private static void MakeCheapForTheBrowser(VolumeProfile profile)
        {
            if (profile.TryGet(out Bloom bloom))
            {
                bloom.highQualityFiltering.Override(false);
                bloom.downscale.Override(BloomDownscaleMode.Quarter);
                bloom.maxIterations.Override(5);
            }
        }

        /// <summary>
        /// 让明亮表面泛光。
        /// </summary>
        /// <remarks>
        /// 此处价值最高的单项效果。攻击覆盖图、首领弱点和弹反闪光依赖发光观感，
        /// 没有 Bloom 就只是平面纯色。
        /// </remarks>
        private static void ConfigureBloom(VolumeProfile profile)
        {
            Bloom bloom = GetOrAdd<Bloom>(profile);

            bloom.threshold.Override(0.9f);
            bloom.intensity.Override(0.85f);
            bloom.scatter.Override(0.65f);
            bloom.tint.Override(new Color(1f, 0.96f, 0.92f));
        }

        /// <summary>压暗边缘，将视线拉向决斗。</summary>
        private static void ConfigureVignette(VolumeProfile profile)
        {
            Vignette vignette = GetOrAdd<Vignette>(profile);

            vignette.intensity.Override(0.36f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(new Color(0.02f, 0.01f, 0.04f));
        }

        /// <summary>降低饱和度并增强对比，形成阴沉多云配色。</summary>
        /// <remarks>
        /// 角色和石材有纹理后，图像不再需要提亮，而需要重量感。近乎全饱和的绿皮、亮资源条和橙光像卡通。
        /// 仅略降曝光，因为再暗会让角色与地板混在一起。
        /// </remarks>
        private static void ConfigureColorAdjustments(VolumeProfile profile)
        {
            ColorAdjustments color = GetOrAdd<ColorAdjustments>(profile);

            color.postExposure.Override(1.3f);
            color.contrast.Override(20f);
            color.saturation.Override(-22f);
            color.colorFilter.Override(new Color(0.94f, 0.96f, 1f));
        }

        /// <summary>应用电影曲线，让亮部逐渐过渡而非直接裁切。</summary>
        private static void ConfigureTonemapping(VolumeProfile profile)
        {
            Tonemapping tonemapping = GetOrAdd<Tonemapping>(profile);
            tonemapping.mode.Override(TonemappingMode.ACES);
        }

        /// <summary>
        /// 边缘轻微镜头色散。
        /// </summary>
        /// <remarks>
        /// 保持很低。这是最常被过度使用的效果，超过几个百分点会像显示器故障而非风格。
        /// </remarks>
        private static void ConfigureChromaticAberration(VolumeProfile profile)
        {
            ChromaticAberration aberration = GetOrAdd<ChromaticAberration>(profile);
            aberration.intensity.Override(0.08f);
        }

        /// <summary>
        /// 在全画面叠加细颗粒。
        /// </summary>
        /// <remarks>
        /// 成本低，且对本项目特别有价值。无纹理生成表面形成完美平滑渐变，强烈暗示并非摄影或绘画。
        /// 颗粒打破渐变，足够轻微、不被直接察觉时会像胶片而非噪声。
        /// </remarks>
        private static void ConfigureFilmGrain(VolumeProfile profile)
        {
            FilmGrain grain = GetOrAdd<FilmGrain>(profile);

            grain.type.Override(FilmGrainLookup.Medium1);
            grain.intensity.Override(0.28f);

            // 让亮部颗粒淡出，符合真实胶片表现。
            grain.response.Override(0.8f);
        }

        /// <summary>
        /// 冷却阴影，暖化高光。
        /// </summary>
        /// <remarks>
        /// 这是完整调色与仅提高对比滑杆的区别。冷影对暖光是使暗景有意图感的经典方法，
        /// 适合一盏冷主光和四处火光照亮的竞技场。
        /// </remarks>
        private static void ConfigureSplitToning(VolumeProfile profile)
        {
            ShadowsMidtonesHighlights grading = GetOrAdd<ShadowsMidtonesHighlights>(profile);

            // 各颜色第四通道是该区间总权重，不是 Alpha。
            grading.shadows.Override(new Vector4(0.9f, 0.95f, 1.05f, 0f));
            grading.midtones.Override(new Vector4(1f, 0.98f, 0.95f, 0f));
            grading.highlights.Override(new Vector4(1.06f, 1f, 0.9f, 0f));
        }

        /// <summary>
        /// 整体图像偏冷。
        /// </summary>
        /// <remarks>
        /// 使用真正色适应，而非此前用颜色滤镜模拟。滤镜逐通道相乘，着色时也压暗；
        /// 白平衡改变何为白色并保留曝光。
        /// </remarks>
        private static void ConfigureWhiteBalance(VolumeProfile profile)
        {
            WhiteBalance balance = GetOrAdd<WhiteBalance>(profile);

            balance.temperature.Override(-5f);
            balance.tint.Override(2f);
        }

        /// <summary>返回配置中的覆盖项，缺失时添加。</summary>
        /// <remarks>
        /// <see cref="VolumeProfile.Add{T}"/> 只在内存创建效果。未保存为配置子资产时，
        /// 重新加载会变成 null，曾让所有构建没有 Bloom、色调映射、暗角和调色，
        /// 而执行设置的编辑器会话看起来正确。
        /// </remarks>
        private static TComponent GetOrAdd<TComponent>(VolumeProfile profile)
            where TComponent : VolumeComponent
        {
            if (!profile.TryGet(out TComponent component))
            {
                component = profile.Add<TComponent>(overrides: true);
            }

            component.active = true;

            if (!AssetDatabase.Contains(component))
            {
                component.name = typeof(TComponent).Name;
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }

            return component;
        }
    }
}
