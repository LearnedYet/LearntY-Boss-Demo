using System.Globalization;

namespace AdaptiveBossArena.Utilities.Statistics
{
    /// <summary>
    /// 本次是否请求性能采样，以及采样方式。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 放在Utilities的纯逻辑中，供两个互不可见的程序集一致使用。测量探针在Game，
    /// 跳过标题进入战斗的逻辑在UI，依赖方向不能反转。首次基线测量曾停在标题，只测到菜单且未输出结果。
    /// </para>
    /// <para>
    /// 桌面程序接收<c>-perfCapture &lt;seconds&gt; [-perfLog &lt;path&gt; ] [-perfQuit]</c>，
    /// WebGL没有命令行，改用<c>?perf=&lt;seconds&gt;</c>。
    /// </para>
    /// </remarks>
    public readonly struct PerfCaptureRequest
    {
        private const string CaptureFlag = "-perfCapture";
        private const string LogFlag = "-perfLog";
        private const string QuitFlag = "-perfQuit";
        private const string ShotFlag = "-perfShot";
        private const string ShotsFlag = "-perfShots";
        private const string ShotEveryFlag = "-perfShotEvery";

        /// <summary>在冻结的开场期间开始采集帧序列。</summary>
        private const string ShotsIntroFlag = "-perfShotsIntro";

        /// <summary>默认帧序列间隔为每秒十帧，足以观察挥击轨迹。</summary>
        public const float DefaultShotEverySeconds = 0.1f;
        private const string CaptureQueryKey = "perf";

        /// <summary>采样时长，单位为秒；未请求时为零。</summary>
        public float Seconds { get; init; }

        /// <summary>报告行追加到的文件，没有则为null。</summary>
        public string LogPath { get; init; }

        /// <summary>保存战斗截图的文件，没有则为null。</summary>
        /// <remarks>
        /// 性能采样测量帧，截图显示帧。即使测试全通过，角色仍可能比例错误或反拿武器，
        /// 运行构建的图像才能发现。
        /// </remarks>
        public string ShotPath { get; init; }

        /// <summary>采样期间写出帧序列的目录，没有则为null。</summary>
        /// <remarks>单帧无法判断挥击重量感或倒地动作，帧序列可以。</remarks>
        public string ShotFolder { get; init; }

        /// <summary>帧序列采样间隔，单位为秒。</summary>
        public float ShotEverySeconds { get; init; }

        /// <summary>写完采样后是否退出应用。</summary>
        public bool QuitWhenDone { get; init; }

        /// <summary>
        /// 帧序列是否也包括开场；帧时间测量始终不包括开场。
        /// </summary>
        /// <remarks>
        /// 入场镜头在时间冻结时播放，批处理下要评估它只能同时拍摄这些帧。
        /// 显式启用以保留已有序列的帧编号。
        /// </remarks>
        public bool ShotsIncludeIntro { get; init; }

        /// <summary>是否请求了任何采样。</summary>
        public bool IsRequested => Seconds > 0f;

        /// <summary>从命令行读取采样请求，回退到页面URL。</summary>
        /// <param name="arguments">命令行参数，如<c>Environment.GetCommandLineArgs</c>的结果。</param>
        /// <param name="url">页面URL，浏览器外为空。</param>
        /// <returns>采样请求，可能为空。</returns>
        public static PerfCaptureRequest Read(string[] arguments, string url)
        {
            float seconds = 0f;
            string logPath = null;
            string shotPath = null;
            string shotFolder = null;
            float shotEvery = DefaultShotEverySeconds;
            bool quit = false;
            bool shotsIntro = false;

            if (arguments != null)
            {
                for (int i = 0; i < arguments.Length; i++)
                {
                    string next = i + 1 < arguments.Length ? arguments[i + 1] : null;

                    switch (arguments[i])
                    {
                        case CaptureFlag when TryParseSeconds(next, out float parsed):
                            seconds = parsed;
                            break;

                        case LogFlag when !string.IsNullOrEmpty(next):
                            logPath = next;
                            break;

                        case QuitFlag:
                            quit = true;
                            break;

                        case ShotsIntroFlag:
                            shotsIntro = true;
                            break;

                        case ShotFlag when !string.IsNullOrEmpty(next):
                            shotPath = next;
                            break;

                        case ShotsFlag when !string.IsNullOrEmpty(next):
                            shotFolder = next;
                            break;

                        case ShotEveryFlag when TryParseSeconds(next, out float every):
                            shotEvery = every;
                            break;
                    }
                }
            }

            if (seconds <= 0f && TryReadQuerySeconds(url, out float querySeconds))
            {
                seconds = querySeconds;
            }

            return new PerfCaptureRequest
            {
                Seconds = seconds,
                LogPath = logPath,
                QuitWhenDone = quit,
                ShotPath = shotPath,
                ShotFolder = shotFolder,
                ShotEverySeconds = shotEvery,
                ShotsIncludeIntro = shotsIntro
            };
        }

        /// <summary>从页面URL查询字符串读取采样时长。</summary>
        /// <remarks>
        /// 手动解析，因为WebGL没有<c>System.Web</c>，且只需要一个键。
        /// </remarks>
        /// <param name="url">页面URL。</param>
        /// <param name="seconds">请求的采样时长。</param>
        /// <returns>查询中请求采样时返回true。</returns>
        public static bool TryReadQuerySeconds(string url, out float seconds)
        {
            seconds = 0f;

            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            int queryStart = url.IndexOf('?');

            if (queryStart < 0)
            {
                return false;
            }

            string query = url.Substring(queryStart + 1);
            int fragment = query.IndexOf('#');

            if (fragment >= 0)
            {
                query = query.Substring(0, fragment);
            }

            foreach (string pair in query.Split('&'))
            {
                int equals = pair.IndexOf('=');

                if (equals <= 0 || pair.Substring(0, equals) != CaptureQueryKey)
                {
                    continue;
                }

                return TryParseSeconds(pair.Substring(equals + 1), out seconds);
            }

            return false;
        }

        private static bool TryParseSeconds(string text, out float seconds)
        {
            seconds = 0f;

            return !string.IsNullOrEmpty(text) &&
                   float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) &&
                   seconds > 0f;
        }
    }
}
