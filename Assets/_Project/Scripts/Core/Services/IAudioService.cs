using UnityEngine;

namespace AdaptiveBossArena.Core.Services
{
    /// <summary>音量设置对应的混音总线。</summary>
    public enum AudioBus
    {
        /// <summary>整体输出音量。</summary>
        Master = 0,

        /// <summary>背景音乐。</summary>
        Music = 1,

        /// <summary>战斗和环境音效。</summary>
        Effects = 2,

        /// <summary>菜单和 HUD 音效。</summary>
        Interface = 3
    }

    /// <summary>
    /// 全部游戏音频的统一播放接口。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 通过标识而非 <c>AudioClip</c>引用请求声音，使玩法代码不依赖音频资源。实现通过查找资源解析标识；
    /// 占位提示音也可在那里替换为真实录音，无需改动任何玩法脚本。
    /// </para>
    /// <para>
    /// 声部限制也由此接口封装。Boss 连招在八帧内四次命中时，不应叠加四份相同命中音效；
    /// 这是快速战斗中刺耳声音的常见来源。
    /// </para>
    /// </remarks>
    public interface IAudioService
    {
        /// <summary>在世界位置播放一次音效。</summary>
        /// <param name="cueId">要播放的音效标识。</param>
        /// <param name="worldPosition">音效播放位置。</param>
        void PlayCue(string cueId, Vector3 worldPosition);

        /// <summary>播放不带空间定位的一次音效，用于界面反馈。</summary>
        /// <param name="cueId">要播放的音效标识。</param>
        void PlayCue2D(string cueId);

        /// <summary>
        /// 在其他声音下播放循环环境底声，表达战斗所在空间。
        /// </summary>
        /// <remarks>
        /// 与会随战斗增减层次的音乐分开；环境声不随战斗变化。没有录音的音效会使场所保持原有静音。
        /// </remarks>
        /// <param name="cueId">循环音效标识。</param>
        void PlayAmbience(string cueId);

        /// <summary>交叉淡入淡出到音乐曲目。</summary>
        /// <param name="trackId">曲目标识，或为 null 以渐变为静音。</param>
        /// <param name="fadeSeconds">交叉淡入淡出时长。</param>
        void PlayMusic(string trackId, float fadeSeconds = 1f);

        /// <summary>
        /// 设置音乐强度，战斗升级时叠加更高层次。
        /// </summary>
        /// <remarks>
        /// 配乐由叠加循环而非独立曲目组成，使层次增厚时不会突兀切换。零级只有底层，每升一级淡入一层。
        /// 由 Boss阶段索引驱动，因此音乐与 Boss 同步升级。
        /// </remarks>
        /// <param name="level">强度等级，不超过可用层数。</param>
        void SetMusicIntensity(int level);

        /// <summary>
        /// 设置总线音量。
        /// </summary>
        /// <remarks>
        /// 线性应用音量，但人的响度感知并非线性：滑块一半听起来高于一半音量。感知曲线可改善它，
        /// 但必须通过聆听而非纯推理调整，因此这里如实描述。此前注释声称转换为分贝，
        /// 实际实现并未进行该转换。
        /// </remarks>
        /// <param name="bus">要调整的总线。</param>
        /// <param name="linearVolume">零到一之间的音量。</param>
        void SetBusVolume(AudioBus bus, float linearVolume);

        /// <summary>取得混音总线当前音量。</summary>
        /// <param name="bus">要查询的总线。</param>
        /// <returns>零到一之间的音量。</returns>
        float GetBusVolume(AudioBus bus);
    }
}
