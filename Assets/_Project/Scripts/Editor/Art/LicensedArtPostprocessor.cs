using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Editor.Art
{
    /// <summary>
    /// 决定许可 Mixamo 角色和动画的导入方式。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 资源由项目配置者下载，不在仓库中，因此导入设置必须来自代码：
    /// 手写 .meta 不会自动存在于下一台机器。
    /// </para>
    /// <para>
    /// 角色以 Humanoid 和自身 Avatar 导入。每个动画文件从原制作角色复制 Avatar：
    /// 骑士剑盾动作集和共享片段使用 Paladin，首领巨剑动作集使用 Warrok，使片段驱动其原始骨架。
    /// 片段按文件名重命名，因为Mixamo 将所有 take 命名为“mixamo.com”；根运动烘焙进姿势，
    /// 角色移动由运动控制器负责，不由片段负责。
    /// </para>
    /// <para>
    /// Unity 导入 <c>Animations</c> 早于 <c>Characters</c>，因此首次导入时动画的源Avatar 尚不存在。
    /// 角色导入完成后，尚无 Avatar 的动画会重新导入，确保最终顺序正确。
    /// </para>
    /// </remarks>
    public sealed class LicensedArtPostprocessor : AssetPostprocessor
    {
        /// <summary>许可美术的根目录。</summary>
        public const string MixamoFolder = "Assets/_Project/Art/Licensed/Mixamo/";

        /// <summary>动画文件的文件夹。</summary>
        public const string AnimationFolder = MixamoFolder + "Animations";

        /// <summary>骑士角色文件。</summary>
        public const string KnightCharacter = MixamoFolder + "Characters/Paladin J Nordstrom.fbx";

        /// <summary>首领角色文件。</summary>
        public const string BossCharacter = MixamoFolder + "Characters/Warrok W Kurniawan.fbx";

        private static readonly string[] LoopingWords = { "idle", "walk", "run", "strafe" };

        /// <summary>此工作副本中是否存在许可角色。</summary>
        public static bool Available => File.Exists(KnightCharacter) && File.Exists(BossCharacter);

        /// <summary>导入规则变化时增加版本号，让已导入文件也应用新规则。</summary>
        /// <returns>这些导入规则的版本。</returns>
        public override uint GetVersion() => 4;

        /// <summary>
        /// 片段名称：从文件名中移除 Mixamo 预览角色的名称。
        /// </summary>
        /// <remarks>
        /// Mixamo 下载文件可能命名为“walk.fbx”或“Warrok W Kurniawan@Mutant Roaring.fbx”，
        /// 取决于获取方式；不值得在每张片段表中区分。前缀中的角色只是预览身体，
        /// 而非这里重定向后的目标骨架。
        /// </remarks>
        /// <param name="assetPath">动画文件的资产路径。</param>
        /// <returns>片段名称。</returns>
        public static string ClipNameFor(string assetPath)
        {
            string name = Path.GetFileNameWithoutExtension(assetPath);
            int character = name.LastIndexOf('@');

            return character >= 0 && character + 1 < name.Length ? name.Substring(character + 1) : name;
        }

        /// <summary>动画文件驱动其骨架的角色。</summary>
        /// <param name="animationPath">动画文件的资产路径。</param>
        /// <returns>角色文件的资产路径。</returns>
        public static string CharacterFor(string animationPath) =>
            animationPath.Replace('\\', '/').Contains("/Animations/Boss/") ? BossCharacter : KnightCharacter;

        /// <summary>根据 Mixamo 命名判断片段是否循环。</summary>
        /// <remarks>
        /// 待机、步行、跑步和侧移循环，包括持续持盾的“block idle”。转身、攻击、受击、倒地和死亡只播放一次。
        /// 按完整单词匹配，避免将“180 turn”误认为循环。
        /// </remarks>
        /// <param name="clipName">片段名称，即文件名。</param>
        /// <returns>循环片段返回 true。</returns>
        public static bool IsLooping(string clipName)
        {
            string[] words = clipName.ToLowerInvariant().Split(' ', '(', ')');

            // 按单词开头匹配，使“Mutant Walking”和“Mutant Run”同“great sword walk”一样循环，
            // 而“180 turn”和“Sprinting Forward Roll”仍只播放一次。
            return words.Any(word => LoopingWords.Any(word.StartsWith));
        }

        private bool IsLicensed =>
            assetPath.StartsWith(MixamoFolder) && assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);

        private bool IsCharacter => assetPath.Contains("/Characters/");

        private void OnPreprocessModel()
        {
            if (!IsLicensed)
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;

            importer.animationType = ModelImporterAnimationType.Human;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;

            if (IsCharacter)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                importer.importAnimation = false;
                return;
            }

            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;

            // 新工作副本首次导入时为 null；角色存在后，OnPostprocessAllAssets 会再次导入此文件。
            importer.sourceAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(CharacterFor(assetPath));
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(MixamoFolder))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.maxTextureSize = 2048;

            // 法线贴图若作为颜色导入，会让护甲上的每个凹陷看起来都朝错方向。
            if (assetPath.Contains("_normal"))
            {
                importer.textureType = TextureImporterType.NormalMap;
            }
        }

        private void OnPreprocessAnimation()
        {
            if (!IsLicensed || IsCharacter)
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            string name = ClipNameFor(assetPath);
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;

            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.name = name;
                clip.loopTime = IsLooping(name);

                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;

                // 地面位移提取为游戏会丢弃的根运动，而不烘焙进姿势。烘焙后，
                // 滑步或冲锋会让身体离开运动控制器和受击框所跟随的根节点数米：突进两次、
                // 脱离自身命中框，并在片段结束时跳回。提取后，每个片段都在根节点上播放，
                // 仅运动控制器移动角色。移动片段本就原地播放，因此不受影响。
                clip.lockRootPositionXZ = false;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }

            importer.clipAnimations = clips;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (!importedAssets.Any(path => path == KnightCharacter || path == BossCharacter) ||
                !Directory.Exists(AnimationFolder))
            {
                return;
            }

            foreach (string animation in Directory.GetFiles(AnimationFolder, "*.fbx", SearchOption.AllDirectories))
            {
                string path = animation.Replace('\\', '/');

                if (AssetImporter.GetAtPath(path) is ModelImporter importer && importer.sourceAvatar == null)
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }
        }
    }
}
