using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AdaptiveBossArena.Game;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 将每个声音标识指向应播放的录音文件。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 以前所有声音都由音调和噪声合成，玩家听来随机且不准确。录音保存在 <see cref="SoundFolder"/>，
    /// 下载自免费 CC0 来源：Kenney 冲击与 RPG 音效包，以及保留 Freesound 原名、
    /// 以声音 ID开头的独立片段。
    /// </para>
    /// <para>
    /// 音效按文件名匹配，名称描述所属时刻而非可能因解压方式变化的路径。多个匹配文件构成多个变体。
    /// 没有匹配文件的音效保留合成声音，因此资源尚未全部下载时游戏声音仍然完整。
    /// </para>
    /// </remarks>
    public static class AudioAssetBuilder
    {
        /// <summary>下载音效的保存位置。</summary>
        public const string SoundFolder = "Assets/_Project/Audio/ThirdParty";

        private static readonly string[] SoundExtensions = { ".ogg", ".wav", ".mp3", ".aif", ".aiff", ".flac" };

        /// <summary>
        /// 每个音效标识对应的录音及音高。
        /// </summary>
        /// <remarks>
        /// 骑士盾牌是涂装木材，因此格挡使用木声；弹反从刀刃发出金属声；招架使用更明亮的钟鸣碰撞声，
        /// 避免两者混淆。巨兽脚步复用重物撞击声并大幅降低音高，表现质量。
        /// </remarks>
        public static readonly (string Cue, string Pattern, float Pitch)[] Mapping =
        {
            (AudioService.Cues.Block, @"^impactWood_heavy_\d+$", 1f),
            (AudioService.Cues.Deflect, @"^impactMetal_heavy_\d+$", 1f),
            (AudioService.Cues.Parry, @"^impactBell_heavy_\d+$", 1f),
            (AudioService.Cues.HitLight, @"^impactPunch_medium_\d+$", 1f),
            (AudioService.Cues.HitHeavy, @"^impactPunch_heavy_\d+$", 0.9f),
            (AudioService.Cues.PostureBreak, @"^impactPlate_heavy_\d+$", 0.85f),
            (AudioService.Cues.FootstepPlayer, @"^footstep_concrete_\d+$", 1f),
            (AudioService.Cues.FootstepBoss, @"^impactSoft_heavy_\d+$", 0.7f),
            (AudioService.Cues.WeaponDraw, @"^drawKnife\d*$", 1f),
            (AudioService.Cues.Execution, @"^knifeSlice\d*$", 0.9f),
            (AudioService.Cues.GuardRaise, @"^cloth\d+$", 1f),
            (AudioService.Cues.PlayerDeath, @"^impactSoft_heavy_\d+$", 0.85f),

            // Freesound 资源按 ID 匹配。咆哮是单个声音，完整播放；挥击和呻吟原文件是合集，
            // 包含五十次挥动或一长串呻吟，按安静间隔切成单独声音后播放。
            // 以前完整播放会让一次挥击触发一串呼啸声。
            (AudioService.Cues.BossRoar, @"^(489901|132874)__", 1f),
            (AudioService.Cues.BossDeath, @"^497056__", 0.9f),
            (AudioService.Cues.SwingGreatsword, @"^507470_take\d+$", 1f),
            (AudioService.Cues.SwingBlade, @"^(317849|471097)_take\d+$", 1f),
            (AudioService.Cues.Whoosh, @"^(317849|471097)_take\d+$", 1.1f),
            (AudioService.Cues.BossGrunt, @"^(125405|869056)_take\d+$", 0.9f),
            (AudioService.Cues.Ambience, @"^174279_loop$", 1f)
        };

        /// <summary>下载文件夹中所有声音文件，按不含扩展名的文件名索引。</summary>
        private static List<(string Name, string Path)> Sounds()
        {
            if (!Directory.Exists(SoundFolder))
            {
                return new List<(string, string)>();
            }

            return Directory.GetFiles(SoundFolder, "*.*", SearchOption.AllDirectories)
                .Where(path => SoundExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                .Select(path => (Path.GetFileNameWithoutExtension(path), path.Replace('\\', '/')))
                .OrderBy(sound => sound.Item1)
                .ToList();
        }

        /// <summary>某个音效标识匹配的录音，按名称排列。</summary>
        /// <param name="pattern">该音效播放的文件名模式。</param>
        /// <returns>音频片段。</returns>
        public static AudioClip[] ClipsMatching(string pattern)
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);

            return Sounds()
                .Where(sound => regex.IsMatch(sound.Name))
                .Select(sound => AssetDatabase.LoadAssetAtPath<AudioClip>(sound.Path))
                .Where(clip => clip != null)
                .ToArray();
        }

        /// <summary>将找到的录音写入音频服务的音效覆盖配置。</summary>
        /// <param name="audio">场景音频服务。</param>
        /// <returns>现在使用录音播放的音效标识数量。</returns>
        public static int Assign(AudioService audio)
        {
            var found = new List<(string Cue, AudioClip[] Clips, float Pitch)>();

            foreach ((string cue, string pattern, float pitch) in Mapping)
            {
                AudioClip[] clips = ClipsMatching(pattern);

                if (clips.Length > 0)
                {
                    found.Add((cue, clips, pitch));
                }
            }

            var serialized = new SerializedObject(audio);
            SerializedProperty overrides = serialized.FindProperty("_cueOverrides");
            overrides.arraySize = found.Count;

            for (int i = 0; i < found.Count; i++)
            {
                SerializedProperty entry = overrides.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("CueId").stringValue = found[i].Cue;
                entry.FindPropertyRelative("Clip").objectReferenceValue = found[i].Clips[0];
                entry.FindPropertyRelative("Pitch").floatValue = found[i].Pitch;

                SerializedProperty takes = entry.FindPropertyRelative("Clips");
                takes.arraySize = found[i].Clips.Length;

                for (int t = 0; t < found[i].Clips.Length; t++)
                {
                    takes.GetArrayElementAtIndex(t).objectReferenceValue = found[i].Clips[t];
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[Adaptive Boss Arena] {found.Count} sound cues play recordings from {SoundFolder}.");

            return found.Count;
        }
    }
}
