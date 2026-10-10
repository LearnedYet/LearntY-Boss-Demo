using System.IO;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Editor.Art
{
    /// <summary>
    /// 为许可角色构建适用于 URP 的专用纹理材质。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mixamo 将各角色的漫反射、法线和高光贴图内嵌于 FBX；提取之前，Unity 不会把它们导入为可用资源。
    /// 导入材质还面向内置渲染器，因此生成器涂上纯色之前角色全白。这里将贴图一次性提取到角色旁边，
    /// 接入 URP Lit 的高光工作流，与 Mixamo 高光贴图的制作方式一致。
    /// </para>
    /// <para>
    /// 所有写出的内容都位于未纳入仓库的许可资源文件夹，因为纹理属于Mixamo。存在许可美术时，
    /// 设置流程会重新构建。
    /// </para>
    /// </remarks>
    public static class LicensedArtMaterials
    {
        /// <summary>写入提取纹理的文件夹。</summary>
        public const string TextureFolder = LicensedArtPostprocessor.MixamoFolder + "Characters/Textures";

        private const string MaterialFolder = LicensedArtPostprocessor.MixamoFolder + "Materials";

        /// <summary>骑士甲胄：较有光泽的钢材，带自身法线细节。</summary>
        /// <returns>材质；许可美术缺失时返回 null。</returns>
        public static Material Knight() =>
            Build("KnightBody", LicensedArtPostprocessor.KnightCharacter, "Paladin", smoothness: 0.55f);

        /// <summary>巨兽皮肤：粗糙表面，高光贴图区分角质和护甲。</summary>
        /// <returns>材质；许可美术缺失时返回 null。</returns>
        public static Material Brute() =>
            Build("BruteBody", LicensedArtPostprocessor.BossCharacter, "bear", smoothness: 0.35f);

        private static Material Build(string name, string character, string texturePrefix, float smoothness)
        {
            if (!LicensedArtPostprocessor.Available)
            {
                return null;
            }

            string diffusePath = $"{TextureFolder}/{texturePrefix}_diffuse.png";

            if (!File.Exists(diffusePath))
            {
                ExtractTextures(character);
            }

            var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(diffusePath);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{texturePrefix}_normal.png");
            var specular = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{texturePrefix}_specular.png");
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");

            if (diffuse == null || shader == null)
            {
                Debug.LogWarning("[Adaptive Boss Arena] Could not build " + name + "; its textures or the URP Lit shader are missing.");
                return null;
            }

            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;

            if (created)
            {
                material = new Material(shader) { name = name };
            }

            material.shader = shader;
            material.SetTexture("_BaseMap", diffuse);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", smoothness);

            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
            }

            if (specular != null)
            {
                material.SetFloat("_WorkflowMode", 0f);
                material.SetTexture("_SpecGlossMap", specular);
                material.SetColor("_SpecColor", Color.white);
                material.EnableKeyword("_SPECULAR_SETUP");
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
            }

            if (created)
            {
                AssetAuthoring.EnsureFolderExists(MaterialFolder);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        private static void ExtractTextures(string character)
        {
            AssetAuthoring.EnsureFolderExists(TextureFolder);

            var importer = (ModelImporter)AssetImporter.GetAtPath(character);

            if (importer == null || !importer.ExtractTextures(TextureFolder))
            {
                Debug.LogWarning("[Adaptive Boss Arena] Could not extract the textures embedded in " + character + ".");
            }

            AssetDatabase.Refresh();
        }
    }
}
