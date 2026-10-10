using UnityEditor;

namespace AdaptiveBossArena.Editor.Art
{
    /// <summary>
    /// 决定第三方角色模型及其动画的导入方式。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 导入设置通常只保存在模型 .meta 文件中，由 Inspe ctor 手动设置。这违反项目一切从代码生成的原则，
    /// 也有特定风险：删除或重新生成 .meta 后，骨架会悄然变为 Generic，片段停止循环，根运动开始拖动角色，
    /// 而测试未必能发现。在此设置导入规则，可确保每次重新导入结果一致。
    /// </para>
    /// <para>
    /// 仅作用于第三方美术文件夹，不影响项目其他模型。
    /// </para>
    /// </remarks>
    public sealed class CharacterModelPostprocessor : AssetPostprocessor
    {
        /// <summary>此后处理器负责的模型文件夹。</summary>
        public const string ThirdPartyFolder = "Assets/_Project/Art/ThirdParty/";

        /// <summary>
        /// 导入规则变化时增加版本号。
        /// </summary>
        /// <remarks>
        /// Unity 仅在其追踪的内容变化时重新导入模型，后处理器代码不在其中。不增加版本号，
        /// 修改规则只对新导入模型生效，已有模型仍保留旧设置。
        /// </remarks>
        /// <returns>这些导入规则的版本。</returns>
        public override uint GetVersion() => 2;

        private bool IsThirdPartyModel => assetPath.StartsWith(ThirdPartyFolder) && assetPath.EndsWith(".fbx");

        private void OnPreprocessModel()
        {
            if (!IsThirdPartyModel)
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;

            // 使用 Humanoid，让一个库的片段能够驱动另一个库的身体，
            // 也让游戏按骨骼角色而非作者命名查找手骨和胸骨。
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

            // 内嵌材质只是占位资源；表面材质来自了解渲染管线和美术方向的 MaterialLibrary。
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;

            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsThirdPartyModel)
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;

            foreach (ModelImporterClipAnimation clip in clips)
            {
                // 导出器为每个动画 take 加上骨架前缀，两个库使用不同的骨架名。去掉前缀后，
                // 同一个片段名在各处含义一致，下游不必知道它来自哪个库。
                clip.name = CleanClipName(clip.name);
                clip.loopTime = IsLoopingClip(clip.name);

                // 移动属于运动控制器，绝不属于片段：命中范围和首领距离逻辑都按代码移动角色的位置设定。
                // 每个片段的根运动都烘焙进姿势，确保片段无法自行移动角色。
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }

            importer.clipAnimations = clips;
        }

        /// <summary>移除 take 名称中的导出骨架前缀。</summary>
        /// <param name="rawName">导出的名称，例如 <c>Rig|Walk_Loop</c>.</param>
        /// <returns>片段自身名称，例如 <c>Walk_Loop</c>.</returns>
        public static string CleanClipName(string rawName)
        {
            int separator = rawName.LastIndexOf('|');

            return separator >= 0 ? rawName.Substring(separator + 1) : rawName;
        }

        /// <summary>根据库自身命名判断片段是否应循环。</summary>
        /// <param name="clipName">片段名称。</param>
        /// <returns>待机和移动片段返回 true。</returns>
        public static bool IsLoopingClip(string clipName) =>
            clipName.EndsWith("_Loop") || clipName == "Sword_Idle";
    }
}
