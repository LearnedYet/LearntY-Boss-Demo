using System.Collections.Generic;
using AdaptiveBossArena.Core.Constants;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 应用项目层、标签、碰撞矩阵和物理时间设置。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这些设置保存为接近二进制结构的 YAML，位于 <c>ProjectSettings</c> ，难以手写且差异难审查。
    /// 代码表达使意图清楚，新克隆可复现设置，也让碰撞矩阵从以下定义推导： <see cref="LayerNames"/> ，
    /// 而非在 Inspector 网格中重复维护。
    /// </para>
    /// <para>
    /// 可安全重复运行。检测并复用已有层与标签，不重复添加。
    /// </para>
    /// </remarks>
    public static class ProjectConfigurator
    {
        /// <summary>Unity 为项目开放的首个层索引。</summary>
        private const int FirstUserLayerIndex = 8;

        /// <summary>Unity 提供的总层槽数。</summary>
        private const int TotalLayerCount = 32;

        private const string TagManagerAssetPath = "ProjectSettings/TagManager.asset";
        private const string TimeManagerAssetPath = "ProjectSettings/TimeManager.asset";

        /// <summary>创建层和标签，配置碰撞，设置物理频率。</summary>
        [MenuItem(EditorMenus.Setup + "1. Configure Project", priority = EditorMenus.SetupPriorityConfigureProject)]
        public static void ConfigureProject()
        {
            ConfigureLayers();
            ConfigureTags();
            ConfigureTimeSettings();
            ConfigureInputHandling();
            ConfigureColorSpace();

            // 运行时会缓存层索引，而它们刚发生变化。
            Layers.InvalidateCache();

            ConfigureCollisionMatrix();

            AssetDatabase.SaveAssets();
            Debug.Log(
                "[Adaptive Boss Arena] Project configured: layers, tags, collision matrix and physics rate applied.");
        }

        /// <summary>
        /// 将项目设为线性颜色空间。
        /// </summary>
        /// <remarks>
        /// <para>
        /// Unity URP 模板项目从开始就是线性。本项目自行生成配置却未设置，始终在 Gamma渲染；
        /// 这是游戏显得粗糙的最大原因，但没有报错、仍可绘制，因此难以识别。
        /// </para>
        /// <para>
        /// Gamma 中光线累积与衰减曲线错误：明暗分界过急、过饱和，物理金属和平滑度发白，
        /// ACES 接收不适合的值，强度超过一的自发光在非预期阈值泛光。玩家只会觉得像原型，却不知道原因。
        /// </para>
        /// <para>
        /// 切换也会明显变暗，因为颜色不再被重复提亮。生成器配色常量随后按线性重新平衡，
        /// 所有针对旧空间目测调整的内容都需要同样处理。
        /// </para>
        /// </remarks>
        private static void ConfigureColorSpace()
        {
            if (PlayerSettings.colorSpace == ColorSpace.Linear)
            {
                return;
            }

            PlayerSettings.colorSpace = ColorSpace.Linear;

            Debug.Log(
                "[Adaptive Boss Arena] Switched to linear colour space. Unity will reimport shaders " +
                "and textures, which takes a moment.");
        }

        /// <summary>确保以下定义中的每个层都 <see cref="LayerNames"/> 存在。</summary>
        private static void ConfigureLayers()
        {
            SerializedObject tagManager = LoadProjectSettingsAsset(TagManagerAssetPath);
            SerializedProperty layers = tagManager.FindProperty("layers");

            foreach (string layerName in LayerNames.All)
            {
                if (LayerExists(layers, layerName))
                {
                    continue;
                }

                if (!TryAssignToFreeSlot(layers, layerName))
                {
                    Debug.LogError(
                        $"[Adaptive Boss Arena] No free layer slot available for '{layerName}'. " +
                        "Free a user layer in Project Settings and run this command again.");
                }
            }

            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>检查任一槽中是否已有该层名。</summary>
        private static bool LayerExists(SerializedProperty layers, string layerName)
        {
            for (int i = 0; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>将层名写入首个空用户槽。</summary>
        private static bool TryAssignToFreeSlot(SerializedProperty layers, string layerName)
        {
            for (int i = FirstUserLayerIndex; i < TotalLayerCount && i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(slot.stringValue))
                {
                    slot.stringValue = layerName;
                    return true;
                }
            }

            return false;
        }

        /// <summary>确保以下定义中的每个标签都存在： <see cref="TagNames"/> 存在。</summary>
        private static void ConfigureTags()
        {
            SerializedObject tagManager = LoadProjectSettingsAsset(TagManagerAssetPath);
            SerializedProperty tags = tagManager.FindProperty("tags");

            foreach (string tagName in TagNames.All)
            {
                if (TagExists(tags, tagName))
                {
                    continue;
                }

                tags.InsertArrayElementAtIndex(tags.arraySize);
                tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tagName;
            }

            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>检查标签是否已定义。</summary>
        private static bool TagExists(SerializedProperty tags, string tagName)
        {
            for (int i = 0; i < tags.arraySize; i++)
            {
                if (tags.GetArrayElementAtIndex(i).stringValue == tagName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 限制命中检测层仅与预期对象交互。
        /// </summary>
        /// <remarks>
        /// 先禁止全部，再允许命名层对，而非仅禁用已知错误组合。默认拒绝可防止新增层悄然与命中框碰撞，
        /// 从而出现穿墙攻击。
        /// </remarks>
        private static void ConfigureCollisionMatrix()
        {
            int[] volumeLayers =
            {
                Layers.PlayerHitbox,
                Layers.PlayerHurtbox,
                Layers.BossHitbox,
                Layers.BossHurtbox,
                Layers.Projectile
            };

            foreach (int volumeLayer in volumeLayers)
            {
                for (int other = 0; other < TotalLayerCount; other++)
                {
                    Physics.IgnoreLayerCollision(volumeLayer, other, true);
                }
            }

            // 命中检测唯一允许观察的交互。
            AllowCollision(Layers.PlayerHitbox, Layers.BossHurtbox);
            AllowCollision(Layers.BossHitbox, Layers.PlayerHurtbox);
            AllowCollision(Layers.Projectile, Layers.PlayerHurtbox);
            AllowCollision(Layers.Projectile, Layers.Arena);

            // 身体与世界碰撞，刻意不互相碰撞：两个 CharacterController 相遇会完全停住，无法互相逼退。
            // BodySeparation 按质量推开双方，负责身体接触。
            AllowCollision(Layers.Player, Layers.Arena);
            AllowCollision(Layers.Boss, Layers.Arena);
            Physics.IgnoreLayerCollision(Layers.Player, Layers.Boss, true);

            // 尸体和碎石是遵守物理的景物。与攻击体积一样默认拒绝交互：
            // 若尸体或柱碎片能接触角色或受击框，就会开始决定战斗。
            foreach (int loose in new[] { Layers.Ragdoll, Layers.Debris })
            {
                for (int other = 0; other < TotalLayerCount; other++)
                {
                    Physics.IgnoreLayerCollision(loose, other, true);
                }

                AllowCollision(loose, 0);
                AllowCollision(loose, Layers.Arena);
            }

            AllowCollision(Layers.Debris, Layers.Debris);
            AllowCollision(Layers.Debris, Layers.Ragdoll);
        }

        /// <summary>重新启用指定层对之间的碰撞。</summary>
        private static void AllowCollision(int layerA, int layerB) =>
            Physics.IgnoreLayerCollision(layerA, layerB, false);

        /// <summary>
        /// 将项目切换为仅使用 Input System 包。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 添加包时 Unity 默认支持新旧两个输入后端，产生持续弃用警告，
        /// 更会允许旧版 <c>UnityEngine.Input</c> 调用编译运行；切到新后端时立即抛异常。
        /// 现在容许它们工作，就是积累未来故障。
        /// </para>
        /// <para>
        /// 修改需重启编辑器才生效，Unity 会提示处理。
        /// </para>
        /// </remarks>
        private static void ConfigureInputHandling()
        {
            const int inputSystemPackageOnly = 2;

            SerializedObject playerSettings = LoadProjectSettingsAsset("ProjectSettings/ProjectSettings.asset");
            SerializedProperty handler = playerSettings.FindProperty("activeInputHandler");

            if (handler == null)
            {
                Debug.LogWarning(
                    "[Adaptive Boss Arena] Could not find the active input handler setting. Set " +
                    "Project Settings > Player > Active Input Handling to 'Input System Package' manually.");
                return;
            }

            if (handler.intValue == inputSystemPackageOnly)
            {
                return;
            }

            handler.intValue = inputSystemPackageOnly;
            playerSettings.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log(
                "[Adaptive Boss Arena] Active Input Handling set to Input System only. " +
                "Unity will ask to restart for this to take effect.");
        }

        /// <summary>将物理步长固定为项目帧数据所依据的频率。</summary>
        private static void ConfigureTimeSettings()
        {
            SerializedObject timeManager = LoadProjectSettingsAsset(TimeManagerAssetPath);

            SetFloatIfPresent(timeManager, "Fixed Timestep", GameplayConstants.FixedTimeStep);

            // 限制追赶步长，防止帧尖峰触发一串物理步骤，让攻击一次处理数帧移动。
            SetFloatIfPresent(timeManager, "Maximum Allowed Timestep", 0.1f);

            timeManager.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>设置浮点项目属性；Unity 重命名属性时警告。</summary>
        private static void SetFloatIfPresent(SerializedObject target, string propertyName, float value)
        {
            SerializedProperty property = target.FindProperty(propertyName);

            if (property == null)
            {
                Debug.LogWarning(
                    $"[Adaptive Boss Arena] Time setting '{propertyName}' was not found and has been " +
                    "skipped. Set it manually in Project Settings > Time.");
                return;
            }

            property.floatValue = value;
        }

        /// <summary>打开项目设置资产进行序列化编辑。</summary>
        private static SerializedObject LoadProjectSettingsAsset(string assetPath)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            return new SerializedObject(assets[0]);
        }

        /// <summary>报告项目当前缺少的层和标签。</summary>
        /// <returns>各缺失项的人类可读说明。</returns>
        public static IReadOnlyList<string> FindMissingConfiguration()
        {
            var missing = new List<string>();

            foreach (string layerName in LayerNames.All)
            {
                if (LayerMask.NameToLayer(layerName) < 0)
                {
                    missing.Add($"Layer '{layerName}'");
                }
            }

            return missing;
        }
    }
}
