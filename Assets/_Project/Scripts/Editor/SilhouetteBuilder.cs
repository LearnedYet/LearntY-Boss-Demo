using UnityEngine;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 用基础模型构建双方身体。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 胶囊只是形状，不是角色。用相同基础模型组合出清晰轮廓：戴盔骑士和巨大有角巨兽，
    /// 因为项目由代码生成资产，多关节身体只比单胶囊多少量部件。
    /// </para>
    /// <para>
    /// 所有部件挂在程序动画整体移动的视觉根对象下，因此多部件身体免费继承已有突进、倾斜、
    /// 下蹲和倒地效果。上方根对象的控制器与受击框不变，不改变任何战斗帧时机。
    /// </para>
    /// <para>
    /// 轮廓是重点。在暗地板上由一盏方向光照明，一眼读到的是外形：骑士窄、直立、带盔饰，巨兽宽、低、带角。
    /// 颜色作用远小于形状。
    /// </para>
    /// </remarks>
    internal static class SilhouetteBuilder
    {
        /// <summary>
        /// 提供蒙皮模型时，在视觉根对象下实例化。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 真实下载美术的接入路径。由配置资产驱动，而非手动放入预制体；构建器每次重写完整层级，
        /// 直接添加内容会在下次设置被销毁，这正是旧接入指南的陷阱。
        /// </para>
        /// <para>
        /// 移除导入对象上的碰撞体。下载角色常带碰撞体，放在 CharacterController 内会互相干扰。
        /// </para>
        /// </remarks>
        /// <param name="visualRoot">模型挂载的视觉根对象。</param>
        /// <param name="config">携带骨架、控制器和缩放的角色动画配置。</param>
        /// <param name="bodyMaterial">绘制骨架全部表面的材质。</param>
        /// <param name="animator">用于按角色查找骨骼的 Animator。</param>
        /// <returns>已实例化骨架、应跳过生成身体时返回 true。</returns>
        public static bool TryBuildRig(
            Transform visualRoot,
            Combat.Feel.CharacterAnimationConfig config,
            Material bodyMaterial,
            out Animator animator)
        {
            animator = null;

            if (config == null || config.RigPrefab == null)
            {
                return false;
            }

            var instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(config.RigPrefab, visualRoot);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * config.RigScale;

            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }

            animator = instance.GetComponentInChildren<Animator>(true);

            if (animator != null)
            {
                animator.runtimeAnimatorController = config.AnimatorController;

                // 运动控制器移动角色，片段绝不可移动。关闭根运动，离屏也动画更新，
                // 避免摄像机转回时姿势突然跳变。
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                FaceRigForward(animator, instance.transform, visualRoot);

                // 死亡前休眠。在此构建，确保骑士和巨兽从同一表获得相同身体，并使用与骨架相同缩放。
                Art.RagdollBuilder.Build(animator, visualRoot.gameObject, config.RigScale);
            }

            // 导入模型自带材质只是占位且未导入，否则所有表面使用引擎默认。
            // 身体采用了解渲染管线与美术方向的生成角色材质。
            if (bodyMaterial != null)
            {
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = new Material[renderer.sharedMaterials.Length == 0 ? 1 : renderer.sharedMaterials.Length];

                    for (int i = 0; i < materials.Length; i++)
                    {
                        materials[i] = bodyMaterial;
                    }

                    renderer.sharedMaterials = materials;
                }
            }

            return true;
        }

        /// <summary>
        /// 旋转附加骨架，使身体面向角色前方。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 美术工具对前向定义不同。Quaternius 人偶面向 -Z，不旋转安装会让双方身体背对实际朝向：
        /// 首领向后挥刀却在前方命中。运动控制器、命中框、首领感知都使用角色根前向，因此必须对齐身体。
        /// </para>
        /// <para>
        /// 使用实测而非假设：绑定姿势的髋部给出身体朝向，转动骨架匹配，并吸附到四分之一圈，
        /// 避免轻微不对称骨盆导致偏斜。不同朝向制作的模型也能自动对齐，无需记得手改。
        /// </para>
        /// </remarks>
        /// <param name="animator">骨架的 Humanoid Animator。</param>
        /// <param name="rig">骨架实例根对象。</param>
        /// <param name="visualRoot">其前向即角色前向的视觉根对象。</param>
        private static void FaceRigForward(Animator animator, Transform rig, Transform visualRoot)
        {
            if (!animator.isHuman)
            {
                return;
            }

            animator.Rebind();

            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);

            if (left == null || right == null)
            {
                return;
            }

            // 面向 +Z 的身体右侧为 +X，+X cross +Y 为 +Z，即右向叉乘上向得到朝向。
            Vector3 facing = Vector3.Cross(right.position - left.position, Vector3.up);
            facing.y = 0f;

            if (facing.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float yaw = Vector3.SignedAngle(facing, visualRoot.forward, Vector3.up);
            float snapped = Mathf.Round(yaw / 90f) * 90f;

            rig.localRotation = Quaternion.Euler(0f, snapped, 0f) * rig.localRotation;
        }

        /// <summary>绘制蒙皮骑士身体的材质。</summary>
        /// <remarks>
        /// 有许可资源时使用 Paladin 自带纹理甲胄，否则使用 CC0 人偶一直采用、
        /// 没有自身纹理可展示的纯钢色。
        /// </remarks>
        /// <returns>骑士护甲材质。</returns>
        public static Material KnightRigMaterial() =>
            Art.LicensedArtMaterials.Knight() ??
            MaterialLibrary.GetOrCreateSurface("KnightArmour", ArmourColor, metallic: 0.85f, smoothness: 0.45f);

        /// <summary>绘制蒙皮巨兽身体的材质。</summary>
        /// <remarks>有许可资源时使用 Warrok 自带纹理皮肤，否则使用人偶纯色皮肤。</remarks>
        /// <returns>巨兽皮肤材质。</returns>
        public static Material BruteRigMaterial() =>
            Art.LicensedArtMaterials.Brute() ??
            MaterialLibrary.GetOrCreateSurface("BruteHide", HideColor, metallic: 0.05f, smoothness: 0.18f);

        /// <summary>
        /// 将巨兽发光核心安装到蒙皮身体胸骨。
        /// </summary>
        /// <remarks>
        /// 核心既标示朝向，又是阶段光环和招架闪光的可见来源，也标出弱点。蒙皮身体没有自带核心，
        /// 因此移到胸部，让它随身体运动，而不是悬浮在旧基础躯干位置。
        /// </remarks>
        /// <param name="animator">蒙皮巨兽的 Animator。</param>
        /// <param name="size">核心直径，以世界单位计。</param>
        public static void AttachBruteCore(Animator animator, float size)
        {
            Transform chest = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Chest) : null;

            if (chest == null)
            {
                return;
            }

            Material core = MaterialLibrary.GetOrCreateSurface(
                "BruteEmberCore", Color.black, metallic: 0f, smoothness: 0.6f, emission: CoreGlow);

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Core";
            Object.DestroyImmediate(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(chest, false);

            // 局部缩放除以胸部世界缩放，使核心在任意骨架缩放下保持尺寸。
            float parentScale = Mathf.Max(0.0001f, chest.lossyScale.x);
            // 嵌入胸部而非贴在表面：纹理皮肤上完整发光球像粘上的平圆盘。下沉后仅露出闷燃光斑。
            sphere.transform.localScale = Vector3.one * (size * CoreScale / parentScale);
            sphere.transform.position = chest.position + chest.root.forward * (size * CoreDepth);
            sphere.GetComponent<MeshRenderer>().sharedMaterial = core;
        }

        /// <summary>钢甲，金属度足以让单灯捕捉边缘。</summary>
        private static readonly Color ArmourColor = new Color(0.46f, 0.50f, 0.58f);

        /// <summary>布料与披风刻意哑光，以显出不同材质。</summary>
        private static readonly Color ClothColor = new Color(0.26f, 0.32f, 0.47f);

        /// <summary>
        /// 面罩发光强度大于一，触发泛光。
        /// </summary>
        /// <remarks>
        /// 同时承担旧白方块的朝向标示。发光狭缝无需真正脸部就像面孔，并告诉玩家面朝哪里。
        /// </remarks>
        private static readonly Color VisorGlow = new Color(1.7f, 1.15f, 0.45f);

        /// <summary>巨兽主体使用暗色带血皮肤。</summary>
        private static readonly Color HideColor = new Color(0.56f, 0.24f, 0.26f);

        /// <summary>巨兽护板使用熏黑铁。</summary>
        private static readonly Color IronColor = new Color(0.33f, 0.30f, 0.33f);

        /// <summary>角使用旧骨色。</summary>
        private static readonly Color BoneColor = new Color(0.86f, 0.83f, 0.76f);

        /// <summary>
        /// 巨兽核心为深余烬红，略高于一，在泛光下闷燃。
        /// </summary>
        /// <remarks>此前亮得多，泛光将其冲淡为平桃色圆盘。</remarks>
        private static readonly Color CoreGlow = new Color(1.25f, 0.16f, 0.04f);

        /// <summary>核心直径相对要求尺寸的比例。</summary>
        private const float CoreScale = 0.7f;

        /// <summary>核心中心位于胸骨前方的距离，相对要求尺寸计。</summary>
        private const float CoreDepth = 0.55f;

        /// <summary>
        /// 为玩家构建直立着甲形体。
        /// </summary>
        /// <param name="visualRoot">部件所挂载的视觉根对象。</param>
        /// <param name="height">完整站立高度，与 CharacterController 匹配。</param>
        /// <param name="radius">身体半径，与 CharacterController 匹配。</param>
        public static void BuildKnight(Transform visualRoot, float height, float radius)
        {
            Material armour = MaterialLibrary.GetOrCreateSurface(
                "KnightArmour", ArmourColor, metallic: 0.85f, smoothness: 0.45f);

            Material cloth = MaterialLibrary.GetOrCreateSurface(
                "KnightCloth", ClothColor, metallic: 0f, smoothness: 0.12f);

            Material visor = MaterialLibrary.GetOrCreateSurface(
                "KnightVisor", Color.black, metallic: 0f, smoothness: 0.8f, emission: VisorGlow);

            float shoulder = radius * 0.78f;

            AddPart(visualRoot, "Torso", PrimitiveType.Cube, armour,
                new Vector3(0f, height * 0.63f, 0f),
                new Vector3(radius * 1.28f, height * 0.34f, radius * 0.82f));

            AddPart(visualRoot, "Hips", PrimitiveType.Cube, armour,
                new Vector3(0f, height * 0.44f, 0f),
                new Vector3(radius * 1.05f, height * 0.12f, radius * 0.74f));

            AddPart(visualRoot, "Head", PrimitiveType.Sphere, armour,
                new Vector3(0f, height * 0.89f, 0f),
                Vector3.one * (radius * 0.82f));

            // 盔饰使正上方视角也能认出头盔，而摄像机几乎一直从这个角度观察。
            AddPart(visualRoot, "Crest", PrimitiveType.Cube, cloth,
                new Vector3(0f, height * 0.98f, -radius * 0.06f),
                new Vector3(radius * 0.12f, height * 0.07f, radius * 0.62f));

            // 前向且发光，给朝向标记换一种外形。
            AddPart(visualRoot, "Visor", PrimitiveType.Cube, visor,
                new Vector3(0f, height * 0.89f, radius * 0.66f),
                new Vector3(radius * 0.46f, height * 0.028f, radius * 0.12f));

            for (int side = -1; side <= 1; side += 2)
            {
                string suffix = side < 0 ? "L" : "R";

                AddPart(visualRoot, $"Pauldron{suffix}", PrimitiveType.Sphere, armour,
                    new Vector3(shoulder * side, height * 0.75f, 0f),
                    Vector3.one * (radius * 0.62f));

                AddPart(visualRoot, $"Arm{suffix}", PrimitiveType.Cube, armour,
                    new Vector3(shoulder * side, height * 0.56f, 0f),
                    new Vector3(radius * 0.34f, height * 0.30f, radius * 0.34f));

                AddPart(visualRoot, $"Leg{suffix}", PrimitiveType.Cube, armour,
                    new Vector3(radius * 0.38f * side, height * 0.19f, 0f),
                    new Vector3(radius * 0.42f, height * 0.40f, radius * 0.46f));
            }

            // 从肩后悬挂并向下变宽，运动时主要靠此将轮廓区别于叠放盒子。
            AddPart(visualRoot, "Cloak", PrimitiveType.Cube, cloth,
                new Vector3(0f, height * 0.56f, -radius * 0.52f),
                new Vector3(radius * 1.5f, height * 0.52f, radius * 0.12f));
        }

        /// <summary>
        /// 为首领构建宽、低、带角的形体。
        /// </summary>
        /// <param name="visualRoot">部件所挂载的视觉根对象。</param>
        /// <param name="height">完整站立高度，与 CharacterController 匹配。</param>
        /// <param name="radius">身体半径，与 CharacterController 匹配。</param>
        public static void BuildBrute(Transform visualRoot, float height, float radius)
        {
            Material hide = MaterialLibrary.GetOrCreateSurface(
                "BruteHide", HideColor, metallic: 0.05f, smoothness: 0.18f);

            Material iron = MaterialLibrary.GetOrCreateSurface(
                "BruteIron", IronColor, metallic: 0.9f, smoothness: 0.35f);

            Material bone = MaterialLibrary.GetOrCreateSurface(
                "BruteBone", BoneColor, metallic: 0f, smoothness: 0.25f);

            Material core = MaterialLibrary.GetOrCreateSurface(
                "BruteEmberCore", Color.black, metallic: 0f, smoothness: 0.6f, emission: CoreGlow);

            float shoulder = radius * 0.82f;

            AddPart(visualRoot, "Torso", PrimitiveType.Cube, hide,
                new Vector3(0f, height * 0.62f, 0f),
                new Vector3(radius * 1.62f, height * 0.36f, radius * 1.05f));

            AddPart(visualRoot, "Hips", PrimitiveType.Cube, hide,
                new Vector3(0f, height * 0.38f, 0f),
                new Vector3(radius * 1.3f, height * 0.16f, radius * 0.95f));

            // 头嵌在肩间而非肩顶。低头位置最能让形体像巨兽而非人。
            AddPart(visualRoot, "Head", PrimitiveType.Sphere, hide,
                new Vector3(0f, height * 0.83f, radius * 0.18f),
                Vector3.one * (radius * 0.72f));

            // 唯一必须从各角度清晰可见的部件，作为首领轮廓标志。
            for (int side = -1; side <= 1; side += 2)
            {
                string suffix = side < 0 ? "L" : "R";

                var horn = AddPart(visualRoot, $"Horn{suffix}", PrimitiveType.Cube, bone,
                    new Vector3(radius * 0.34f * side, height * 0.94f, radius * 0.1f),
                    new Vector3(radius * 0.14f, height * 0.20f, radius * 0.14f));

                horn.transform.localRotation = Quaternion.Euler(-18f, 0f, 26f * side);

                AddPart(visualRoot, $"Shoulder{suffix}", PrimitiveType.Sphere, iron,
                    new Vector3(shoulder * side, height * 0.72f, 0f),
                    Vector3.one * (radius * 0.86f));

                AddPart(visualRoot, $"Arm{suffix}", PrimitiveType.Cube, hide,
                    new Vector3(shoulder * 1.05f * side, height * 0.45f, 0f),
                    new Vector3(radius * 0.44f, height * 0.38f, radius * 0.44f));

                AddPart(visualRoot, $"Leg{suffix}", PrimitiveType.Cube, hide,
                    new Vector3(radius * 0.46f * side, height * 0.15f, 0f),
                    new Vector3(radius * 0.56f, height * 0.32f, radius * 0.62f));
            }

            // 朝前，成为阶段光环的可见来源；也替代旧黄方块标示朝向。
            AddPart(visualRoot, "Core", PrimitiveType.Sphere, core,
                new Vector3(0f, height * 0.63f, radius * 0.56f),
                Vector3.one * (radius * 0.34f));
        }

        /// <summary>
        /// 创建一个身体部件。
        /// </summary>
        /// <remarks>
        /// 所有部件毫无例外移除碰撞体。碰撞属于根对象控制器，伤害属于旁边受击框，
        /// 此处遗漏碰撞体会干扰两者。球和立方体宽一个单位，胶囊和圆柱高两个单位；
        /// 上方缩放刻意依据这些尺寸，因此这里不用圆柱。
        /// </remarks>
        private static GameObject AddPart(
            Transform parent,
            string name,
            PrimitiveType primitive,
            Material material,
            Vector3 localPosition,
            Vector3 localScale)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;

            Object.DestroyImmediate(part.GetComponent<Collider>());

            if (material != null)
            {
                part.GetComponent<MeshRenderer>().sharedMaterial = material;
            }

            return part;
        }
    }
}
