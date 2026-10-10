using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 构建武器挂点一直等待的握持武器模型。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 从美术接入点建立起，武器挂点、装备调用和模型字段就已存在，但没有模型可放。
    /// 因此切武器只改标签和挥击颜色，手中看不到变化。这里从代码填充空位。
    /// </para>
    /// <para>
    /// 三种形状，便于近乎俯视摄像机在瞬间区分：巨剑长宽、长剑中等均匀、能量刃短细且发光。
    /// 各自按武器的以下字段着色：<c>SignatureColor</c> ，该字段此前虽已设置和暴露，却无人读取。
    /// </para>
    /// <para>
    /// 挂点契约为握柄处 pivot、刀刃沿 +Z，因为<c>WeaponSocket.Equip</c> 将实例局部位置和旋转归零。
    /// 所有部件无碰撞体：武器模型仅供观看，命中由攻击重叠系统判定，不由模型决定。
    /// </para>
    /// </remarks>
    internal static class WeaponModelBuilder
    {
        private const string PrefabFolder = "Assets/_Project/Prefabs/Weapons";

        /// <summary>三种共用握柄和护手，表现为同一套装备。</summary>
        private static readonly Color HiltColor = new Color(0.14f, 0.13f, 0.15f);

        /// <summary>偏暗、略暖的钢材。</summary>
        private static readonly Color SteelColor = new Color(0.56f, 0.55f, 0.53f);

        /// <summary>
        /// 构建或加载一件武器模型。
        /// </summary>
        /// <param name="assetName">武器资产名，用作预制体文件名。</param>
        /// <param name="signature">用于刀刃着色的武器标志色。</param>
        /// <param name="length">刀刃长度，单位为米。</param>
        /// <param name="width">刀刃宽度，单位为米。</param>
        /// <param name="glows">刀刃是否自发光，用于能量武器。</param>
        /// <returns>预制体；无法创建时返回 null。</returns>
        public static GameObject GetOrCreate(
            string assetName, Color signature, float length, float width, bool glows)
        {
            // 每次重建：模型是结构而非调参。同路径覆盖保存保留预制体身份，因此武器引用仍有效。
            string path = $"{PrefabFolder}/{assetName}Model.prefab";

            AssetAuthoring.EnsureFolderExists(PrefabFolder);

            var root = new GameObject($"{assetName}Model");

            try
            {
                Build(root.transform, assetName, signature, length, width, glows);

                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                // 在场景构建后保存，必须移除临时原对象，与角色预制体构建器方式相同。
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>围绕手部 pivot 组合握柄、护手和刀刃。</summary>
        private static void Build(
            Transform root, string assetName, Color signature, float length, float width, bool glows)
        {
            // 网格使用不透明颜色：标志色为挥击覆盖图带 Alpha，半透明刀刃会像错误而非风格。
            var bladeColor = new Color(signature.r, signature.g, signature.b);

            Material hilt = MaterialLibrary.GetOrCreateSurface(
                $"{assetName}Hilt", HiltColor, metallic: 0.5f, smoothness: 0.3f);

            Material blade = glows
                ? MaterialLibrary.GetOrCreateSurface(
                    $"{assetName}Blade", bladeColor * 0.4f, metallic: 0f, smoothness: 0.8f,
                    emission: bladeColor * 2.2f)
                : MaterialLibrary.GetOrCreateSurface(
                    // 钢材仅轻微受标志色影响，完全着色的刀刃像涂漆玩具。
                    $"{assetName}Steel", Color.Lerp(SteelColor, bladeColor, 0.12f), metallic: 0.95f, smoothness: 0.72f);

            // 真实形状而非拉伸盒体：收尖的菱形截面刀刃、护手、握柄和剑首。握柄从 pivot 向后延伸，
            // 让手能握住。
            AddPart(root, "Grip", hilt, Art.ArmsBuilder.Blade(0.2f, 0.045f, 0.045f, 0.05f),
                new Vector3(0f, 0f, -0.2f), Quaternion.identity);

            AddPart(root, "Pommel", hilt, Art.ArmsBuilder.Blade(0.07f, 0.07f, 0.07f, 0.5f),
                new Vector3(0f, 0f, -0.26f), Quaternion.identity);

            // 沿 +Z 构建并横过刀刃旋转，使其像护手一样横跨手部。
            AddPart(root, "Guard", hilt, Art.ArmsBuilder.Blade(width * 3.2f, 0.05f, 0.04f, 0.15f),
                new Vector3(-width * 1.6f, 0f, 0.02f), Quaternion.Euler(0f, 90f, 0f));

            AddPart(root, "Blade", blade, Art.ArmsBuilder.Blade(length, width, width * 0.2f, 0.14f),
                new Vector3(0f, 0f, 0.04f), Quaternion.identity);
        }

        /// <summary>创建一个无碰撞体武器部件。</summary>
        /// <remarks>
        /// 无碰撞体：此处碰撞体会在玩家自身层、CharacterController 内干扰控制。伤害来自攻击重叠，
        /// 不来自模型。
        /// </remarks>
        private static void AddPart(
            Transform parent, string name, Material material, Mesh mesh, Vector3 position, Quaternion rotation)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;

            if (material != null)
            {
                part.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
        }
    }
}
