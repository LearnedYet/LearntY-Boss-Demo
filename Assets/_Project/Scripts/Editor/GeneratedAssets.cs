using System.IO;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 为消费生成资产的生成器解析其他生成器产出的资产。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 先按路径加载，再回退到搜索。顺序源于实际故障：整个设置链一次批处理执行时，
    /// 刚生成的资产已在磁盘却尚未进入资产数据库搜索索引，
    /// 因此<see cref="AssetDatabase.FindAssets(string)"/> 加名称过滤会悄然返回空。
    /// 预制体所有事件通道引用随后均为 null，唯一症状是无人查看的日志警告。
    /// </para>
    /// <para>
    /// 生成器输出路径已知，直接加载更快且不受此问题影响。搜索仅作为资产被手动移动后的回退。
    /// </para>
    /// </remarks>
    internal static class GeneratedAssets
    {
        /// <summary>生成事件通道资产的文件夹。</summary>
        public const string EventFolder = EditorMenus.GeneratedAssetFolder + "/Events";

        /// <summary>生成配置资产的文件夹。</summary>
        public const string ConfigFolder = EditorMenus.GeneratedAssetFolder + "/Config";

        /// <summary>按名称加载生成的事件通道。</summary>
        /// <typeparam name="TChannel">要加载的通道类型。</typeparam>
        /// <param name="assetName">不含扩展名的文件名。</param>
        /// <returns>通道；失败时返回 null 并记录警告。</returns>
        public static TChannel EventChannel<TChannel>(string assetName) where TChannel : Object =>
            LoadByPathOrSearch<TChannel>($"{EventFolder}/{assetName}.asset", assetName);

        /// <summary>
        /// 加载某配置类型的单个资产。
        /// </summary>
        /// <typeparam name="TAsset">要加载的配置类型。</typeparam>
        /// <param name="preferredName">
        /// 生成器使用的文件名，优先尝试，使项目存在多个同类型资产时仍可确定性解析。
        /// </param>
        /// <returns>资产；失败时返回 null 并记录警告。</returns>
        public static TAsset Config<TAsset>(string preferredName) where TAsset : Object =>
            LoadByPathOrSearch<TAsset>($"{ConfigFolder}/{preferredName}.asset", preferredName);

        /// <summary>从准确路径加载，必要时强制导入，然后回退搜索。</summary>
        private static TAsset LoadByPathOrSearch<TAsset>(string expectedPath, string assetName)
            where TAsset : Object
        {
            var direct = AssetDatabase.LoadAssetAtPath<TAsset>(expectedPath);

            if (direct != null)
            {
                return direct;
            }

            direct = ForceImportAndLoad<TAsset>(expectedPath);

            if (direct != null)
            {
                return direct;
            }

            foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(TAsset).Name}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var candidate = AssetDatabase.LoadAssetAtPath<TAsset>(path);

                if (candidate != null && candidate.name == assetName)
                {
                    return candidate;
                }
            }

            Debug.LogWarning(
                $"[Adaptive Boss Arena] Could not resolve {typeof(TAsset).Name} '{assetName}'. " +
                $"Expected it at '{expectedPath}'. Run 'Generate Default Assets' and regenerate.");

            return null;
        }

        /// <summary>
        /// 为已存在但资产数据库尚未知晓的文件同步导入并加载。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 当完整设置链运行于一次 <c>-executeMethod</c> 调用时，Unity 延迟导入直到批处理完成。
        /// 刚写入的资产因此已在磁盘却不在数据库中，连直接按路径加载也返回 null。
        /// </para>
        /// <para>
        /// 后果不只是查找失败。生成器通过以下方法调用此处：<c>CreateOrLoad</c>，
        /// 未找到资产会把全新资产写到已有文件上，悄然丢弃所有手工调参数值。
        /// </para>
        /// </remarks>
        /// <typeparam name="TAsset">要加载的资产类型。</typeparam>
        /// <param name="assetPath">项目相对路径。</param>
        /// <returns>资产；文件确实不存在时返回 null。</returns>
        public static TAsset ForceImportAndLoad<TAsset>(string assetPath) where TAsset : Object
        {
            // Unity 工作目录为项目根目录，因此检查文件系统时项目相对路径可直接解析。
            if (!File.Exists(assetPath))
            {
                return null;
            }

            AssetDatabase.ImportAsset(
                assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            return AssetDatabase.LoadAssetAtPath<TAsset>(assetPath);
        }
    }
}
