using System;
using System.IO;
using AdaptiveBossArena.Core.Services;
using UnityEngine;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 将记录作为 JSON 文件保存到平台存档目录。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 写入采用原子替换：先写临时文件，再替换原文件。直接覆盖在进程中途退出时会留下截断文件，
    /// 而自动存档期间按 Alt-F4 的玩家，恰恰会因此无法再次启动游戏。
    /// </para>
    /// <para>
    /// 读取绝不抛出异常。损坏或不可读文件返回 false，调用者退回默认值；丢失设置只是小麻烦，
    /// 无法启动则不是。
    /// </para>
    /// <para>
    /// 刻意不使用 <see cref="PlayerPrefs"/>。它在 Windows 上写入注册表，不透明、难以备份，
    /// 诊断玩家问题时也几乎无法检查。
    /// </para>
    /// </remarks>
    public sealed class JsonSaveService : ISaveService
    {
        private const string FileExtension = ".json";
        private const string TempExtension = ".tmp";
        private const string BackupExtension = ".bak";

        private readonly string _rootFolder;

        /// <summary>创建向指定目录下写入的存档服务。</summary>
        /// <param name="rootFolder">
        /// 目标目录。默认采用平台持久化数据路径，这是唯一保证跨平台可写的位置。
        /// </param>
        public JsonSaveService(string rootFolder = null)
        {
            _rootFolder = string.IsNullOrEmpty(rootFolder)
                ? Application.persistentDataPath
                : rootFolder;

            Directory.CreateDirectory(_rootFolder);
        }

        /// <summary>记录写入的目录。</summary>
        public string RootFolder => _rootFolder;

        /// <inheritdoc />
        public bool Exists(string key) => File.Exists(PathFor(key));

        /// <inheritdoc />
        public void Save<TData>(string key, TData data) where TData : class
        {
            if (data == null)
            {
                return;
            }

            string finalPath = PathFor(key);
            string tempPath = finalPath + TempExtension;
            string json = JsonUtility.ToJson(data, prettyPrint: true);

            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                // 浏览器构建的虚拟文件系统不支持 File.Replace，
                // 且运行时本来就会将写入持久化到 IndexedDB，因此普通写入既正确，
                // 也能避免每次修改设置时原子路径产生的控制台错误。
                File.WriteAllText(finalPath, json);
#else
                File.WriteAllText(tempPath, json);

                // 平台支持时 File.Replace 为原子操作，并把上一版本保留为备份，
                // 使损坏写入仍有可恢复版本。
                if (File.Exists(finalPath))
                {
                    File.Replace(tempPath, finalPath, finalPath + BackupExtension);
                }
                else
                {
                    File.Move(tempPath, finalPath);
                }
#endif
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Adaptive Boss Arena] Failed to save '{key}': {exception.Message}");
                TryDelete(tempPath);
            }
        }

        /// <inheritdoc />
        public bool TryLoad<TData>(string key, out TData data) where TData : class
        {
            if (TryReadFrom(PathFor(key), out data))
            {
                return true;
            }

            // 主文件缺失或不可读。File.Replace 正是为此保留了上一版本，因此放弃前值得检查备份。
            if (TryReadFrom(PathFor(key) + BackupExtension, out data))
            {
                Debug.LogWarning(
                    $"[Adaptive Boss Arena] Recovered '{key}' from its backup; the primary file was unreadable.");
                return true;
            }

            data = null;
            return false;
        }

        /// <inheritdoc />
        public void Delete(string key)
        {
            TryDelete(PathFor(key));
            TryDelete(PathFor(key) + BackupExtension);
        }

        /// <summary>读取并解析一个文件，任何失败均视为不可用。</summary>
        private static bool TryReadFrom<TData>(string path, out TData data) where TData : class
        {
            data = null;

            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return false;
                }

                data = JsonUtility.FromJson<TData>(json);
                return data != null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[Adaptive Boss Arena] Could not read '{path}': {exception.Message}. Defaults will be used.");
                return false;
            }
        }

        /// <summary>删除文件，并忽略失败。</summary>
        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Adaptive Boss Arena] Could not delete '{path}': {exception.Message}");
            }
        }

        /// <summary>解析记录键对应的文件路径。</summary>
        private string PathFor(string key) => Path.Combine(_rootFolder, key + FileExtension);
    }
}
