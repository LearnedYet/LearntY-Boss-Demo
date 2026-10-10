using UnityEditor;

namespace AdaptiveBossArena.Editor.Art
{
    /// <summary>
    /// 决定第三方石材和砖材纹理的导入方式。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 纹理导入类型保存在 .meta 中，每套三张贴图中有两张的默认设置不正确。
    /// 法线作为颜色导入会让表面仿佛弯向错误方向；遮罩作为 sRGB 导入会将遮蔽和平滑度压暗，
    /// 让石材莫名湿黑。这两种问题都不会让测试失败。在此定义导入设置，
    /// 可确保 . meta 丢失或重建后仍然正确。
    /// </para>
    /// <para>
    /// 所有平台都限制为 1K。竞技场最远也只从数米外观察，而 WebGL下载体积才是关键预算。
    /// </para>
    /// </remarks>
    public sealed class SurfaceTexturePostprocessor : AssetPostprocessor
    {
        /// <summary>此后处理器负责的纹理文件夹。</summary>
        public const string SurfaceFolder = "Assets/_Project/Art/ThirdParty/PolyHaven/";

        private const int MaximumSize = 1024;

        /// <summary>导入规则变化时增加版本号，让已导入纹理也应用新规则。</summary>
        /// <returns>这些导入规则的版本。</returns>
        public override uint GetVersion() => 1;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(SurfaceFolder))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;

            importer.maxTextureSize = MaximumSize;
            importer.mipmapEnabled = true;
            importer.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            importer.anisoLevel = 4;

            if (assetPath.Contains("_nor_gl"))
            {
                // Poly Haven 的 _gl 贴图使用 OpenGL 约定，与 Unity 预期一致。
                importer.textureType = TextureImporterType.NormalMap;
                return;
            }

            importer.textureType = TextureImporterType.Default;

            // 颜色使用 sRGB；遮罩承载金属度、遮蔽和平滑度数据，必须保持线性。
            importer.sRGBTexture = !assetPath.Contains("_mask");
            importer.alphaIsTransparency = false;
        }
    }
}
