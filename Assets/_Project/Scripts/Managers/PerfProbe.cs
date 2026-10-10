using System;
using System.Globalization;
using System.IO;
using AdaptiveBossArena.Utilities.Statistics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 根据项目性能预算测量每帧成本。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 物理与图形升级增加蒙皮角色、布娃娃、碎片、雾与第二套渲染级别，每项都可能悄然耗尽帧预算；
    /// “这台机器上感觉流畅”不是测量。这里在相同场景下重复报告预算采用的同类指标，
    /// 使性能退步的阶段能被看见。
    /// </para>
    /// <para>
    /// 两个入口：F3 在游玩时显示实时面板；采集可以无人值守，
    /// 通过桌面命令行标记 <c>-perfCapture &lt;seconds&gt;</c>，
    /// 或 WebGL 页面的查询字符串<c>?perf=&lt;seconds&gt;</c> 启动。采集等待战斗开始并稳定，
    /// 按请求时长采样，再向日志写入一行 <c>[PERF]</c>；浏览器中日志进入开发者控制台，
    /// 这是远程读取发布构建的唯一位置。
    /// </para>
    /// <para>
    /// 绘制调用与三角形数量来自分析器，仅在开发构建与编辑器中报告。正式发布对这些项目显示 <c>n/a</c>，
    /// 而非会被误解为满足预算的零。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PerfProbe : MonoBehaviour
    {
        /// <summary>采集帧数上限，相当于 240 Hz 下十分钟。</summary>
        private const int SampleCapacity = 144000;

        /// <summary>战斗开始后丢弃的帧数，避免加载卡顿干扰采集。</summary>
        private const float SettleSeconds = 2f;

        private const float PanelWidth = 260f;
        private const float PanelMargin = 12f;

        [SerializeField]
        [Tooltip("Key that shows and hides the live panel.")]
        private Key _toggleKey = Key.F3;

        private readonly FrameTimeSampler _sampler = new FrameTimeSampler(SampleCapacity);

        private ProfilerRecorder _drawCalls;
        private ProfilerRecorder _triangles;
        private long _drawCallTotal;
        private long _triangleTotal;
        private int _countedFrames;

        private bool _isVisible;
        private GUIStyle _style;

        private float _captureSeconds;
        private string _logPath;
        private bool _quitWhenDone;
        private string _shotPath;
        private bool _shotTaken;
        private string _shotFolder;
        private float _shotEvery;

        /// <summary>序列是否也按独立未缩放时钟截取冻结的入场画面。</summary>
        private bool _shotsIncludeIntro;

        private float _introElapsed;
        private float _nextIntroShot;
        private float _nextSequenceShot;
        private int _sequenceIndex;

        /// <summary>战斗采样开始后截图的秒数，此时双方应已靠近。</summary>
        private const float ShotAfterSeconds = 4f;
        private float _settleRemaining = SettleSeconds;
        private float _captured;
        private bool _captureFinished;

        /// <summary>本轮是否请求无人值守采集，且采集仍在运行。</summary>
        public bool IsCapturing => _captureSeconds > 0f && !_captureFinished;

        private void Awake()
        {
            ReadCaptureRequest();
        }

        private void OnEnable()
        {
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        }

        private void OnDisable()
        {
            _drawCalls.Dispose();
            _triangles.Dispose();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard[_toggleKey].wasPressedThisFrame)
            {
                _isVisible = !_isVisible;
            }

            // 战斗进行时始终测量，不论是否请求采集，使实时面板显示战斗而非冻结入场的指标。
            // 可以读取时间缩放，但只有 TimeService 可以写入。
            if (Time.timeScale <= 0f)
            {
                PhotographIntro();
                return;
            }

            if (_settleRemaining > 0f)
            {
                _settleRemaining -= Time.unscaledDeltaTime;
                return;
            }

            Sample();

            if (!IsCapturing)
            {
                return;
            }

            _captured += Time.unscaledDeltaTime;

            if (!_shotTaken && !string.IsNullOrEmpty(_shotPath) && _captured >= ShotAfterSeconds)
            {
                _shotTaken = true;
                StartCoroutine(SaveFrame(_shotPath));
            }

            if (!string.IsNullOrEmpty(_shotFolder) && _captured >= _nextSequenceShot)
            {
                _nextSequenceShot += _shotEvery;
                StartCoroutine(SaveFrame(System.IO.Path.Combine(_shotFolder, $"frame_{_sequenceIndex++:D4}.raw")));
            }

            if (_captured >= _captureSeconds)
            {
                FinishCapture();
            }
        }

        /// <summary>请求序列包含入场时，截取冻结的入场画面。</summary>
        private void PhotographIntro()
        {
            if (!_shotsIncludeIntro || !IsCapturing || string.IsNullOrEmpty(_shotFolder))
            {
                return;
            }

            _introElapsed += Time.unscaledDeltaTime;

            if (_introElapsed >= _nextIntroShot)
            {
                _nextIntroShot += _shotEvery;
                StartCoroutine(SaveFrame(System.IO.Path.Combine(_shotFolder, $"frame_{_sequenceIndex++:D4}.raw")));
            }
        }

        /// <summary>
        /// 将完成帧的像素写入文件：宽高，随后是原始 RGBA 行。
        /// </summary>
        /// <remarks>
        /// 采用原始数据而非 PNG，因为图像编码和截屏助手均位于项目未包含的引擎模块；
        /// 为调试辅助而在每个发布构建中加入模块并不划算。读取帧缓冲只需要核心模块。
        /// </remarks>
        private static System.Collections.IEnumerator SaveFrame(string path)
        {
            yield return new WaitForEndOfFrame();

            int width = Screen.width;
            int height = Screen.height;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();

            Color32[] pixels = texture.GetPixels32();
            var bytes = new byte[8 + pixels.Length * 4];

            System.BitConverter.GetBytes(width).CopyTo(bytes, 0);
            System.BitConverter.GetBytes(height).CopyTo(bytes, 4);

            for (int i = 0; i < pixels.Length; i++)
            {
                bytes[8 + i * 4] = pixels[i].r;
                bytes[9 + i * 4] = pixels[i].g;
                bytes[10 + i * 4] = pixels[i].b;
                bytes[11 + i * 4] = pixels[i].a;
            }

            Destroy(texture);

            try
            {
                File.WriteAllBytes(path, bytes);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Adaptive Boss Arena] Could not write frame capture: " + exception.Message);
            }
        }

        private void Sample()
        {
            _sampler.Add(Time.unscaledDeltaTime);

            if (_drawCalls.Valid && _drawCalls.LastValue > 0)
            {
                _drawCallTotal += _drawCalls.LastValue;
                _triangleTotal += _triangles.Valid ? _triangles.LastValue : 0;
                _countedFrames++;
            }
        }

        private void FinishCapture()
        {
            _captureFinished = true;

            string line = FormatReport();
            Debug.Log(line);

            if (!string.IsNullOrEmpty(_logPath))
            {
                try
                {
                    File.AppendAllText(_logPath, line + Environment.NewLine);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"[Adaptive Boss Arena] Could not write perf log '{_logPath}': {exception.Message}");
                }
            }

            if (_quitWhenDone)
            {
                StartCoroutine(QuitAfterFramesAreWritten());
            }
        }

        /// <summary>全部已开始的帧采集完成写入后退出。</summary>
        /// <remarks>
        /// 帧采集等待帧结束再读取屏幕。若序列结束的同一帧立即退出，仍在等待的采集会随播放器销毁，
        /// 导致退出时崩溃。
        /// </remarks>
        private System.Collections.IEnumerator QuitAfterFramesAreWritten()
        {
            yield return new WaitForEndOfFrame();
            yield return null;

            Application.Quit();
        }

        /// <summary>将当前测量格式化为核对预算的单行报告。</summary>
        /// <returns>报告行。</returns>
        public string FormatReport()
        {
            CultureInfo invariant = CultureInfo.InvariantCulture;

            string drawCalls = _countedFrames > 0
                ? (_drawCallTotal / _countedFrames).ToString(invariant)
                : "n/a";

            string triangles = _countedFrames > 0
                ? (_triangleTotal / _countedFrames).ToString(invariant)
                : "n/a";

            // 报告画质级别，使采集能证明实际测量的渲染档位。否则回落到网页档位的桌面构建，
            // 会报告没有实际绘制高档图形时看似漂亮的数值。
            return string.Format(
                invariant,
                "[PERF] platform={0} quality={8} p50={1:F2}ms p95={2:F2}ms max={3:F2}ms frames={4} drawcalls={5} tris={6} seconds={7:F1}",
                Application.platform,
                _sampler.PercentileMilliseconds(0.5f),
                _sampler.PercentileMilliseconds(0.95f),
                _sampler.MaxMilliseconds,
                _sampler.Count,
                drawCalls,
                triangles,
                _captured,
                QualitySettings.names[QualitySettings.GetQualityLevel()].Replace(' ', '_'));
        }

        private void ReadCaptureRequest()
        {
            PerfCaptureRequest request =
                PerfCaptureRequest.Read(Environment.GetCommandLineArgs(), Application.absoluteURL);

            _captureSeconds = request.Seconds;
            _logPath = request.LogPath;
            _quitWhenDone = request.QuitWhenDone;
            _shotPath = request.ShotPath;
            _shotFolder = request.ShotFolder;
            _shotEvery = Mathf.Max(0.02f, request.ShotEverySeconds);
            _shotsIncludeIntro = request.ShotsIncludeIntro;

            if (!string.IsNullOrEmpty(_shotFolder))
            {
                Directory.CreateDirectory(_shotFolder);
            }
        }

        private void OnGUI()
        {
            if (!_isVisible)
            {
                return;
            }

            _style ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 13,
                wordWrap = true
            };

            var panel = new Rect(Screen.width - PanelWidth - PanelMargin, PanelMargin, PanelWidth, 170f);
            GUI.Box(panel, FormatReport().Replace(" ", "\n"), _style);
        }
    }
}
