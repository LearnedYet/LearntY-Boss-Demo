using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Combat.Feel;
using AdaptiveBossArena.Core.Constants;
using AdaptiveBossArena.Core.Events;
using AdaptiveBossArena.Player;
using AdaptiveBossArena.Player.Controls;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 从基础模型生成玩家预制体，并连接引用。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 预制体序列化 GUID 引用，难以从差异中审查，资产移动还易悄然断裂。生成方式将结构表达为可读、
    /// 可审查、可重运行的代码，让坏引用成为编译错误而非运行时缺脚本。
    /// </para>
    /// <para>
    /// 角色刻意仅由胶囊组成。未完成感的占位美术让注意力集中在角色操控上，
    /// 这是此阶段唯一值得评估的内容。
    /// </para>
    /// </remarks>
    public static class PlayerPrefabBuilder
    {
        /// <summary>生成预制体的写入位置。</summary>
        public const string PrefabPath = "Assets/_Project/Prefabs/Player.prefab";

        private const float CapsuleHeight = 1.8f;
        private const float CapsuleRadius = 0.4f;

        /// <summary>略小于身体半径，要求攻击确实与角色重叠。</summary>
        /// <summary>骑士受击框半径，即剑尖超过后角色中心仍可被击中的距离。</summary>
        public const float HurtboxRadius = 0.38f;

        private const float StepOffset = 0.3f;
        private const float SkinWidth = 0.03f;

        private static readonly Color PlayerColor = new Color(0.35f, 0.75f, 0.95f);

        /// <summary>创建或替换玩家预制体。</summary>
        [MenuItem(EditorMenus.Setup + "Generate Player Prefab", priority = EditorMenus.SetupPriorityBuildScene - 1)]
        public static void GeneratePlayerPrefab()
        {
            GameObject root = BuildPlayerObject();

            try
            {
                AssetAuthoring.EnsureFolderExists("Assets/_Project/Prefabs");
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Adaptive Boss Arena] Player prefab generated at '{PrefabPath}'.");
        }

        /// <summary>在当前场景构建玩家层级，准备保存为预制体。</summary>
        private static GameObject BuildPlayerObject()
        {
            var root = new GameObject("Player")
            {
                layer = Layers.Player,
                tag = TagNames.Player
            };

            ConfigureCharacterController(root);
            BuildVisual(root.transform);
            BuildHurtbox(root.transform);
            AttackOverlayBuilder.Attach(root.transform);
            WireComponents(root);

            return root;
        }

        /// <summary>添加并调整负责移动和碰撞的 CharacterController。</summary>
        private static void ConfigureCharacterController(GameObject root)
        {
            CharacterController controller = root.AddComponent<CharacterController>();

            controller.height = CapsuleHeight;
            controller.radius = CapsuleRadius;
            controller.center = new Vector3(0f, CapsuleHeight * 0.5f, 0f);
            controller.stepOffset = StepOffset;
            controller.skinWidth = SkinWidth;

            // 容差为零会让控制器在竞技场墙段接缝处抖动。
            controller.minMoveDistance = 0f;
        }

        /// <summary>添加占位身体和标示朝向的鼻部标记。</summary>
        private static void BuildVisual(Transform parent)
        {
            var visualRoot = new GameObject("Visual");
            visualRoot.transform.SetParent(parent, false);

            // 提供蒙皮模型时优先使用，否则采用直立戴盔轮廓而非胶囊。所有生成部件无碰撞体：
            // 根对象 CharacterController 负责碰撞，受击框负责伤害，身体仅供观看，不影响战斗时机。
            // 发光面罩替代此前标示朝向的白色立方体。
            var animationConfig =
                GeneratedAssets.Config<CharacterAnimationConfig>("DefaultPlayerAnimation");

            if (!SilhouetteBuilder.TryBuildRig(
                    visualRoot.transform, animationConfig, SilhouetteBuilder.KnightRigMaterial(), out Animator rig))
            {
                SilhouetteBuilder.BuildKnight(visualRoot.transform, CapsuleHeight, CapsuleRadius);
            }

            // 挂在视觉根对象，使其着色影响下方所有渲染器，包括以后替换基础模型的美术。
            visualRoot.AddComponent<HitFlash>();

            // 程序动画移动整个视觉根对象，包括身体、标记和轨迹；碰撞体及受击框保留在上方角色根对象，
            // 挥击仅影响外观，命中框不随其移动。
            visualRoot.AddComponent<CharacterAnimator>();

            // 暂未启用的角色美术接入点：根对象有蒙皮模型子对象后，用同一状态流驱动骨骼 Animator。
            // 玩家为基础模型时不操作。
            visualRoot.AddComponent<CharacterAnimationBridge>();

            Transform socket = BuildWeaponSocket(visualRoot.transform, rig);

            // 沿剑刃从护手外侧到尖端布置，让拖影就是钢刃扫过的弧线。使用冷钢灰，
            // 挥击应像金属移动而非彩色丝带。
            WeaponTrailBuilder.Attach(socket, Vector3.zero, baseDistance: 0.25f, tipDistance: 0.95f,
                new Color(0.42f, 0.44f, 0.48f, 1f));

            // 判定斩击的刀刃，跨度与轨迹一致，所见挥动即实际打击。仅在有骨架且手部实际带动挂点时添加。
            if (rig != null)
            {
                StrikeVolumeBuilder.Attach(socket, Combat.StrikerParts.Weapon, 0.25f, 0.95f, 0.08f);
            }
        }

        /// <summary>在占位手位置添加供武器模型安装的空挂点。</summary>
        /// <remarks>
        /// 当前为空且不可见，是武器美术接入点。大致放在右手握柄位置，即使尚无骨架可挂到真实手骨，
        /// 导入剑刃也会合理。参见 <see cref="Combat.Feel.WeaponSocket"/>.
        /// </remarks>
        /// <param name="visualRoot">角色视觉根对象。</param>
        /// <param name="rig">构建了蒙皮身体时为其 Animator，否则为 null。</param>
        /// <returns>挂点 Transform。</returns>
        private static Transform BuildWeaponSocket(Transform visualRoot, Animator rig)
        {
            var socket = new GameObject("WeaponSocket");

            // 有骨架时剑放右手，跟随片段每次挥动；生成身体没有手，就放在手应在的位置。
            Transform hand = rig != null && rig.isHuman ? rig.GetBoneTransform(HumanBodyBones.RightHand) : null;

            if (hand != null)
            {
                socket.transform.SetParent(hand, false);
                socket.transform.localPosition = Vector3.zero;
                socket.transform.rotation = Art.ArmsBuilder.GripRotation(rig, right: true);

                // 骑士使用剑盾，片段抬左前臂格挡；没有盾就像空拳防守。使用风化涂装木材而非金属，
                // 因为任何金属响应都会让平背面反射明亮探针，骑士转身时像浅色卡片。
                Art.ArmsBuilder.MountShield(rig, MaterialLibrary.GetOrCreateTexturedSurface(
                    "KnightShieldPainted",
                    Art.ArmsBuilder.ShieldFace(Art.ArmsBuilder.ShieldWidth, Art.ArmsBuilder.ShieldHeight),
                    smoothness: 0.18f));
            }
            else
            {
                socket.transform.SetParent(visualRoot, false);
                socket.transform.localPosition =
                    new Vector3(CapsuleRadius * 1.2f, CapsuleHeight * 0.55f, CapsuleRadius * 1.1f);
            }

            socket.AddComponent<WeaponSocket>();

            return socket.transform;
        }

        /// <summary>添加接收首领攻击的触发体积。</summary>
        private static void BuildHurtbox(Transform parent)
        {
            var hurtboxObject = new GameObject("Hurtbox")
            {
                layer = Layers.PlayerHurtbox
            };

            hurtboxObject.transform.SetParent(parent, false);

            CapsuleCollider collider = hurtboxObject.AddComponent<CapsuleCollider>();
            collider.isTrigger = true;
            collider.height = CapsuleHeight;
            collider.radius = HurtboxRadius;
            collider.center = new Vector3(0f, CapsuleHeight * 0.5f, 0f);

            hurtboxObject.AddComponent<Hurtbox>();
        }

        /// <summary>添加玩法组件并解析资产引用。</summary>
        private static void WireComponents(GameObject root)
        {
            PlayerInputReader inputReader = root.AddComponent<PlayerInputReader>();
            PlayerController controller = root.AddComponent<PlayerController>();

            // 脚步随实际移动节奏触发，每步从石材扬起少量尘土。玩家脚步轻快，尘量也较小。
            root.AddComponent<Combat.Feel.FootstepEmitter>()
                .Configure(
                    Game.AudioService.Cues.FootstepPlayer, strideLength: 1.5f,
                    MaterialLibrary.GetOrCreateImpactMatter(), dustSize: 0.3f);

            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsGenerator.AssetPath);
            if (actions != null)
            {
                inputReader.SetActionsAsset(actions);
            }
            else
            {
                Debug.LogWarning(
                    "[Adaptive Boss Arena] Input actions asset not found. Run " +
                    "'Generate Input Actions' and regenerate the prefab.");
            }

            PlayerConfig config = GeneratedAssets.Config<PlayerConfig>("DefaultPlayerConfig");

            if (config != null)
            {
                controller.SetConfig(config);
            }

            var animator = root.GetComponentInChildren<CharacterAnimator>();
            var animationConfig =
                GeneratedAssets.Config<CharacterAnimationConfig>("DefaultPlayerAnimation");

            if (animator != null && animationConfig != null)
            {
                animator.SetConfig(animationConfig);
            }

            controller.SetChannels(
                GeneratedAssets.EventChannel<FloatEventChannel>("OnPlayerHealthChanged"),
                GeneratedAssets.EventChannel<FloatEventChannel>("OnPlayerStaminaChanged"),
                GeneratedAssets.EventChannel<VoidEventChannel>("OnPlayerDied"),
                GeneratedAssets.EventChannel<VoidEventChannel>("OnPerfectDodge"),
                GeneratedAssets.EventChannel<VoidEventChannel>("OnDeflect"),
                GeneratedAssets.EventChannel<FloatEventChannel>("OnPlayerPostureChanged"),
                GeneratedAssets.EventChannel<StringEventChannel>("OnWeaponDrawn"),
                GeneratedAssets.EventChannel<FloatEventChannel>("OnPlayerFocusChanged"),
                GeneratedAssets.EventChannel<StringEventChannel>("OnWeaponSwingCue"));
        }

        /// <summary>用共享生成材质为基础模型设置颜色。</summary>
        private static void ApplyColor(GameObject target, Color color) =>
            MaterialLibrary.Apply(target, $"Player_{ColorUtility.ToHtmlStringRGB(color)}", color);

    }
}
