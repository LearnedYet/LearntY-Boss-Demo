using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 用于独立版和浏览器演示的无界面玩家构建。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 两个目标使用同一场景列表，即 Build Settings中启用的场景；主菜单场景生成器已固定为标题场景在前、
    /// 竞技场在后，保证两种产物包含相同场景。通过命令行调用
    /// <c>-executeMethod AdaptiveBossArena.Editor.BuildScript.BuildWindows</c> （或
    /// <c>BuildWebGL</c>).
    /// </para>
    /// <para>
    /// WebGL 设置专门面向 GitHub Pages：Pages 提供静态文件时没有<c>Content-Encoding</c> 响应头，
    /// 因此 gzip 压缩构建必须启用 Unity的 JavaScript 解压回退才能加载。开启后构建体积小，
    /// 也能从普通静态主机加载。
    /// </para>
    /// </remarks>
    public static class BuildScript
    {
        private const string WindowsOutput = "Build/Windows/AdaptiveBossArena.exe";
        private const string WindowsFolder = "Build/Windows";
        private const string WebGLOutput = "Build/WebGL";
        private const string ReleaseFolder = "Build/Release";

        /// <summary>构建 64 位 Windows 独立玩家版本。</summary>
        [MenuItem(EditorMenus.Setup + "Build Windows (x64)")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, WindowsOutput);

        /// <summary>
        /// 构建 Windows 玩家版本，并打包成可附加到发行版的 zip。
        /// </summary>
        /// <remarks>
        /// 按构建来源提交命名，使下载的 zip 可以追溯到准确代码。这里刻意不发布 zip：发行是对外操作，
        /// 需要单独执行。
        /// </remarks>
        [MenuItem(EditorMenus.Setup + "Build Windows Release Zip")]
        public static void BuildWindowsRelease()
        {
            if (!Build(BuildTarget.StandaloneWindows64, WindowsOutput))
            {
                return;
            }

            Directory.CreateDirectory(ReleaseFolder);

            string zipPath = Path.Combine(ReleaseFolder, $"AdaptiveBossArena-{ShortCommit()}-win64.zip");

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            ZipFile.CreateFromDirectory(
                WindowsFolder, zipPath, System.IO.Compression.CompressionLevel.Optimal, false);

            Debug.Log(
                $"[Build] Windows release packed -> {zipPath} ({new FileInfo(zipPath).Length / (1024 * 1024)} MB).");
        }

        /// <summary>
        /// 说明目标默认画质等级为何会使用错误层级；没有问题时返回 null。
        /// </summary>
        /// <remarks>
        /// 若构建悄然从另一层级启动，会把导致浏览器启动崩溃的环境遮蔽发布到网页，
        /// 或让桌面版缺少应有图形效果。运行产物前这些问题都不可见，因此改为拒绝开始构建。
        /// </remarks>
        /// <param name="target">构建目标。</param>
        /// <returns>问题说明；层级正确时返回 null。</returns>
        public static string TierProblemFor(BuildTarget target)
        {
            bool isWeb = target == BuildTarget.WebGL;
            string platform = isWeb
                ? RenderPipelineConfigurator.WebGLPlatform
                : RenderPipelineConfigurator.StandalonePlatform;

            string[] expectedPipelines = isWeb
                ? RenderPipelineConfigurator.WebPipelinePaths
                : RenderPipelineConfigurator.DesktopPipelinePaths;

            SerializedObject quality = RenderPipelineConfigurator.LoadQualitySettings();
            SerializedProperty levels = quality?.FindProperty("m_QualitySettings");
            SerializedProperty defaults = quality?.FindProperty("m_PerPlatformDefaultQuality");

            if (levels == null || defaults == null)
            {
                return "quality settings could not be read";
            }

            int defaultLevel = -1;

            for (int i = 0; i < defaults.arraySize; i++)
            {
                SerializedProperty pair = defaults.GetArrayElementAtIndex(i);

                if (pair.FindPropertyRelative("first").stringValue == platform)
                {
                    defaultLevel = pair.FindPropertyRelative("second").intValue;
                }
            }

            if (defaultLevel < 0 || defaultLevel >= levels.arraySize)
            {
                return $"{platform} has no valid default quality level";
            }

            SerializedProperty level = levels.GetArrayElementAtIndex(defaultLevel);
            Object pipeline = level.FindPropertyRelative("customRenderPipeline").objectReferenceValue;
            string pipelinePath = pipeline != null ? AssetDatabase.GetAssetPath(pipeline) : "(none)";

            if (System.Array.IndexOf(expectedPipelines, pipelinePath) < 0)
            {
                string levelName = level.FindPropertyRelative("name").stringValue;

                return $"{platform} starts on quality level {levelName}, which renders with {pipelinePath}, " +
                       "a pipeline from the other platform's tier";
            }

            return ShippedPipelineProblem(platform, expectedPipelines);
        }

        /// <summary>
        /// 检查引擎实际将随某平台发布的渲染管线资产。
        /// </summary>
        /// <remarks>
        /// 仅检查默认等级，曾让两次发布错误层级的构建通过，因为配置正确但引擎仍打包了另一平台资产。
        /// 这里询问引擎实际构建时所使用的数据，提前拦截错误，而不是等到浏览器运行才发现。
        /// </remarks>
        private static string ShippedPipelineProblem(string platform, string[] expectedPipelines)
        {
            var shipped = new System.Collections.Generic.List<UnityEngine.Rendering.RenderPipelineAsset>();
            QualitySettings.GetAllRenderPipelineAssetsForPlatform(platform, ref shipped);

            foreach (UnityEngine.Rendering.RenderPipelineAsset asset in shipped)
            {
                string path = asset != null ? AssetDatabase.GetAssetPath(asset) : "(none)";

                if (System.Array.IndexOf(expectedPipelines, path) < 0)
                {
                    return $"a {platform} build would ship {path}, which belongs to the other render tier";
                }
            }

            return shipped.Count == 0 ? $"a {platform} build would ship no render pipeline at all" : null;
        }

        /// <summary>构建可从 GitHub Pages 运行的 WebGL 玩家版本。</summary>
        [MenuItem(EditorMenus.Setup + "Build WebGL")]
        public static void BuildWebGL()
        {
            // Gzip 配合 JS 解压回退，既减小下载量，也能从没有 Content-Encoding 响应头的静态主机加载。
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;

            // 演示应在战斗中标签页失焦后继续模拟，而不是冻结。
            PlayerSettings.runInBackground = true;

            // 使用原生 WebAssembly 异常而非 JavaScript 异常。采用 JavaScript 异常时，
            // try 块中的每次调用都经 JS 跳板往返，深调用链每层额外消耗一个 JS 栈帧。
            // Firefox 的栈在主流浏览器中最小，玩家曾在 Chrome 和 Windows 正常运行的链路遇到“too much
            // recursion”。当前浏览器都支持2023 功能集，Firefox 从版本 100 起已支持。
            PlayerSettings.WebGL.wasm2023 = true;

            // 浏览器崩溃只显示编号 wasm 函数。每次构建都在旁边写出命名符号映射，
            // 便于对照玩家实际版本解读截图。映射仅本地保留，不部署。
            PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.External;

            Build(BuildTarget.WebGL, WebGLOutput);
        }

        /// <summary>运行玩家构建；失败时明确使进程失败。</summary>
        /// <returns>构建成功时返回 true。</returns>
        private static bool Build(BuildTarget target, string outputPath)
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Fail("No enabled scenes in Build Settings — run 'Run Full Setup' first.");
                return false;
            }

            // 先于其他操作切换目标，不交给 BuildPlayer 延迟处理。
            // 渲染管线在 BuildPlayer 切换之前就按编辑器当前平台决定发布资产，
            // 因此从 Windows 编辑器开始的 WebGL构建曾包含桌面层级及环境遮蔽，
            // 随后从 WebGL 开始的 Windows 构建又包含网页层级。每次都带上了前一个平台的图形配置。
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);

            if (EditorUserBuildSettings.activeBuildTarget != target &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
            {
                Fail($"Could not switch the active platform to {target} before building.");
                return false;
            }

            string tierProblem = TierProblemFor(target);

            if (tierProblem != null)
            {
                Fail($"Refusing to build {target}: {tierProblem}. Run 'Run Full Setup' first.");
                return false;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Fail($"{target} build {summary.result} with {summary.totalErrors} error(s).");
                return false;
            }

            Debug.Log(
                $"[Build] {target} succeeded → '{outputPath}' " +
                $"({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:F0}s).");

            return true;
        }

        /// <summary>当前提交的短哈希；无法使用 git 时为“local”。</summary>
        private static string ShortCommit()
        {
            try
            {
                using Process git = Process.Start(new ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (git == null)
                {
                    return "local";
                }

                string hash = git.StandardOutput.ReadToEnd().Trim();
                git.WaitForExit();

                return string.IsNullOrEmpty(hash) ? "local" : hash;
            }
            catch (Exception)
            {
                return "local";
            }
        }

        /// <summary>记录失败，批处理模式下以非零码退出，使 CI 能够发现。</summary>
        private static void Fail(string message)
        {
            Debug.LogError($"[Build] {message}");

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
