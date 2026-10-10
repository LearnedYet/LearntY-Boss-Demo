using System.Collections.Generic;
using System.IO;
using System.Linq;
using AdaptiveBossArena.Combat.Feel;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AdaptiveBossArena.Editor.Art
{
    /// <summary>
    /// 构建蒙皮角色播放动画所需的 Animator Controller。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 每次都从代码重建，而不是在已存在时直接加载，因为它属于结构而非调参数据：其内容不应手动调整。
    /// 若旧控制器缺少代码要交叉淡入的状态，角色会悄然停留在最后的姿势。
    /// </para>
    /// <para>
    /// 其中没有任何状态转换。桥接组件依据其他系统读取的同一可观察状态决定角色应处于哪个动画状态，
    /// 并交叉淡入。再用 Animator转换重复表达这些决定，就会产生两个角色行为的事实来源。
    /// 攻击状态由以下参数驱动： <c>AttackTime</c> 而不是按时钟播放，
    /// 因此攻击自身的时间轴及其 hit-stop 决定姿势。
    /// </para>
    /// </remarks>
    public static class AnimatorControllerBuilder
    {
        /// <summary>生成的控制器所在文件夹。</summary>
        public const string ControllerFolder = EditorMenus.GeneratedAssetFolder + "/Animation";

        /// <summary>判断表格指定的模型是否存在于项目中。</summary>
        /// <param name="table">战斗角色的动画表。</param>
        /// <returns>模型可加载时返回 true。</returns>
        public static bool IsAvailable(CharacterClipTable table) => LoadRigModel(table) != null;

        /// <summary>加载用于构建战斗角色的模型。</summary>
        /// <param name="table">战斗角色的动画表。</param>
        /// <returns>导入的模型；美术资源缺失时返回 null。</returns>
        public static GameObject LoadRigModel(CharacterClipTable table) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(table.RigModel);

        /// <summary>根据表格重建一个战斗角色的控制器。</summary>
        /// <param name="table">战斗角色的动画表。</param>
        /// <returns>生成的控制器；项目中没有表格指定的动画片段时返回 null。</returns>
        public static AnimatorController Build(CharacterClipTable table)
        {
            Dictionary<string, AnimationClip> clips = LoadClips(table.ClipSources);

            if (clips.Count == 0)
            {
                Debug.LogWarning("[Adaptive Boss Arena] No clips found for " + table.Name + "; the generated bodies stay.");
                return null;
            }

            AssetAuthoring.EnsureFolderExists(ControllerFolder);

            string path = ControllerFolder + "/" + table.Name + ".controller";
            AnimatorController controller = LoadEmptiedOrCreate(path);
            controller.AddParameter(CharacterAnimatorParameters.Speed, AnimatorControllerParameterType.Float);
            controller.AddParameter(CharacterAnimatorParameters.MoveX, AnimatorControllerParameterType.Float);
            controller.AddParameter(CharacterAnimatorParameters.MoveZ, AnimatorControllerParameterType.Float);
            controller.AddParameter(CharacterAnimatorParameters.AttackTime, AnimatorControllerParameterType.Float);
            controller.AddParameter(CharacterAnimatorParameters.ReactionTime, AnimatorControllerParameterType.Float);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            AnimatorState locomotion = machine.AddState(CharacterAnimatorParameters.LocomotionState);
            locomotion.motion = table.Directional.Length > 0
                ? BuildDirectionalLocomotion(controller, clips, table)
                : BuildLocomotion(controller, clips, table.Locomotion);
            machine.defaultState = locomotion;

            foreach (KeyValuePair<string, string> entry in table.States)
            {
                // 受击动作放在下方独立的动画层。
                if (entry.Key == CharacterAnimatorParameters.HitState)
                {
                    continue;
                }

                AnimatorState state = AddClipState(machine, entry.Key, clips, entry.Value);

                if (state == null)
                {
                    continue;
                }

                if (entry.Key == CharacterAnimatorParameters.KnockedDownState)
                {
                    state.timeParameterActive = true;
                    state.timeParameter = CharacterAnimatorParameters.ReactionTime;
                }
                else if (CharacterClipTable.AttackStateNames.Contains(entry.Key))
                {
                    state.timeParameterActive = true;
                    state.timeParameter = CharacterAnimatorParameters.AttackTime;

                    // 同一动画片段连续攻击时使用的配对状态。
                    AnimatorState twin = machine.AddState(entry.Key + CharacterAnimatorParameters.AlternateSuffix);
                    twin.motion = state.motion;
                    twin.timeParameterActive = true;
                    twin.timeParameter = CharacterAnimatorParameters.AttackTime;
                }
            }

            AddHitLayer(controller, clips, table);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return controller;
        }

        /// <summary>
        /// 添加命中时播放的动画层：使用该角色的受击片段，并限定在上半身。
        /// </summary>
        /// <remarks>
        /// 静止时权重为零；桥接组件在受击动作期间提高权重。使用上半身遮罩进行 Override 混合，
        /// 躯干和手臂表现受击，腿部仍继续下层的步伐或站姿。
        /// </remarks>
        private static void AddHitLayer(AnimatorController controller, Dictionary<string, AnimationClip> clips, CharacterClipTable table)
        {
            if (!table.States.TryGetValue(CharacterAnimatorParameters.HitState, out string clipName) ||
                !clips.TryGetValue(clipName, out AnimationClip clip))
            {
                return;
            }

            var hits = new AnimatorStateMachine { name = CharacterAnimatorParameters.HitLayer, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(hits, controller);

            AnimatorState rest = hits.AddState(CharacterAnimatorParameters.HitRestState);
            hits.defaultState = rest;
            hits.AddState(CharacterAnimatorParameters.HitState).motion = clip;

            controller.AddLayer(new AnimatorControllerLayer
            {
                name = CharacterAnimatorParameters.HitLayer,
                stateMachine = hits,
                defaultWeight = 0f,
                blendingMode = AnimatorLayerBlendingMode.Override,
                avatarMask = UpperBodyMask()
            });
        }

        /// <summary>加载或创建覆盖脊柱、头部和手臂，但不覆盖腿部或根节点的遮罩。</summary>
        private static AvatarMask UpperBodyMask()
        {
            string path = ControllerFolder + "/UpperBody.mask";
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);

            if (mask == null)
            {
                mask = new AvatarMask { name = "UpperBody" };
                AssetDatabase.CreateAsset(mask, path);
            }

            for (int part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
            {
                var bodyPart = (AvatarMaskBodyPart)part;
                bool upper = bodyPart == AvatarMaskBodyPart.Body || bodyPart == AvatarMaskBodyPart.Head ||
                             bodyPart == AvatarMaskBodyPart.LeftArm || bodyPart == AvatarMaskBodyPart.RightArm ||
                             bodyPart == AvatarMaskBodyPart.LeftFingers || bodyPart == AvatarMaskBodyPart.RightFingers;
                mask.SetHumanoidBodyPartActive(bodyPart, upper);
            }

            EditorUtility.SetDirty(mask);
            return mask;
        }

        /// <summary>
        /// 清空已有控制器后加载它；不存在时创建一个。
        /// </summary>
        /// <remarks>
        /// 原地清空，而不是删除后重建：重建资产会生成新 GUID，
        /// 所有指向此控制器的预制体和配置都会失去引用；这曾导致首领无法攻击。其内容属于结构，
        /// 每次都完整重建，仅保留资产身份。
        /// </remarks>
        private static AnimatorController LoadEmptiedOrCreate(string path)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);

            if (controller == null)
            {
                return AnimatorController.CreateAnimatorControllerAtPath(path);
            }

            controller.parameters = new AnimatorControllerParameter[0];

            // 基础层之上的每一层也重建；保留它们会让每次运行都叠加新的受击层。
            while (controller.layers.Length > 1)
            {
                AnimatorStateMachine extra = controller.layers[controller.layers.Length - 1].stateMachine;
                controller.RemoveLayer(controller.layers.Length - 1);

                if (extra != null)
                {
                    Object.DestroyImmediate(extra, true);
                }
            }

            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            foreach (ChildAnimatorState child in machine.states)
            {
                machine.RemoveState(child.state);
            }

            foreach (Object subAsset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (subAsset is BlendTree)
                {
                    Object.DestroyImmediate(subAsset, true);
                }
            }

            return controller;
        }

        /// <summary>按名称收集所有来源中的片段；出现同名片段时保留较早来源。</summary>
        private static Dictionary<string, AnimationClip> LoadClips(IEnumerable<string> sources)
        {
            var clips = new Dictionary<string, AnimationClip>();

            foreach (string source in sources)
            {
                IEnumerable<string> files = AssetDatabase.IsValidFolder(source)
                    ? Directory.GetFiles(source, "*.fbx", SearchOption.AllDirectories).Select(f => f.Replace(Path.DirectorySeparatorChar, '/'))
                    : new[] { source };

                foreach (AnimationClip clip in files.SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<AnimationClip>())
                {
                    if (!clip.name.StartsWith("__preview__") && !clips.ContainsKey(clip.name))
                    {
                        clips.Add(clip.name, clip);
                    }
                }
            }

            return clips;
        }

        private static Motion BuildLocomotion(
            AnimatorController controller, Dictionary<string, AnimationClip> clips, (float Speed, string Clip)[] steps)
        {
            var tree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = CharacterAnimatorParameters.Speed,
                useAutomaticThresholds = false
            };

            AssetDatabase.AddObjectToAsset(tree, controller);

            foreach ((float speed, string clip) in steps)
            {
                AddChild(tree, clips, clip, speed);
            }

            return tree;
        }

        /// <summary>
        /// 二维混合待机、步行、跑步、后退和侧移，每个片段按实际移动方向定位。
        /// </summary>
        /// <remarks>
        /// 每个片段的位置以其测得的移动速度除以最快片段的速度确定：跑步位于朝向轴上的 1，
        /// 步行约在三分之一处，侧移则按实际步速置于对应侧。参数由相对朝向的移动驱动，
        /// 让侧向和后退移动匹配腿部动作。
        /// </remarks>
        private static Motion BuildDirectionalLocomotion(
            AnimatorController controller, Dictionary<string, AnimationClip> clips, CharacterClipTable table)
        {
            var tree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.FreeformDirectional2D,
                blendParameter = CharacterAnimatorParameters.MoveX,
                blendParameterY = CharacterAnimatorParameters.MoveZ,
                useAutomaticThresholds = false
            };

            AssetDatabase.AddObjectToAsset(tree, controller);

            if (table.Locomotion.Length > 0 && clips.TryGetValue(table.Locomotion[0].Clip, out AnimationClip idle))
            {
                tree.AddChild(idle, Vector2.zero);
            }

            var travels = table.Directional
                .Where(clips.ContainsKey)
                .Select(name => (Name: name, Travel: ClipTravelMeasure.TravelOf(table, name)))
                .Where(entry => entry.Travel.magnitude > MinimumTravel)
                .ToList();

            float fastest = travels.Count > 0 ? travels.Max(entry => entry.Travel.magnitude) : 1f;

            foreach ((string name, Vector2 travel) in travels)
            {
                tree.AddChild(clips[name], travel / fastest);
            }

            return tree;
        }

        /// <summary>低于此移动速度的片段按站立处理，单位为米每秒。</summary>
        private const float MinimumTravel = 0.2f;

        private static void AddChild(BlendTree tree, Dictionary<string, AnimationClip> clips, string clip, float threshold)
        {
            if (clips.TryGetValue(clip, out AnimationClip found))
            {
                tree.AddChild(found, threshold);
            }
        }

        private static AnimatorState AddClipState(
            AnimatorStateMachine machine, string stateName, Dictionary<string, AnimationClip> clips, string clip)
        {
            if (!clips.TryGetValue(clip, out AnimationClip found))
            {
                Debug.LogWarning("[Adaptive Boss Arena] Clip " + clip + " is missing; state " + stateName + " has no motion.");
                return null;
            }

            AnimatorState state = machine.AddState(stateName);
            state.motion = found;

            return state;
        }
    }
}
