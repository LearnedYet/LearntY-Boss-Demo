using System.Collections.Generic;
using System.Linq;
using AdaptiveBossArena.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AdaptiveBossArena.Editor.Environment
{
    /// <summary>
    /// 烘焙教堂反射和反弹光，并在重建场景时重新关联已有烘焙数据。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 烘焙两类内容：反射探针让骑士甲胄和巨兽肩部反射石材与天空；
    /// 光照探针网格让站在日照地板上的角色接收暖色反弹光，区别于仅被照亮而未融入房间的身体。
    /// </para>
    /// <para>
    /// 烘焙需要 GPU，而设置流程无 GPU、无界面运行，因此烘焙单独执行：<see cref="BakeArenaLighting"/>，
    /// 可从菜单或未使用以下参数的批处理编辑器运行：<c>-nographics</c>。每次设置都从代码重建场景，
    /// 原本会丢失烘焙数据，因此 <see cref="PrepareScene"/> 以相同方式布置探针并重新关联已有烘焙。
    /// 在没有 GPU 的机器上设置仍保留上次结果。
    /// </para>
    /// <para>
    /// 灯光使用 Baked Indirect 模式的 Mixed：直接光和阴影保持实时，让阶段颜色直接作用于每个表面，
    /// 仅反弹光烘焙。
    /// </para>
    /// </remarks>
    public static class LightingBaker
    {
        /// <summary>照明设置和烘焙反射所在文件夹。</summary>
        public const string LightingFolder = "Assets/_Project/Lighting";

        /// <summary>烘焙反射立方体贴图。</summary>
        public const string ReflectionPath = LightingFolder + "/ArenaReflection.exr";

        private const string SettingsPath = LightingFolder + "/ArenaLighting.lighting";

        /// <summary>Unity 烘焙时写在场景旁边的照明数据。</summary>
        private const string LightingDataPath = EditorMenus.GeneratedSceneFolder + "/Arena/LightingData.asset";

        /// <summary>战斗地板上探针网格间距，单位为米。</summary>
        private const float ProbeSpacing = 4f;

        /// <summary>探针高度位于角色膝部和头部，符合身体采样位置。</summary>
        private static readonly float[] ProbeHeights = { 0.6f, 2.4f };

        /// <summary>布置探针网格、配置灯光、重连先前烘焙；由场景构建器调用。</summary>
        /// <param name="config">竞技场尺寸。</param>
        public static void PrepareScene(ArenaConfig config)
        {
            Lightmapping.lightingSettings = LoadOrCreateSettings();

            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                light.lightmapBakeType = LightmapBakeType.Mixed;
            }

            // 石材为烘焙贡献反弹光，但之后不接受探针照明。地板低于网格，墙柱在网格外；
            // 实测这些位置插值探针亮度约只有环境光的五分之二，让石材采样会使整座废墟变暗。
            // 探针用于站在网格内部的战斗角色。
            GameObject cathedral = GameObject.Find(CathedralBuilder.RootName);

            if (cathedral != null)
            {
                foreach (MeshRenderer renderer in cathedral.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.sharedMaterial != null && renderer.sharedMaterial.renderQueue >= (int)RenderQueue.Transparent)
                    {
                        continue;
                    }

                    GameObject stone = renderer.gameObject;
                    StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(stone);
                    GameObjectUtility.SetStaticEditorFlags(stone, flags | StaticEditorFlags.ContributeGI);
                    renderer.receiveGI = ReceiveGI.LightProbes;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                }
            }

            var probes = new GameObject("Light Probes").AddComponent<LightProbeGroup>();
            probes.probePositions = ProbeGrid(config.Radius).ToArray();

            RelinkReflection();

            var data = AssetDatabase.LoadAssetAtPath<LightingDataAsset>(LightingDataPath);

            if (data != null)
            {
                Lightmapping.lightingDataAsset = data;
            }
        }

        /// <summary>烘焙竞技场光照探针和反射探针，需要 GPU。</summary>
        [MenuItem(EditorMenus.Setup + "4. Bake Arena Lighting", priority = EditorMenus.SetupPriorityBuildScene + 1)]
        public static void BakeArenaLighting()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.LogError("[Adaptive Boss Arena] Baking lighting needs a GPU. Run the editor without -nographics.");

                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(2);
                }

                return;
            }

            EditorSceneManager.OpenScene(EditorMenus.ArenaScenePath, OpenSceneMode.Single);

            if (!Lightmapping.Bake())
            {
                Debug.LogError("[Adaptive Boss Arena] The light probe bake failed.");
                return;
            }

            ReflectionProbe probe = Object.FindAnyObjectByType<ReflectionProbe>();

            if (probe != null)
            {
                AssetAuthoring.EnsureFolderExists(LightingFolder);

                probe.mode = ReflectionProbeMode.Baked;

                if (!Lightmapping.BakeReflectionProbe(probe, ReflectionPath))
                {
                    Debug.LogError("[Adaptive Boss Arena] The reflection probe bake failed.");
                    return;
                }

                AssetDatabase.ImportAsset(ReflectionPath);
                RelinkReflection();
            }

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

            int probeCount = LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0;
            Debug.Log($"[Adaptive Boss Arena] Arena lighting baked: {probeCount} light probes, reflection at '{ReflectionPath}'.");
        }

        /// <summary>探针位置：按各采样高度布置方格网，并裁剪到战斗地板内。</summary>
        /// <param name="radius">战斗地板半径。</param>
        /// <returns>每次调用相同的世界位置。</returns>
        public static IEnumerable<Vector3> ProbeGrid(float radius)
        {
            int steps = Mathf.CeilToInt(radius / ProbeSpacing);

            foreach (float height in ProbeHeights)
            {
                for (int x = -steps; x <= steps; x++)
                {
                    for (int z = -steps; z <= steps; z++)
                    {
                        var point = new Vector3(x * ProbeSpacing, height, z * ProbeSpacing);

                        if (point.x * point.x + point.z * point.z <= radius * radius)
                        {
                            yield return point;
                        }
                    }
                }
            }
        }

        /// <summary>若存在烘焙立方体贴图，则将反射探针指向它。</summary>
        private static void RelinkReflection()
        {
            var cubemap = AssetDatabase.LoadAssetAtPath<Texture>(ReflectionPath);
            ReflectionProbe probe = Object.FindAnyObjectByType<ReflectionProbe>();

            if (cubemap == null || probe == null)
            {
                return;
            }

            // 使用 Custom 而非 Baked：Baked 探针纹理保存在重建场景会丢弃的照明数据中；
            // 自定义纹理是普通资产引用，可以保留。
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = cubemap;
        }

        private static LightingSettings LoadOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(SettingsPath);

            if (settings == null)
            {
                settings = new LightingSettings { name = "ArenaLighting" };
                AssetAuthoring.EnsureFolderExists(LightingFolder);
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            settings.indirectSampleCount = 256;
            settings.maxBounces = 2;
            settings.autoGenerate = false;
            EditorUtility.SetDirty(settings);

            return settings;
        }
    }
}
