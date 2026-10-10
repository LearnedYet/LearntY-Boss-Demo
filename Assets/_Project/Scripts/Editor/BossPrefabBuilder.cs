using AdaptiveBossArena.AI;
using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Combat.Feel;
using AdaptiveBossArena.Core.Constants;
using AdaptiveBossArena.Core.Events;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 从基础模型生成首领预制体，并连接引用。
    /// </summary>
    /// <remarks>
    /// 首领明显大于玩家，这不是装饰：玩家凭轮廓迅速判断距离，其攻击也按此身体尺寸调节。
    /// </remarks>
    public static class BossPrefabBuilder
    {
        /// <summary>生成预制体的写入位置。</summary>
        public const string PrefabPath = "Assets/_Project/Prefabs/Boss.prefab";

        private const float BodyHeight = 2.8f;
        private const float BodyRadius = 0.9f;
        private const float HurtboxRadius = 0.85f;

        // 弱点覆盖巨兽轮廓已有的发光核心，并刻意比美术更宽：战斗速度下，
        // 0.3 米球体会变成找像素的小游戏。值得要求玩家决定的是站位，而不是瞄准精度。
        // 这两个数值应与 SilhouetteBuilder 的 Core 部位一起理解，后者使用相同的高度和半径比例定位。
        private const float WeakPointRadius = 0.45f;
        private const float WeakPointHeightFraction = 0.63f;
        private const float WeakPointForwardFraction = 0.56f;

        /// <summary>命中核心时的伤害倍率。</summary>
        /// <remarks>
        /// 使完整轻攻击连击命中核心明显优于侧面，又不让其他打法失去意义。
        /// 代价是首领朝向玩家时核心才面对玩家，额外伤害需要承担风险。
        /// </remarks>
        private const float WeakPointMultiplier = 1.6f;

        private static readonly Color BossColor = new Color(0.85f, 0.25f, 0.3f);
        private static readonly Color WeakPointColor = new Color(1f, 0.85f, 0.3f);

        /// <summary>创建或替换首领预制体。</summary>
        [MenuItem(EditorMenus.Setup + "Generate Boss Prefab", priority = EditorMenus.SetupPriorityBuildScene - 1)]
        public static void GenerateBossPrefab()
        {
            GameObject root = BuildBossObject();

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
            Debug.Log($"[Adaptive Boss Arena] Boss prefab generated at '{PrefabPath}'.");
        }

        private static GameObject BuildBossObject()
        {
            var root = new GameObject("Boss")
            {
                layer = Layers.Boss,
                tag = TagNames.Boss
            };

            ConfigureCharacterController(root);
            BuildVisual(root.transform);
            BuildHurtboxes(root.transform);
            AttackOverlayBuilder.Attach(root.transform);
            WireComponents(root);

            return root;
        }

        private static void ConfigureCharacterController(GameObject root)
        {
            CharacterController controller = root.AddComponent<CharacterController>();

            controller.height = BodyHeight;
            controller.radius = BodyRadius;
            controller.center = new Vector3(0f, BodyHeight * 0.5f, 0f);
            controller.stepOffset = 0.4f;
            controller.skinWidth = 0.04f;
            controller.minMoveDistance = 0f;
        }

        private static void BuildVisual(Transform parent)
        {
            var visualRoot = new GameObject("Visual");
            visualRoot.transform.SetParent(parent, false);

            // 提供蒙皮模型时优先使用，否则生成宽、低、带角的轮廓。必须从竞技场远端就能看清朝向，
            // 因为玩家据此判断攻击是否指向自己。前方发光核心承担这个任务，也成为阶段光环的可见来源，
            // 避免光环看起来凭空出现。
            var animationConfig =
                GeneratedAssets.Config<CharacterAnimationConfig>("DefaultBossAnimation");

            if (SilhouetteBuilder.TryBuildRig(
                    visualRoot.transform, animationConfig, SilhouetteBuilder.BruteRigMaterial(), out Animator rig))
            {
                SilhouetteBuilder.AttachBruteCore(rig, BodyRadius * 0.34f);

                // 巨兽片段来自双手巨剑动作集，此前却没有真正挥动的武器。
                Art.ArmsBuilder.MountCleaver(
                    rig,
                    MaterialLibrary.GetOrCreateSurface("BruteCleaver", new Color(0.3f, 0.28f, 0.27f), metallic: 0.9f, smoothness: 0.45f),
                    MaterialLibrary.GetOrCreateSurface("BruteGrip", new Color(0.12f, 0.08f, 0.06f), metallic: 0.1f, smoothness: 0.25f));
            }
            else
            {
                SilhouetteBuilder.BuildBrute(visualRoot.transform, BodyHeight, BodyRadius);
            }

            visualRoot.AddComponent<HitFlash>();

            // 采用与玩家相同的动画结构，通过首领动画配置调得更沉重，让挥击更有质量感。仅移动视觉对象，
            // 受击框仍留在根对象。
            visualRoot.AddComponent<CharacterAnimator>();

            // 暂未启用的角色美术接入点：根对象有蒙皮模型子对象后，用同一状态流驱动骨骼 Animator。
            // 首领为基础模型时不执行任何操作。
            visualRoot.AddComponent<CharacterAnimationBridge>();

            // 从内部照亮首领，起初暗淡，各阶段逐渐炽热，让升级变化显现在角色身上而非仅体现在伤害上。
            visualRoot.AddComponent<PhaseAura>().SetEmberMaterial(MaterialLibrary.GetOrCreateImpactSparks());

            // 有砍刀时沿宽刀刃布置，无砍刀时放到基础模型手臂应在的位置。轨迹更长且略暖，
            // 表现首领挥击覆盖更广；拖影也是玩家判断是否会被击中的重要依据。
            Transform hand = rig != null && rig.isHuman ? rig.GetBoneTransform(HumanBodyBones.RightHand) : null;
            Transform cleaver = hand != null ? hand.Find("Cleaver") : null;
            var warmSteel = new Color(0.5f, 0.36f, 0.3f, 1f);

            if (cleaver != null)
            {
                WeaponTrailBuilder.Attach(cleaver, Vector3.zero, baseDistance: 0.45f, tipDistance: 1.6f, warmSteel);

                // 巨兽斩击使用的巨剑，范围与轨迹拖影一致。
                StrikeVolumeBuilder.Attach(cleaver, Combat.StrikerParts.Weapon, 0.45f, 1.6f, 0.14f);
            }
            else
            {
                WeaponTrailBuilder.Attach(
                    visualRoot.transform, new Vector3(BodyRadius * 0.8f, BodyHeight * 0.6f, 0f), 0.3f, 1.4f, warmSteel);
            }

            // 为踢击、抓取、跃击和狂暴徒手攻击配置拳、脚和身体。此身体的拳头较宽；身体球体代表躯干，
            // 用于落到骑士身上。没有骨架时不操作。
            StrikeVolumeBuilder.AttachLimbs(rig, fistRadius: 0.2f, footRadius: 0.2f, bodyRadius: 0.8f);
        }

        private static void BuildHurtboxes(Transform parent)
        {
            var hurtboxObject = new GameObject("Hurtbox")
            {
                layer = Layers.BossHurtbox
            };

            hurtboxObject.transform.SetParent(parent, false);

            CapsuleCollider collider = hurtboxObject.AddComponent<CapsuleCollider>();
            collider.isTrigger = true;
            collider.height = BodyHeight;
            collider.radius = HurtboxRadius;
            collider.center = new Vector3(0f, BodyHeight * 0.5f, 0f);

            hurtboxObject.AddComponent<Hurtbox>();

            BuildWeakPoint(parent);
        }

        /// <summary>
        /// 在首领前方核心上添加第二个伤害倍率更高的受击框。
        /// </summary>
        /// <remarks>
        /// 与身体受击框一样挂在根对象而非视觉对象：视觉对象在动画中倾斜起伏，
        /// 若命中框跟随待机动画移动，同一挥击在不同瞬间会有不同结果。
        /// <para>
        /// 它完全位于身体胶囊内，这是有意设计。正面挥击会同时找到两者，
        /// 而 <see cref="AttackHitDetector"/> 会让较高倍率优先。因此核心奖励玩家正面面对首领战斗，
        /// 同时也承受首领正面攻击的风险。
        /// </para>
        /// </remarks>
        private static void BuildWeakPoint(Transform parent)
        {
            var weakPointObject = new GameObject("WeakPoint")
            {
                layer = Layers.BossHurtbox
            };

            weakPointObject.transform.SetParent(parent, false);

            SphereCollider collider = weakPointObject.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = WeakPointRadius;
            collider.center = new Vector3(
                0f, BodyHeight * WeakPointHeightFraction, BodyRadius * WeakPointForwardFraction);

            var hurtbox = weakPointObject.AddComponent<Hurtbox>();

            using (AssetAuthoring.AssetWriter writer = AssetAuthoring.Edit(hurtbox))
            {
                writer.Float("_damageMultiplier", WeakPointMultiplier);
            }
        }

        private static void WireComponents(GameObject root)
        {
            BossController controller = root.AddComponent<BossController>();

            // 脚步比玩家更沉、更慢，让首领即使是基础模型也像一大团质量在竞技场移动，
            // 并且每步扬起更多尘土。
            root.AddComponent<Combat.Feel.FootstepEmitter>()
                .Configure(
                    Game.AudioService.Cues.FootstepBoss, strideLength: 2.4f,
                    MaterialLibrary.GetOrCreateImpactMatter(), dustSize: 0.5f, tremor: 0.07f);

            BossConfig config = GeneratedAssets.Config<BossConfig>("DefaultBossConfig");

            if (config != null)
            {
                controller.SetConfig(config);
            }

            var animator = root.GetComponentInChildren<CharacterAnimator>();
            var animationConfig =
                GeneratedAssets.Config<CharacterAnimationConfig>("DefaultBossAnimation");

            if (animator != null && animationConfig != null)
            {
                animator.SetConfig(animationConfig);
            }

            controller.SetChannels(
                GeneratedAssets.EventChannel<FloatEventChannel>("OnBossHealthChanged"),
                GeneratedAssets.EventChannel<IntEventChannel>("OnBossPhaseChanged"),
                GeneratedAssets.EventChannel<VoidEventChannel>("OnBossDefeated"),
                GeneratedAssets.EventChannel<StringEventChannel>("OnAdaptationAdopted"),
                GeneratedAssets.EventChannel<FloatEventChannel>("OnBossPostureChanged"),
                GeneratedAssets.EventChannel<VoidEventChannel>("OnBossOverbalanced"),
                GeneratedAssets.EventChannel<VoidEventChannel>("OnBossParried"));
        }
    }
}
