using AdaptiveBossArena.Core.Constants;
using AdaptiveBossArena.Core.Events;
using AdaptiveBossArena.Core.Services;
using AdaptiveBossArena.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 根据以下配置以程序方式构建竞技场场景： <see cref="ArenaConfig"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 生成场景而不是手动摆放，可以让布局始终由配置资产决定；
    /// 调整竞技场半径后重新构建即可完成整个流程。这也让刚克隆的项目能够从源码重建场景，
    /// 因为场景文件难以合并，几乎无法在拉取请求中有效审查。
    /// </para>
    /// <para>
    /// 第一阶段生成竞技场外壳：地板、边界、灯光、摄像机，以及空的管理器根对象。
    /// 后续阶段生成战斗角色预制体时再添加角色。
    /// </para>
    /// </remarks>
    public static class ArenaSceneBuilder
    {
        private const string ArenaRootName = "Arena";
        private const string ManagersRootName = "--- Managers ---";
        private const string SpawnRootName = "Spawns";

        private const float FloorThickness = 0.1f;
        private const float WallThickness = 0.5f;

        private static readonly Color FloorColor = new Color(0.26f, 0.26f, 0.30f);
        private static readonly Color WallColor = new Color(0.17f, 0.17f, 0.20f);

        /// <summary>竞技场上方阴沉而带瘀色的天空。</summary>
        private static readonly Color SkyTint = new Color(0.47f, 0.31f, 0.36f);

        private static readonly Color SkyGroundColor = new Color(0.05f, 0.04f, 0.06f);

        /// <summary>保持低亮度，让天空衬托战斗而不喧宾夺主。</summary>
        /// <remarks>
        /// 后处理真正生效后进一步降低了亮度：调色提高曝光后，每扇窗外的天空都会变成苍白的强光。
        /// </remarks>
        private const float SkyExposure = 0.2f;

        /// <summary>创建竞技场场景，替换之前生成的场景。</summary>
        [MenuItem(EditorMenus.Setup + "3. Build Arena Scene", priority = EditorMenus.SetupPriorityBuildScene)]
        public static void BuildArenaScene()
        {
            ArenaConfig config = LoadArenaConfig();
            if (config == null)
            {
                return;
            }

            // 保存提示是模态对话框，构建机器无法回答。
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            // 在替换场景前解析引用。新建场景会卸载资产，之后即使资产确实存在且有效，
            // 查找也可能返回 null；这曾导致整个界面的引用全部为空。
            HudChannels channels = HudBuilder.ResolveChannels();
            var actions = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(
                InputActionsGenerator.AssetPath);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildArenaGeometry(config);
            Light directionalLight = BuildLighting(config);
            Environment.CathedralBuilder.BindSun(directionalLight);

            // 在所有灯光和石材创建完毕后处理探针网格、Mixed 灯光以及已有的烘焙数据。
            Environment.LightingBaker.PrepareScene(config);
            BuildSpawnMarkers(config);
            BuildManagers();

            GameObject player = SpawnCombatant(
                PlayerPrefabBuilder.PrefabPath, config.PlayerSpawn, "Player");

            GameObject boss = SpawnCombatant(
                BossPrefabBuilder.PrefabPath, config.BossSpawn, "Boss");

            FaceEachOther(player, boss);
            BuildCamera(config, player, boss);
            BuildInterface(config, actions, channels);

            Object.FindAnyObjectByType<ArenaAtmosphere>()?.Bind(directionalLight, channels.BossPhase);

            Object.FindAnyObjectByType<ScreenEffects>()?.Bind(
                Object.FindAnyObjectByType<UnityEngine.Rendering.Volume>(),
                channels.PlayerHealth, channels.Deflect);

            // 浏览器版本在场景启动时换用成本更低的配置和抗锯齿方式。
            Camera mainCamera = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
            Object.FindAnyObjectByType<PlatformLighting>()?.BindWebBudget(
                Object.FindAnyObjectByType<UnityEngine.Rendering.Volume>(),
                PostProcessingConfigurator.LoadWebProfile(),
                mainCamera != null ? mainCamera.GetComponent<UniversalAdditionalCameraData>() : null);

            AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedSceneFolder);
            EditorSceneManager.SaveScene(scene, EditorMenus.ArenaScenePath);
            AddSceneToBuildSettings(EditorMenus.ArenaScenePath);

            Debug.Log($"[Adaptive Boss Arena] Arena scene built at '{EditorMenus.ArenaScenePath}'.");
        }

        /// <summary>查找竞技场配置资产，并在缺失时给出明确提示。</summary>
        private static ArenaConfig LoadArenaConfig()
        {
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(ArenaConfig)}");

            if (guids.Length == 0)
            {
                Debug.LogError(
                    "[Adaptive Boss Arena] No ArenaConfig asset found. " +
                    "Run 'Adaptive Boss Arena/Setup/2. Generate Default Assets' first.");
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<ArenaConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        /// <summary>创建圆形地板及其外围的一圈墙段。</summary>
        private static void BuildArenaGeometry(ArenaConfig config)
        {
            var arenaRoot = new GameObject(ArenaRootName);
            arenaRoot.layer = Layers.Arena;

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "Floor";
            floor.layer = Layers.Arena;
            floor.transform.SetParent(arenaRoot.transform);

            // Unity 的圆柱体基础模型直径为一个单位，高度为两个单位，因此水平缩放应使用完整直径，
            // 竖直缩放应为厚度的一半。若按半径缩放，地板就会只有预期尺寸的一半，
            // 看起来像墙摆错了位置而不是地板太小。
            floor.transform.localScale =
                new Vector3(config.Radius * 2f, FloorThickness * 0.5f, config.Radius * 2f);
            floor.transform.position = new Vector3(0f, -FloorThickness * 0.5f, 0f);
            MaterialLibrary.Apply(floor, "ArenaFloor", FloorColor);

            ReplaceFloorCollider(floor, config);
            BuildWalls(config, arenaRoot.transform);
            Environment.CathedralBuilder.Build(config, arenaRoot.transform);
            BuildEnvironment(config, arenaRoot.transform);
        }

        /// <summary>
        /// 实例化可选的场景装饰预制体，并按需隐藏占位网格。
        /// </summary>
        /// <remarks>
        /// 背景美术的接入点。当前为 null，因此竞技场仍使用程序生成的地板和墙环。提供预制体后，
        /// 将它叠加布置到场景上；程序网格的渲染器可以选择隐藏，
        /// 但隐藏处理必须在 <em>之前</em> 任何环境对象添加之前完成，确保只隐藏占位物。
        /// 所有碰撞体始终保持启用，无论导入的场景美术如何变化，都保留已验证的战斗边界。
        /// </remarks>
        private static void BuildEnvironment(ArenaConfig config, Transform arenaRoot)
        {
            if (config.EnvironmentPrefab == null)
            {
                return;
            }

            if (config.HideProceduralMeshesWithEnvironment)
            {
                // 碰撞体是独立组件，保持启用；只移除可见网格。
                foreach (MeshRenderer renderer in arenaRoot.GetComponentsInChildren<MeshRenderer>(true))
                {
                    renderer.enabled = false;
                }
            }

            // 在隐藏处理之后实例化，因此不会触碰环境本身的渲染器。
            var environment = (GameObject)PrefabUtility.InstantiatePrefab(
                config.EnvironmentPrefab, arenaRoot);
            environment.transform.localPosition = Vector3.zero;
            environment.transform.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// 将圆柱体基础模型的胶囊碰撞体换成平坦的盒形碰撞体。
        /// </summary>
        /// <remarks>
        /// <para>
        /// Unity 的圆柱体基础模型默认带有 <see cref="CapsuleCollider"/>而不是网格碰撞体；
        /// 胶囊碰撞体无法正确承受非均匀缩放。缩放成宽而薄的圆盘时，其半径取最大的水平轴，高度取竖直轴；
        /// 高度远小于半径的两倍时，胶囊会退化成一个宽达竞技场一半的球体。
        /// </para>
        /// <para>
        /// 结果是地板看起来平坦，碰撞却像一个隐形穹顶：角色站在顶上，沿曲面滑落并掉出世界。
        /// 场景外观完全正常，因此值得在这里详细解释。
        /// </para>
        /// <para>
        /// 应替换为盒形碰撞体，而不是网格碰撞体。墙环限定了可玩区域，角色无法到达盒子的角落；
        /// 基础碰撞体的检测成本也远低于网格碰撞体。
        /// </para>
        /// </remarks>
        private static void ReplaceFloorCollider(GameObject floor, ArenaConfig config)
        {
            Object.DestroyImmediate(floor.GetComponent<Collider>());

            var colliderObject = new GameObject("FloorCollider")
            {
                layer = Layers.Arena
            };

            colliderObject.transform.SetParent(floor.transform.parent, worldPositionStays: false);
            colliderObject.transform.localPosition = new Vector3(0f, -FloorThickness * 0.5f, 0f);

            BoxCollider box = colliderObject.AddComponent<BoxCollider>();
            box.size = new Vector3(config.Radius * 2f, FloorThickness, config.Radius * 2f);
        }

        /// <summary>用一圈盒形墙段近似圆形边界。</summary>
        /// <summary>
        /// 在竞技场中心放置一个探针。
        /// </summary>
        /// <remarks>
        /// 骑士甲胄的金属度为 0.85，巨兽肩部为 0.9；场景没有探针时，两者只能反射单调的天空。
        /// 无法反射周围环境的金属看起来像灰色塑料，这会破坏依靠轮廓辨识的角色使用金属材质的意义。
        /// 一个覆盖战斗地板的探针即可让它们反射墙壁、立柱和火光。
        /// </remarks>
        private static void BuildReflectionProbe(ArenaConfig config)
        {
            var probeObject = new GameObject("Reflection Probe");
            probeObject.transform.position = new Vector3(0f, config.WallHeight * 0.6f, 0f);

            ReflectionProbe probe = probeObject.AddComponent<ReflectionProbe>();

            // 使用烘焙而非实时探针：竞技场没有需要反射追踪的移动对象；实时探针需要六次摄像机渲染，
            // 却没有可见收益。
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;
            probe.size = new Vector3(config.Radius * 2.4f, config.WallHeight * 4f, config.Radius * 2.4f);
            probe.resolution = 256;
            probe.hdr = true;
            probe.shadowDistance = 0f;
        }

        private static void BuildWalls(ArenaConfig config, Transform parent)
        {
            var wallRoot = new GameObject("Walls");
            wallRoot.transform.SetParent(parent);

            int segments = Mathf.Max(3, config.WallSegments);
            float degreesPerSegment = 360f / segments;

            // 单个墙段的弦长；略微加宽，使相邻墙段重叠，避免留下高速移动角色能够挤过的缝隙。
            float segmentWidth = 2f * config.Radius * Mathf.Sin(Mathf.PI / segments) * 1.05f;

            for (int i = 0; i < segments; i++)
            {
                float angleDegrees = i * degreesPerSegment;
                float angleRadians = angleDegrees * Mathf.Deg2Rad;

                GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = $"Wall_{i:D2}";
                wall.layer = Layers.Arena;
                wall.transform.SetParent(wallRoot.transform);

                float radialOffset = config.Radius + WallThickness * 0.5f;
                wall.transform.position = new Vector3(
                    Mathf.Cos(angleRadians) * radialOffset,
                    config.WallHeight * 0.5f,
                    Mathf.Sin(angleRadians) * radialOffset);

                // 旋转每个墙段，使其朝向竞技场中心。
                wall.transform.rotation = Quaternion.Euler(0f, -angleDegrees, 0f);
                wall.transform.localScale = new Vector3(WallThickness, config.WallHeight, segmentWidth);

                MaterialLibrary.Apply(wall, "ArenaWall", WallColor);
            }
        }

        /// <summary>添加一盏带倾角的方向光，让基础模型的形体清晰可辨。</summary>
        /// <param name="config">竞技场配置，用于确定反射探针尺寸。</param>
        /// <returns>方向光，供氛围系统按阶段调整颜色。</returns>
        private static Light BuildLighting(ArenaConfig config)
        {
            var lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();

            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;

            // 光线从大教堂开放的交叉部向下照射，与光柱的绘制方向一致。
            lightObject.transform.rotation = Environment.CathedralBuilder.SunRotation;

            BuildReflectionProbe(config);

            // 天空仅作背景：环境光保持为氛围控制器按阶段驱动的三色 Trilight，
            // 允许调整天空美术而不把竞技场的照明氛围交给它决定。
            RenderSettings.skybox = MaterialLibrary.GetOrCreateSkybox(
                "ArenaSky", SkyTint, SkyGroundColor, SkyExposure);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.22f, 0.24f, 0.30f);
            RenderSettings.ambientEquatorColor = new Color(0.14f, 0.14f, 0.17f);
            RenderSettings.ambientGroundColor = new Color(0.06f, 0.06f, 0.08f);

            // 在场景中设置，而不是仅在运行时设置：若构建所含场景都未启用雾，URP 会剔除雾的着色器变体，
            // 最终玩家版本中便会悄然没有薄雾。
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = ArenaAtmosphere.FogStart;
            RenderSettings.fogEndDistance = ArenaAtmosphere.FogEnd;
            RenderSettings.fogColor = HazeColor.Derive(
                RenderSettings.ambientEquatorColor, light.color, light.intensity);

            return light;
        }

        /// <summary>
        /// 创建以机架为父对象、摄像机为子对象的摄像机结构。
        /// </summary>
        /// <remarks>
        /// 拆分的意义在于：父对象负责构图，子对象负责震动。若两者共用一个 Transform，
        /// 跟随平滑会追赶震动偏移，将清脆的冲击变成持续晃动。
        /// </remarks>
        private static void BuildCamera(ArenaConfig config, GameObject player, GameObject boss)
        {
            var rigObject = new GameObject("Camera Rig");
            rigObject.transform.position = new Vector3(0f, config.CameraHeight, -config.CameraDistance);
            rigObject.transform.rotation = Quaternion.Euler(config.CameraPitchDegrees, 0f, 0f);

            ArenaCameraRig rig = rigObject.AddComponent<ArenaCameraRig>();
            rig.SetConfig(config);

            // 构图从玩家位置向首领偏移，确保两者之间承载关键战斗信息的空间始终留在屏幕内。
            rig.SetTargets(
                player != null ? player.transform : null,
                boss != null ? boss.transform : null);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.SetParent(rigObject.transform, false);

            Camera camera = cameraObject.AddComponent<Camera>();

            // 此前摄像机仅清为纯色，世界看起来在墙顶处戛然而止。
            // 保留背景色作为无法找到天空着色器时的回退。
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = new Color(0.04f, 0.04f, 0.06f);
            camera.fieldOfView = 50f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 200f;

            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<CameraShaker>();

            // URP 除了全局配置，还必须逐个摄像机启用后处理；
            // 遗漏此开关会让配置正确的 Volume Profile 完全不生效。
            UniversalAdditionalCameraData cameraData =
                cameraObject.GetComponent<UniversalAdditionalCameraData>()
                ?? cameraObject.AddComponent<UniversalAdditionalCameraData>();

            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            BuildPostProcessingVolume();
        }

        /// <summary>添加使用生成的后处理配置的全局 Volume。</summary>
        private static void BuildPostProcessingVolume()
        {
            UnityEngine.Rendering.VolumeProfile profile = PostProcessingConfigurator.LoadProfile();

            if (profile == null)
            {
                Debug.LogWarning(
                    "[Adaptive Boss Arena] No post-processing profile found; the arena will render " +
                    "without bloom or grading. Run 'Configure Post Processing'.");
                return;
            }

            var volumeObject = new GameObject("Global Volume");
            var volume = volumeObject.AddComponent<UnityEngine.Rendering.Volume>();

            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = profile;
        }

        /// <summary>添加负责创建服务、驱动时钟并承载调试工具的对象。</summary>
        private static void BuildManagers()
        {
            var managersRoot = new GameObject(ManagersRootName);

            managersRoot.AddComponent<GameBootstrapper>();
            managersRoot.AddComponent<AdaptationDebugOverlay>();

            // F3 面板，以及用于衡量每个改进阶段的无人值守采样。
            managersRoot.AddComponent<PerfProbe>();

            // 测量战斗表现后升降一次浏览器画质，除非玩家已自行选择。
            managersRoot.AddComponent<QualityDirector>();

            // 通过 -debugHits 或 ?debughits 请求时绘制攻击部位和受击框；默认关闭。
            managersRoot.AddComponent<HitboxDebugView>().SetMaterial(MaterialLibrary.GetOrCreateAttackOverlay());
            managersRoot.AddComponent<EncounterDirector>();
            // 已下载录音资源时，按音效标识逐项替换合成声音。
            AudioAssetBuilder.Assign(managersRoot.AddComponent<AudioService>());
            managersRoot.AddComponent<CombatAudioDirector>();
            managersRoot.AddComponent<CombatEffectsDirector>();
            managersRoot.AddComponent<ArenaAtmosphere>();
            managersRoot.AddComponent<ScreenEffects>();

            // 池化首领重攻击留下的地面危险区域。首领在启动时按类型查找该对象；缺失时仅不留下地面伤痕，
            // 因此测试场景和精简场景都可正常使用。
            managersRoot.AddComponent<Combat.HazardField>().SetMaterial(MaterialLibrary.GetOrCreateHazardDisc());
        }

        /// <summary>
        /// 构建 HUD 并将其连接到遭遇控制器。
        /// </summary>
        /// <remarks>
        /// 最后构建，因为需要连接的遭遇控制器必须在战斗角色已存在时进行自动查找。
        /// </remarks>
        private static void BuildInterface(
            ArenaConfig config,
            UnityEngine.InputSystem.InputActionAsset actions,
            HudChannels channels)
        {
            HudReferences hud = HudBuilder.Build(actions, channels);

            // 摄像机使用与玩家相同的生成动作来环绕、锁定和切换视角；仅在没有菜单需要鼠标时捕获鼠标。
            ArenaCameraRig rig = Object.FindAnyObjectByType<ArenaCameraRig>();

            if (rig != null)
            {
                rig.SetInputActions(actions);
                rig.gameObject.AddComponent<CursorLock>().Bind(hud.PauseMenu, hud.EndScreen, hud.SettingsMenu);

                // 锁定时在巨兽上方显示一个环，使用与其他文字一致的骨白色。
                var boss = Object.FindAnyObjectByType<AI.BossController>();
                Transform canvasRoot = hud.RoundIntro.transform;
                var marker = new GameObject("LockOnReticle", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                marker.transform.SetParent(canvasRoot, false);
                var markerRect = (RectTransform)marker.transform;
                markerRect.sizeDelta = new Vector2(44f, 44f);
                marker.GetComponent<UnityEngine.UI.Image>().color = new Color(0.84f, 0.77f, 0.66f, 0.9f);
                marker.SetActive(false);

                canvasRoot.gameObject.AddComponent<LockOnReticle>()
                    .Bind(rig, boss != null ? boss.transform : null, markerRect);
            }

            var director = Object.FindAnyObjectByType<EncounterDirector>();

            if (director == null)
            {
                Debug.LogWarning(
                    "[Adaptive Boss Arena] No EncounterDirector found, so the outcome screens will " +
                    "not be driven.");
                return;
            }

            director.Bind(
                config, hud.EndScreen, hud.PauseMenu, hud.RoundIntro,
                channels.PlayerDied, channels.BossDefeated, channels.Deflect, channels.PerfectDodge,
                channels.BossParried, hud.RipostePrompt);

            var audioDirector = Object.FindAnyObjectByType<CombatAudioDirector>();
            audioDirector?.Bind(
                channels.Deflect, channels.PerfectDodge, channels.BossPhase, channels.PlayerFocus,
                channels.Overbalance, channels.WeaponSwingCue);

            var effectsDirector = Object.FindAnyObjectByType<CombatEffectsDirector>();
            effectsDirector?.Bind(
                channels.PerfectDodge, channels.BossPhase, channels.Overbalance,
                MaterialLibrary.GetOrCreateImpactSparks(), MaterialLibrary.GetOrCreateImpactMatter(),
                MaterialLibrary.GetOrCreateBloodSplatter());
        }

        /// <summary>在出生点放置生成的战斗角色预制体。</summary>
        private static GameObject SpawnCombatant(string prefabPath, Vector3 position, string label)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab == null)
            {
                Debug.LogWarning(
                    $"[Adaptive Boss Arena] {label} prefab not found at '{prefabPath}', so it has been " +
                    "left out of the scene. Generate the prefab and rebuild.");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = position;

            return instance;
        }

        /// <summary>让双方在战斗开始时面向彼此。</summary>
        private static void FaceEachOther(GameObject player, GameObject boss)
        {
            if (player == null || boss == null)
            {
                return;
            }

            Vector3 between = boss.transform.position - player.transform.position;
            between.y = 0f;

            if (between.sqrMagnitude < Mathf.Epsilon)
            {
                return;
            }

            player.transform.rotation = Quaternion.LookRotation(between, Vector3.up);
            boss.transform.rotation = Quaternion.LookRotation(-between, Vector3.up);
        }

        /// <summary>在配置的出生位置创建空标记对象。</summary>
        private static void BuildSpawnMarkers(ArenaConfig config)
        {
            var spawnRoot = new GameObject(SpawnRootName);

            var playerSpawn = new GameObject("PlayerSpawn");
            playerSpawn.transform.SetParent(spawnRoot.transform);
            playerSpawn.transform.position = config.PlayerSpawn;

            var bossSpawn = new GameObject("BossSpawn");
            bossSpawn.transform.SetParent(spawnRoot.transform);
            bossSpawn.transform.position = config.BossSpawn;
        }

        /// <summary>若场景尚未登记，则将其加入构建设置列表。</summary>
        private static void AddSceneToBuildSettings(string scenePath)
        {
            EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;

            foreach (EditorBuildSettingsScene scene in existing)
            {
                if (scene.path == scenePath)
                {
                    return;
                }
            }

            var updated = new EditorBuildSettingsScene[existing.Length + 1];
            existing.CopyTo(updated, 0);
            updated[existing.Length] = new EditorBuildSettingsScene(scenePath, true);

            EditorBuildSettings.scenes = updated;
        }
    }
}
