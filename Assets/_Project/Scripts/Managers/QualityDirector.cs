using System.Collections.Generic;
using AdaptiveBossArena.Core.Services;
using UnityEngine;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 首次运行时选择图形画质，应用到渲染器，并在浏览器战斗开始后根据帧率检查选择是否合适。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 画质级别按平台命名，例如 Browser Medium、Desktop High；每个构建只保留自身的三级，
    /// 因此按名称末尾而非固定索引查找画质。
    /// </para>
    /// <para>
    /// Windows 可以读取显卡信息，因此独显从高画质开始，集显从低画质开始。浏览器隐藏显卡信息，
    /// 所以先采用中画质再测量；战斗最初十秒只决定一次升降，而且只在玩家尚未手动选择画质时执行。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class QualityDirector : MonoBehaviour
    {
        /// <summary>浏览器测量后作出决定所需的时间。</summary>
        private const float SampleSeconds = 10f;

        /// <summary>允许升档的第 95 百分位帧时间上限，确保六十帧仍有余量。</summary>
        public const float StepUpMilliseconds = 12f;

        /// <summary>触发降档的第 95 百分位帧时间阈值，代表可见卡顿。</summary>
        public const float StepDownMilliseconds = 20f;

        /// <summary>Windows 显卡被视为独显所需的显存量。</summary>
        private const int DedicatedMemoryMegabytes = 3000;

        private readonly List<float> _frames = new List<float>(1024);
        private float _sampled;
        private bool _decided;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ChooseOnStartup()
        {
            GraphicsTier.Changed -= ApplyLevel;
            GraphicsTier.Changed += ApplyLevel;

            GraphicsTier.SetAutomatic(StartingQuality(
                Application.platform == RuntimePlatform.WebGLPlayer,
                SystemInfo.graphicsMemorySize,
                SystemInfo.graphicsDeviceName));

            ApplyLevel(GraphicsTier.Current);
        }

        /// <summary>尚未测量或手动选择前机器采用的画质。</summary>
        /// <param name="browser">是否为浏览器构建。</param>
        /// <param name="graphicsMemoryMegabytes">显卡显存。</param>
        /// <param name="deviceName">显卡名称。</param>
        /// <returns>初始画质。</returns>
        public static GraphicsQuality StartingQuality(bool browser, int graphicsMemoryMegabytes, string deviceName)
        {
            if (browser)
            {
                return GraphicsQuality.Medium;
            }

            string device = (deviceName ?? string.Empty).ToLowerInvariant();
            bool integrated = device.Contains("intel") || device.Contains("uhd") || device.Contains("iris") ||
                              device.Contains("radeon(tm) graphics") || device.Contains("vega");

            return !integrated && graphicsMemoryMegabytes >= DedicatedMemoryMegabytes
                ? GraphicsQuality.High
                : GraphicsQuality.Low;
        }

        /// <summary>
        /// 测量后应切换的画质；无需切换时返回当前画质。
        /// </summary>
        /// <param name="current">测量时采用的画质。</param>
        /// <param name="p95Milliseconds">样本中的第 95 百分位帧时间。</param>
        /// <returns>应采用的画质。</returns>
        public static GraphicsQuality AdjustedQuality(GraphicsQuality current, float p95Milliseconds)
        {
            if (p95Milliseconds > StepDownMilliseconds && current > GraphicsQuality.Low)
            {
                return current - 1;
            }

            if (p95Milliseconds < StepUpMilliseconds && current < GraphicsQuality.High)
            {
                return current + 1;
            }

            return current;
        }

        /// <summary>
        /// 指定画质在构建所带画质级别中的索引。
        /// </summary>
        /// <param name="levelNames">构建的画质级别名称，从低到高。</param>
        /// <param name="quality">希望采用的画质。</param>
        /// <returns>级别索引；没有匹配名称时按位置选择最近项。</returns>
        public static int LevelIndex(IReadOnlyList<string> levelNames, GraphicsQuality quality)
        {
            string suffix = " " + quality;

            for (int i = 0; i < levelNames.Count; i++)
            {
                if (levelNames[i] != null && levelNames[i].EndsWith(suffix))
                {
                    return i;
                }
            }

            return Mathf.Clamp((int)quality, 0, Mathf.Max(0, levelNames.Count - 1));
        }

        private static void ApplyLevel(GraphicsQuality quality)
        {
            int level = LevelIndex(QualitySettings.names, quality);

            if (QualitySettings.GetQualityLevel() != level)
            {
                QualitySettings.SetQualityLevel(level, applyExpensiveChanges: true);
            }
        }

        private void Update()
        {
            // 仅在浏览器执行一次，仅在玩家尚未选择画质且正在战斗时执行；
            // 冻结的入场和菜单无法反映战斗的实际成本。
            if (_decided || !GraphicsTier.IsAutomatic || Application.platform != RuntimePlatform.WebGLPlayer ||
                Time.timeScale <= 0f)
            {
                return;
            }

            float frame = Time.unscaledDeltaTime;
            _frames.Add(frame * 1000f);
            _sampled += frame;

            if (_sampled < SampleSeconds)
            {
                return;
            }

            _decided = true;
            _frames.Sort();
            float p95 = _frames[Mathf.Min(_frames.Count - 1, (int)(_frames.Count * 0.95f))];

            GraphicsQuality adjusted = AdjustedQuality(GraphicsTier.Current, p95);
            Debug.Log($"[Adaptive Boss Arena] Graphics: p95 {p95:F1} ms on {GraphicsTier.Current}; using {adjusted}.");
            GraphicsTier.SetAutomatic(adjusted);
        }
    }
}
