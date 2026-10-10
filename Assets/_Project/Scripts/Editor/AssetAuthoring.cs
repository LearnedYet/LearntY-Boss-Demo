using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 从编辑器代码创建并填充配置资产的辅助工具。
    /// </summary>
    /// <remarks>
    /// 配置字段私有并通过只读属性暴露，这对运行时代码是正确的，却让生成器无法写入。
    /// <see cref="SerializedObject"/> 是允许的写入途径；在此封装可让生成器保持可读，
    /// 而不把意图淹没在序列化样板代码中。
    /// </remarks>
    internal static class AssetAuthoring
    {
        /// <summary>加载指定路径的资产；不存在时创建。</summary>
        /// <typeparam name="TAsset">要创建的 ScriptableObject 类型。</typeparam>
        /// <param name="assetPath">包含扩展名的项目相对路径。</param>
        /// <param name="created">新建了资产时接收 true。</param>
        /// <returns>已存在或新建的资产。</returns>
        public static TAsset CreateOrLoad<TAsset>(string assetPath, out bool created)
            where TAsset : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<TAsset>(assetPath);

            // 直接加载可能漏掉磁盘上已存在但尚未导入的资产，这会在整条设置链一次批处理运行时出现。
            // 没有第二次尝试，生成器会误判资产不存在并覆盖它，导致每次运行都丢失手工调参。
            if (existing == null)
            {
                existing = GeneratedAssets.ForceImportAndLoad<TAsset>(assetPath);
            }

            if (existing != null)
            {
                created = false;
                return existing;
            }

            EnsureFolderExists(Path.GetDirectoryName(assetPath));

            var asset = ScriptableObject.CreateInstance<TAsset>();
            AssetDatabase.CreateAsset(asset, assetPath);
            created = true;
            return asset;
        }

        /// <summary>创建项目相对路径中所有尚不存在的文件夹。</summary>
        /// <param name="folderPath">项目相对文件夹路径，可使用任意斜杠方向。</param>
        public static void EnsureFolderExists(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                return;
            }

            folderPath = folderPath.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] segments = folderPath.Split('/');
            string accumulated = segments[0];

            for (int i = 1; i < segments.Length; i++)
            {
                string next = $"{accumulated}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(accumulated, segments[i]);
                }

                accumulated = next;
            }
        }

        /// <summary>通过作用域辅助对象打开资产以进行序列化编辑。</summary>
        /// <param name="asset">要编辑的资产。</param>
        /// <returns>释放时应用更改的写入器。</returns>
        public static AssetWriter Edit(UnityEngine.Object asset) => new AssetWriter(asset);

        /// <summary>
        /// 释放时应用修改并将目标标记为脏的作用域写入器。
        /// </summary>
        /// <remarks>
        /// 释放语义确保生成器不会忘记调用<see cref="SerializedObject.ApplyModifiedProperties"/>；
        /// 遗漏该调用会悄然生成全是默认值的资产，没有错误信息解释原因。
        /// </remarks>
        public sealed class AssetWriter : IDisposable
        {
            private readonly SerializedObject _serialized;
            private readonly UnityEngine.Object _asset;

            internal AssetWriter(UnityEngine.Object asset)
            {
                _asset = asset;
                _serialized = new SerializedObject(asset);
            }

            /// <summary>设置浮点数字段。</summary>
            /// <param name="fieldName">包含下划线前缀的后备字段名。</param>
            /// <param name="value">要设置的值。</param>
            /// <returns>返回当前写入器以支持链式调用。</returns>
            public AssetWriter Float(string fieldName, float value)
            {
                Require(fieldName).floatValue = value;
                return this;
            }

            /// <summary>设置整数或枚举字段。</summary>
            /// <param name="fieldName">后备字段名。</param>
            /// <param name="value">要设置的值。</param>
            /// <returns>返回当前写入器以支持链式调用。</returns>
            public AssetWriter Int(string fieldName, int value)
            {
                SerializedProperty property = Require(fieldName);

                // 使用枚举值而非其在成员列表中的位置。按 0、1、2 顺序编号时两者一致，
                // 此处枚举起初均如此，直到引入标志枚举：把 17 等组合值作为位置写入会超出范围，
                // Unity 会悄然将其保存为零。
                if (property.propertyType == SerializedPropertyType.Enum)
                {
                    property.enumValueFlag = value;
                }
                else
                {
                    property.intValue = value;
                }

                return this;
            }

            /// <summary>设置枚举字段。</summary>
            /// <param name="fieldName">后备字段名。</param>
            /// <param name="value">要设置的枚举值。</param>
            /// <returns>返回当前写入器以支持链式调用。</returns>
            public AssetWriter Enum(string fieldName, System.Enum value) =>
                Int(fieldName, Convert.ToInt32(value));

            /// <summary>设置字符串字段。</summary>
            /// <param name="fieldName">后备字段名。</param>
            /// <param name="value">要设置的值。</param>
            /// <returns>返回当前写入器以支持链式调用。</returns>
            public AssetWriter String(string fieldName, string value)
            {
                Require(fieldName).stringValue = value;
                return this;
            }

            /// <summary>设置布尔字段。</summary>
            /// <param name="fieldName">后备字段名。</param>
            /// <param name="value">要设置的值。</param>
            /// <returns>返回当前写入器以支持链式调用。</returns>
            public AssetWriter Bool(string fieldName, bool value)
            {
                Require(fieldName).boolValue = value;
                return this;
            }

            /// <summary>设置三维向量字段。</summary>
            /// <param name="fieldName">后备字段名。</param>
            /// <param name="value">要设置的值。</param>
            /// <returns>返回当前写入器以支持链式调用。</returns>
            public AssetWriter Vector3(string fieldName, UnityEngine.Vector3 value)
            {
                Require(fieldName).vector3Value = value;
                return this;
            }

            /// <summary>设置颜色字段。</summary>
            /// <param name="fieldName">后备字段名。</param>
            /// <param name="value">要设置的值。</param>
            /// <returns>返回当前写入器以支持链式调用。</returns>
            public AssetWriter Color(string fieldName, UnityEngine.Color value)
            {
                Require(fieldName).colorValue = value;
                return this;
            }

            /// <summary>设置对象引用字段。</summary>
            /// <param name="fieldName">后备字段名。</param>
            /// <param name="value">要设置的引用。</param>
            /// <returns>返回当前写入器以支持链式调用。</returns>
            public AssetWriter Reference(string fieldName, UnityEngine.Object value)
            {
                Require(fieldName).objectReferenceValue = value;
                return this;
            }

            /// <summary>替换对象引用数组字段。</summary>
            /// <param name="fieldName">后备字段名。</param>
            /// <param name="values">按顺序排列的待设置引用。</param>
            /// <returns>返回当前写入器以支持链式调用。</returns>
            public AssetWriter ReferenceArray(string fieldName, params UnityEngine.Object[] values)
            {
                SerializedProperty property = Require(fieldName);
                property.arraySize = values.Length;

                for (int i = 0; i < values.Length; i++)
                {
                    property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
                }

                return this;
            }

            /// <summary>调整数组字段长度并返回属性，以便逐元素编辑。</summary>
            /// <param name="fieldName">后备字段名。</param>
            /// <param name="size">元素数量。</param>
            /// <returns>调整长度后的数组属性。</returns>
            public SerializedProperty Array(string fieldName, int size)
            {
                SerializedProperty property = Require(fieldName);
                property.arraySize = size;
                return property;
            }

            /// <summary>查找属性；字段被重命名时明确报错。</summary>
            private SerializedProperty Require(string fieldName)
            {
                SerializedProperty property = _serialized.FindProperty(fieldName);

                if (property == null)
                {
                    throw new InvalidOperationException(
                        $"Field '{fieldName}' was not found on '{_asset.name}' ({_asset.GetType().Name}). " +
                        "The generator and the configuration type have drifted apart.");
                }

                return property;
            }

            /// <summary>应用待提交修改，并将资产标记为脏。</summary>
            public void Dispose()
            {
                _serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(_asset);
            }
        }
    }
}
