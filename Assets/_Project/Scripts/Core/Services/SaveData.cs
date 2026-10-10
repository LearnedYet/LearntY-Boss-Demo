using System;

namespace AdaptiveBossArena.Core.Services
{
    /// <summary>
    /// 玩家可调设置，跨会话持久化。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 使用普通可序列化类而非 ScriptableObject，因为这是存放于存档目录的玩家数据，
    /// 而非随构建发布的项目数据。
    /// </para>
    /// <para>
    /// 每个字段都有合理默认值，因此旧版存档缺少字段时仍可正确加载，只为新增项采用默认值。
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class SettingsData
    {
        /// <summary>写入此记录时使用的结构版本。</summary>
        public int SchemaVersion = CurrentSchemaVersion;

        /// <summary>当前代码写入和理解的版本。</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>
        /// 与音频服务共享的初始音量。
        /// </summary>
        /// <remarks>
        /// 采用命名常量，而非在两处写字面量。服务曾硬编码自己的默认值，其中一项与这里不同，
        /// 导致首次打开设置菜单并推送存档值前，音乐音量一直错误。
        /// </remarks>
        public const float DefaultMasterVolume = 0.8f;

        /// <summary>初始音乐音量。</summary>
        public const float DefaultMusicVolume = 0.7f;

        /// <summary>初始音效音量。</summary>
        public const float DefaultEffectsVolume = 0.9f;

        /// <summary>整体输出音量，范围零到一。</summary>
        public float MasterVolume = DefaultMasterVolume;

        /// <summary>音乐音量，范围零到一。</summary>
        public float MusicVolume = DefaultMusicVolume;

        /// <summary>音效音量，范围零到一。</summary>
        public float EffectsVolume = DefaultEffectsVolume;

        /// <summary>
        /// 相机震动强度，范围零到一。
        /// </summary>
        /// <remarks>
        /// 暴露完整范围而非开关。屏幕震动对部分玩家确实是无障碍问题，
        /// 而对多数人“更少”比“完全没有”更有用。
        /// </remarks>
        public float ScreenShakeIntensity = 1f;

        /// <summary>
        /// 为 true 时减弱受击闪光。
        /// </summary>
        /// <remarks>快速全屏闪烁有光敏风险，必须可以关闭。</remarks>
        public bool ReducedFlashing;

        /// <summary>为 true 时，Boss 适应提示在屏幕上停留更久。</summary>
        public bool ExtendedTellDuration;

        /// <summary>鼠标或摇杆控制相机转动速度的倍率，默认为一。</summary>
        public float MouseSensitivity = LookSettings.DefaultSensitivity;

        /// <summary>为 true 时，鼠标上移使视野向下。</summary>
        public bool InvertLook;

        /// <summary>玩家选择的画质，范围零到二；负一表示由游戏选择。</summary>
        public int GraphicsQuality = -1;

        /// <summary>按 Input System 自身 JSON 格式序列化的输入重绑定。</summary>
        public string InputRebinds = string.Empty;
    }

    /// <summary>玩家对战 Boss 的最佳结果。</summary>
    /// <remarks>
    /// 刻意与设置分开保存。玩家清除设置不应丢失纪录，设置文件损坏也不应损失纪录。
    /// </remarks>
    [Serializable]
    public sealed class RecordsData
    {
        /// <summary>写入此记录时使用的结构版本。</summary>
        public int SchemaVersion = CurrentSchemaVersion;

        /// <summary>当前代码写入和理解的版本。</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Boss 是否曾被击败。</summary>
        public bool HasWon;

        /// <summary>最快胜利秒数。从未击败 Boss 时为零。</summary>
        public float FastestVictorySeconds;

        /// <summary>玩家获胜时剩余生命比例的最高值。</summary>
        public float BestRemainingHealth;

        /// <summary>总尝试次数。</summary>
        public int TotalAttempts;

        /// <summary>获胜战斗中 Boss 成功形成的最少反制策略数。</summary>
        /// <remarks>
        /// 本游戏最有趣的成绩。快速取胜是一种技巧；获胜且不给 Boss 可学习的规律则更难，
        /// 因为这要求不断变换打法，而非反复使用一个强力战术。
        /// </remarks>
        public int FewestAdaptationsAllowed = int.MaxValue;

        /// <summary>记录完成的尝试，保留更好的结果。</summary>
        /// <param name="won">Boss 是否被击败。</param>
        /// <param name="durationSeconds">此次尝试持续的时间。</param>
        /// <param name="remainingHealth">结束时玩家生命比例。</param>
        /// <param name="adaptationsAllowed">Boss 形成的反制策略数。</param>
        public void RecordAttempt(
            bool won,
            float durationSeconds,
            float remainingHealth,
            int adaptationsAllowed)
        {
            TotalAttempts++;

            if (!won)
            {
                return;
            }

            // 只有胜利才更新最佳纪录，因此故意失败无法污染纪录。
            if (!HasWon || durationSeconds < FastestVictorySeconds)
            {
                FastestVictorySeconds = durationSeconds;
            }

            if (remainingHealth > BestRemainingHealth)
            {
                BestRemainingHealth = remainingHealth;
            }

            if (adaptationsAllowed < FewestAdaptationsAllowed)
            {
                FewestAdaptationsAllowed = adaptationsAllowed;
            }

            HasWon = true;
        }
    }
}
