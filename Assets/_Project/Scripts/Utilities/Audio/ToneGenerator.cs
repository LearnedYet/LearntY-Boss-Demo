using System;
using UnityEngine;

namespace AdaptiveBossArena.Utilities.Audio
{
    /// <summary>
    /// 在运行时合成游戏音效。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 最初项目没有音频资源和音效设计师，但动作游戏缺少声音就难以产生良好手感：
    /// 无声打击会让人以为没有命中。因此用噪声和简单振荡器生成每种提示音。
    /// </para>
    /// <para>
    /// 方法刻意保持简单，重点满足战斗反馈：尖锐瞬态让声音准确落在命中帧，短衰减避免连续交换混成一团，
    /// 不同音色让玩家不看画面也能区分弹反和格挡。弹反用金属鸣响，格挡用沉闷撞击，挥击用滤波噪声扫频。
    /// </para>
    /// <para>
    /// 所有音频均由程序按种子生成，每次运行一致，无需附带音频文件。
    /// </para>
    /// </remarks>
    public static class ToneGenerator
    {
        /// <summary>音频系统未报告采样率时采用的默认值。</summary>
        public const int FallbackSampleRate = 44100;

        /// <summary>单次音效结尾淡出到静音的时长，单位为秒。</summary>
        private const float ReleaseSeconds = 0.004f;

        /// <summary>
        /// 所有生成音频使用的采样率。
        /// </summary>
        /// <remarks>
        /// 读取音频系统实际采样率，避免每次播放重采样。项目输出为48 kHz，而以前硬编码为44.1 kHz，
        /// 造成无谓运行时转换。无音频设备的批处理模式等情况下使用回退值。
        /// </remarks>
        public static int SampleRate
        {
            get
            {
                int configured = AudioSettings.outputSampleRate;

                return configured > 0 ? configured : FallbackSampleRate;
            }
        }

        /// <summary>
        /// 生成打击声：经过快速衰减的噪声爆发。
        /// </summary>
        /// <remarks>
        /// 冲击是宽频声音，因此使用噪声而非单一音调。较长衰减可以在不改变音量的情况下传达更重的打击。
        /// </remarks>
        /// <param name="name">音频名称。</param>
        /// <param name="durationSeconds">总时长。</param>
        /// <param name="lowPassHz">截止频率，越低越沉闷、厚重。</param>
        /// <param name="seed">随机种子，使各次运行的音频一致。</param>
        /// <param name="peak">
        /// 打击音量，与截止频率独立，避免音色变闷时自动变小。
        /// </param>
        /// <returns>生成的音频。</returns>
        public static AudioClip CreateImpact(
            string name,
            float durationSeconds = 0.18f,
            float lowPassHz = 1400f,
            int seed = 1,
            float peak = 0.75f)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * durationSeconds));
            var samples = new float[sampleCount];

            var random = new System.Random(seed);
            float previous = 0f;
            float smoothing = Mathf.Clamp01(lowPassHz / (SampleRate * 0.5f));

            for (int i = 0; i < sampleCount; i++)
            {
                float noise = (float)(random.NextDouble() * 2d - 1d);

                // 单极点低通，计算开销低，足以将白噪声从嘶声变成有重量感的声音。
                previous += (noise - previous) * smoothing;

                float envelope = FastDecay(i / (float)sampleCount, sharpness: 9f);
                samples[i] = previous * envelope;
            }

            return BuildClip(name, samples, peak);
        }

        /// <summary>
        /// 生成有重量感的打击声，叠加接触瞬态、主体和空间反射。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 单个滤波噪声爆发只是带包络的嘶声，过去所有打击都采用这种方式，因此听起来相似且缺乏冲击。
        /// 厚重打击需要明亮接触瞬态、随衰减降低音高的低频主体，以及表现空间的反射。
        /// </para>
        /// <para>
        /// 主体音高下降是关键。固定低音像嗡鸣，随淡出下降则像重物受到撞击，这是鼓音合成中的常用方法。
        /// </para>
        /// </remarks>
        /// <param name="name">音频名称。</param>
        /// <param name="bodyHz">主体最终音高，越低越显得厚重。</param>
        /// <param name="lowPassHz">瞬态的截止频率，越低越沉闷。</param>
        /// <param name="durationSeconds">包括空间尾音的总时长。</param>
        /// <param name="peak">打击音量。</param>
        /// <param name="roomAmount">背景中竞技场空间反射的强度。</param>
        /// <param name="seed">随机种子，使各次运行的音频一致。</param>
        /// <returns>生成的音频。</returns>
        public static AudioClip CreateWeightedImpact(
            string name,
            float bodyHz = 90f,
            float lowPassHz = 1400f,
            float durationSeconds = 0.32f,
            float peak = 0.85f,
            float roomAmount = 0.5f,
            int seed = 3)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * durationSeconds));
            var samples = new float[sampleCount];

            var random = new System.Random(seed);
            float previous = 0f;
            float smoothing = Mathf.Clamp01(lowPassHz / (SampleRate * 0.5f));
            float phase = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                float progress = i / (float)sampleCount;

                // 接触层：短促、明亮、几乎立即消失的噪声裂响。
                float noise = (float)(random.NextDouble() * 2d - 1d);
                previous += (noise - previous) * smoothing;
                float crack = previous * FastDecay(progress, sharpness: 26f);

                // 重量层：正弦音从较高音高下降到稳定音高。
                float sweep = Mathf.Lerp(2.2f, 1f, Mathf.Clamp01(progress * 5f));
                phase += 2f * Mathf.PI * bodyHz * sweep / SampleRate;
                float body = Mathf.Sin(phase) * FastDecay(progress, sharpness: 7f);

                samples[i] = Saturate((crack * 0.7f) + (body * 0.9f));
            }

            ApplyRoomTail(samples, roomAmount);

            return BuildClip(name, samples, peak);
        }

        /// <summary>
        /// 为死亡生成悠长、下沉的钟鸣。
        /// </summary>
        /// <remarks>
        /// 过去双方都没有死亡音效，战斗最关键时刻无声结束。此声音刻意成为音效库中最缓慢的一项：
        /// 低频音簇逐渐降调并延长衰减，让玩家感知到结束。
        /// </remarks>
        /// <param name="name">音频名称。</param>
        /// <param name="rootHz">最低分音。</param>
        /// <param name="durationSeconds">总时长。</param>
        /// <param name="peak">钟鸣音量。</param>
        /// <returns>生成的音频。</returns>
        public static AudioClip CreateDeathKnell(
            string name,
            float rootHz = 110f,
            float durationSeconds = 1.8f,
            float peak = 0.9f)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * durationSeconds));
            var samples = new float[sampleCount];

            // 根音上方的小三度和下方的八度，形成沉重、明确不同于胜利的声音。
            float[] partials = { 0.5f, 1f, 1.19f };
            float[] gains = { 1f, 0.7f, 0.45f };
            var phases = new float[partials.Length];

            for (int i = 0; i < sampleCount; i++)
            {
                float progress = i / (float)sampleCount;

                // 整个音频下降一个全音，传达坍塌感。
                float sag = Mathf.Lerp(1f, 0.89f, progress);
                float value = 0f;

                for (int p = 0; p < partials.Length; p++)
                {
                    phases[p] += 2f * Mathf.PI * rootHz * partials[p] * sag / SampleRate;
                    value += Mathf.Sin(phases[p]) * gains[p];
                }

                // 缓慢起音使声音逐渐膨胀，再进入长衰减。
                float attack = Mathf.Clamp01(progress * 12f);
                samples[i] = Saturate(value * 0.4f) * attack * FastDecay(progress, sharpness: 2.6f);
            }

            ApplyRoomTail(samples, 0.7f);

            return BuildClip(name, samples, peak);
        }

        /// <summary>
        /// 为成功弹反生成金属鸣响。
        /// </summary>
        /// <remarks>
        /// 使用多个非谐波分音；谐波相关音像乐器，刻意失谐的分音更像金属撞击，符合招架反馈。
        /// </remarks>
        /// <param name="name">音频名称。</param>
        /// <param name="rootHz">最低分音。</param>
        /// <param name="durationSeconds">总时长。</param>
        /// <returns>生成的音频。</returns>
        public static AudioClip CreateMetallicRing(
            string name,
            float rootHz = 880f,
            float durationSeconds = 0.5f,
            float peak = 0.7f)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * durationSeconds));
            var samples = new float[sampleCount];

            float[] partials = { 1f, 2.76f, 5.4f, 8.93f };
            float[] gains = { 1f, 0.6f, 0.36f, 0.22f };

            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)SampleRate;
                float value = 0f;

                for (int p = 0; p < partials.Length; p++)
                {
                    value += Mathf.Sin(2f * Mathf.PI * rootHz * partials[p] * time) * gains[p];
                }

                // 像真实金属一样，让较高分音先衰减。
                float envelope = FastDecay(i / (float)sampleCount, sharpness: 4.5f);
                samples[i] = value * envelope * 0.25f;
            }

            return BuildClip(name, samples, peak);
        }

        /// <summary>
        /// 为治疗等恢复时刻生成柔和、上升的闪光音。
        /// </summary>
        /// <remarks>
        /// 使用谐波分音，区别于金属鸣响的非谐波分音，传达温暖和音乐性。柔和起音与轻微升调表现恢复感。
        /// </remarks>
        /// <param name="name">音频名称。</param>
        /// <param name="baseHz">铃音基频。</param>
        /// <param name="durationSeconds">总时长。</param>
        /// <returns>生成的音频。</returns>
        public static AudioClip CreateShimmer(
            string name,
            float baseHz = 660f,
            float durationSeconds = 0.6f,
            float peak = 0.55f)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * durationSeconds));
            var samples = new float[sampleCount];

            float[] partials = { 1f, 1.5f, 2f, 3f };
            float[] gains = { 1f, 0.5f, 0.32f, 0.18f };

            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)SampleRate;
                float progress = i / (float)sampleCount;

                // 轻微上滑音高传达上升和恢复。
                float glide = Mathf.Lerp(0.97f, 1.06f, progress);

                float value = 0f;
                for (int p = 0; p < partials.Length; p++)
                {
                    value += Mathf.Sin(2f * Mathf.PI * baseHz * partials[p] * glide * time) * gains[p];
                }

                // 柔和起音避免尖锐瞬态，缓慢衰减让声音展开。
                float attack = Mathf.Clamp01(progress / 0.09f);
                float decay = FastDecay(progress, sharpness: 3.2f);

                samples[i] = value * attack * decay * 0.16f;
            }

            return BuildClip(name, samples, peak);
        }

        /// <summary>
        /// 生成挥击破空声：截止频率先升后降的滤波噪声。
        /// </summary>
        /// <remarks>
        /// 先升后降的形状表现从耳边掠过的运动；在前摇播放，还能提供听觉预警。
        /// </remarks>
        /// <param name="name">音频名称。</param>
        /// <param name="durationSeconds">总时长。</param>
        /// <param name="seed">用于复现的随机种子。</param>
        /// <returns>生成的音频。</returns>
        /// <param name="brightness">
        /// 缩放截止频率扫描高度。小于一时厚重、宽松，大于一时轻薄、迅速，
        /// 让共享招式的三种武器仍能凭声音区分。
        /// </param>
        public static AudioClip CreateWhoosh(
            string name,
            float durationSeconds = 0.3f,
            int seed = 7,
            float peak = 0.45f,
            float brightness = 1f)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * durationSeconds));
            var samples = new float[sampleCount];

            var random = new System.Random(seed);
            float previous = 0f;
            float ceiling = Mathf.Clamp(0.35f * brightness, 0.02f, 0.95f);

            for (int i = 0; i < sampleCount; i++)
            {
                float progress = i / (float)sampleCount;

                // 截止频率先上升再回落，使声音像物体掠过。
                float sweep = Mathf.Sin(progress * Mathf.PI);
                float smoothing = Mathf.Lerp(0.02f, ceiling, sweep);

                float noise = (float)(random.NextDouble() * 2d - 1d);
                previous += (noise - previous) * smoothing;

                samples[i] = previous * sweep * 0.5f;
            }

            return BuildClip(name, samples, peak);
        }

        /// <summary>
        /// 为无法格挡的危险攻击生成尖锐警报音。
        /// </summary>
        /// <remarks>
        /// 三全音配合快速震音，立刻传达危险。在红色预警完全展开前，玩家已经听见不能格挡的警告。
        /// </remarks>
        /// <param name="name">音频名称。</param>
        /// <param name="rootHz">警报基频。</param>
        /// <param name="durationSeconds">总时长。</param>
        /// <returns>生成的音频。</returns>
        public static AudioClip CreatePerilWarning(
            string name,
            float rootHz = 300f,
            float durationSeconds = 0.42f,
            float peak = 0.85f)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * durationSeconds));
            var samples = new float[sampleCount];

            float[] partials = { 1f, 1.414f, 2f };
            float[] gains = { 1f, 0.85f, 0.4f };

            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)SampleRate;
                float progress = i / (float)sampleCount;

                float value = 0f;
                for (int p = 0; p < partials.Length; p++)
                {
                    value += Mathf.Sin(2f * Mathf.PI * rootHz * partials[p] * time) * gains[p];
                }

                // 快速震音使其像警报一样脉动。
                float tremolo = 0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * 18f * time);
                float envelope = FastDecay(progress, sharpness: 2.4f);

                samples[i] = value * envelope * tremolo * 0.2f;
            }

            return BuildClip(name, samples, peak);
        }

        /// <summary>
        /// 为Boss全力挥击后失衡生成不稳定的低沉踉跄声。
        /// </summary>
        /// <remarks>
        /// 声音逐渐降调，配合缓慢摆动和轻微失谐分音，表现失去平衡的沉重踉跄，让玩家识别进攻机会。
        /// </remarks>
        /// <param name="name">音频名称。</param>
        /// <param name="rootHz">基频，重物失去立足点应使用低频。</param>
        /// <param name="durationSeconds">总时长。</param>
        /// <returns>生成的音频。</returns>
        public static AudioClip CreateStumble(
            string name,
            float rootHz = 165f,
            float durationSeconds = 0.55f,
            float peak = 0.7f)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * durationSeconds));
            var samples = new float[sampleCount];

            // 轻微失谐分音使音调无法完全稳定，传达不稳感。
            float[] partials = { 1f, 1.5f, 2.02f };
            float[] gains = { 1f, 0.5f, 0.3f };

            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)SampleRate;
                float progress = i / (float)sampleCount;

                // 音高下降配合缓慢摆动，表现踉跄式下坠。
                float glide = Mathf.Lerp(1f, 0.72f, progress);
                float wobble = 1f + 0.04f * Mathf.Sin(2f * Mathf.PI * 6.5f * time);

                float value = 0f;
                for (int p = 0; p < partials.Length; p++)
                {
                    value += Mathf.Sin(2f * Mathf.PI * rootHz * partials[p] * glide * wobble * time) * gains[p];
                }

                // 柔和敲击后接长而沉重的下落，表现Boss恢复站稳。
                float attack = Mathf.Clamp01(progress / 0.06f);
                float envelope = attack * FastDecay(progress, sharpness: 1.6f);

                samples[i] = value * envelope * 0.22f;
            }

            return BuildClip(name, samples, peak);
        }

        /// <summary>
        /// 为Boss阶段切换生成低沉咆哮。
        /// </summary>
        /// <param name="name">音频名称。</param>
        /// <param name="baseHz">基频，越低越显得庞大。</param>
        /// <param name="durationSeconds">总时长。</param>
        /// <param name="seed">用于复现的随机种子。</param>
        /// <returns>生成的音频。</returns>
        public static AudioClip CreateRoar(
            string name,
            float baseHz = 70f,
            float durationSeconds = 1.4f,
            int seed = 13,
            float peak = 0.95f)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * durationSeconds));
            var samples = new float[sampleCount];

            var random = new System.Random(seed);
            float noiseState = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)SampleRate;
                float progress = i / (float)sampleCount;

                // 咆哮逐渐降调，表现用力和体量。
                float pitch = baseHz * Mathf.Lerp(1.15f, 0.85f, progress);

                float fundamental = Mathf.Sin(2f * Mathf.PI * pitch * time);
                float growl = Mathf.Sin(2f * Mathf.PI * pitch * 1.5f * time) * 0.4f;

                float noise = (float)(random.NextDouble() * 2d - 1d);
                noiseState += (noise - noiseState) * 0.06f;

                float envelope = Mathf.Sin(progress * Mathf.PI);
                samples[i] = (fundamental + growl + noiseState * 0.7f) * envelope * 0.3f;
            }

            return BuildClip(name, samples, peak);
        }

        /// <summary>
        /// 为音乐背景生成持续低音。
        /// </summary>
        /// <remarks>
        /// 由音频服务循环并分层，随着Boss升级淡入高层。没有旋律的紧张感适合没有叙事节拍的单场遭遇战。
        /// </remarks>
        /// <param name="name">音频名称。</param>
        /// <param name="rootHz">基频。</param>
        /// <param name="durationSeconds">循环时长。</param>
        /// <returns>生成的音频。</returns>
        public static AudioClip CreateDrone(
            string name, float rootHz = 55f, float durationSeconds = 4f, float peak = 0.4f)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * durationSeconds));
            var samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)SampleRate;

                // 轻微失谐的同度音产生柔和拍频，减少合成器感。
                float a = Mathf.Sin(2f * Mathf.PI * rootHz * time);
                float b = Mathf.Sin(2f * Mathf.PI * rootHz * 1.004f * time);
                float fifth = Mathf.Sin(2f * Mathf.PI * rootHz * 1.5f * time) * 0.35f;

                samples[i] = (a + b + fifth) * 0.18f;
            }

            return BuildLoopingClip(name, samples, peak);
        }

        /// <summary>
        /// 为音乐顶层生成节奏脉冲音。
        /// </summary>
        /// <remarks>
        /// 背景低音提供持续紧张，脉冲层像心跳，用稳定节拍控制音调。只在最终阶段淡入，增加紧迫感。
        /// 循环时长包含整数拍，避免接缝处节奏跳变。
        /// </remarks>
        /// <param name="name">音频名称。</param>
        /// <param name="rootHz">基频。</param>
        /// <param name="beatsPerMinute">脉冲频率。</param>
        /// <param name="beats">循环包含的拍数。</param>
        /// <returns>生成的音频。</returns>
        public static AudioClip CreatePulse(
            string name,
            float rootHz = 110f,
            float beatsPerMinute = 120f,
            int beats = 8,
            float peak = 0.35f)
        {
            float beatSeconds = 60f / Mathf.Max(1f, beatsPerMinute);
            int beatSamples = Mathf.Max(1, Mathf.RoundToInt(SampleRate * beatSeconds));
            int sampleCount = beatSamples * Mathf.Max(1, beats);
            var samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)SampleRate;

                float tone = Mathf.Sin(2f * Mathf.PI * rootHz * time)
                             + Mathf.Sin(2f * Mathf.PI * rootHz * 1.5f * time) * 0.3f;

                // 每拍中尖锐起音和快速衰减提供推进感。
                float withinBeat = (i % beatSamples) / (float)beatSamples;
                float pulse = FastDecay(withinBeat, sharpness: 6f);

                samples[i] = tone * pulse * 0.16f;
            }

            return BuildLoopingClip(name, samples, peak);
        }

        /// <summary>按音频播放进度归一化的指数衰减包络。</summary>
        private static float FastDecay(float progress, float sharpness) =>
            Mathf.Exp(-progress * sharpness);

        /// <summary>早期反射延迟，单位为秒。</summary>
        /// <remarks>
        /// 延迟刻意互不成整数倍。等距抽头会强化单一频率，产生梳状滤波音色，而非空间感。
        /// </remarks>
        private static readonly float[] ReflectionSeconds = { 0.011f, 0.023f, 0.037f };

        /// <summary>各次反射相对干声的音量。</summary>
        private static readonly float[] ReflectionGains = { 0.5f, 0.32f, 0.2f };

        /// <summary>
        /// 添加少量早期反射，让声音具有空间来源。
        /// </summary>
        /// <remarks>
        /// 过去音频完全没有空间反射，缺少体量感。石质竞技场中的真实冲击紧接着会有房间反射；
        /// 几个短延迟抽头只需遍历缓冲区一次，比混响便宜，也能支持多个声音同时播放。
        /// </remarks>
        /// <param name="samples">原地添加反射的缓冲区。</param>
        /// <param name="amount">房间反射存在感，约从零到一。</param>
        private static void ApplyRoomTail(float[] samples, float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            // 反射取自干声，不使用正在修改的结果，避免抽头彼此反馈形成越来越长的混响。
            var dry = (float[])samples.Clone();

            for (int tap = 0; tap < ReflectionSeconds.Length; tap++)
            {
                int delay = Mathf.RoundToInt(SampleRate * ReflectionSeconds[tap]);

                if (delay <= 0 || delay >= samples.Length)
                {
                    continue;
                }

                float gain = ReflectionGains[tap] * amount;

                for (int i = delay; i < samples.Length; i++)
                {
                    samples[i] += dry[i - delay] * gain;
                }
            }
        }

        /// <summary>
        /// 平滑压低峰值，避免直接削波。
        /// </summary>
        /// <remarks>
        /// 叠加的撞击声和裂响容易超出幅度范围；硬削波会产生刺耳爆裂，柔和压缩则保留响度感。
        /// </remarks>
        private static float Saturate(float sample) => (float)System.Math.Tanh(sample);

        /// <summary>
        /// 缩放缓冲区，使最响采样达到指定峰值。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 让音量成为明确配置。单极点低通会随截止频率降低而衰减，输出约按截止频率平方根变化。
        /// 以前轻击2200 Hz比重击、处决和85 Hz低生命心跳响数倍，音量关系反而错误。
        /// </para>
        /// <para>
        /// 将每个缓冲区归一化到明确峰值，消除这种耦合，让截止频率选择<em>音色</em>、
        /// 峰值参数选择<em>响度</em>。脚步轻是主动配置的结果，而非滤波器副作用。
        /// </para>
        /// </remarks>
        /// <param name="samples">原地缩放的缓冲区。</param>
        /// <param name="targetPeak">期望绝对峰值，范围零到一。</param>
        private static void NormalisePeak(float[] samples, float targetPeak)
        {
            float loudest = 0f;

            for (int i = 0; i < samples.Length; i++)
            {
                loudest = Mathf.Max(loudest, Mathf.Abs(samples[i]));
            }

            // 静音缓冲区无需缩放，除以其峰值会造成除零。
            if (loudest <= Mathf.Epsilon)
            {
                return;
            }

            float scale = Mathf.Clamp01(targetPeak) / loudest;

            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] *= scale;
            }
        }

        /// <summary>
        /// 将最后几毫秒淡出到静音。
        /// </summary>
        /// <remarks>
        /// 指数衰减不会真正到零，直接截断会产生阶跃和点击声。此前失衡音结束时仍有峰值五分之一，
        /// 危险警报仍有十分之一。几毫秒线性淡出不易察觉，却能消除这些点击。
        /// </remarks>
        private static void ApplyReleaseTaper(float[] samples)
        {
            int taper = Mathf.Min(samples.Length, Mathf.RoundToInt(SampleRate * ReleaseSeconds));

            for (int i = 0; i < taper; i++)
            {
                // 从结尾向前处理，淡出数组索引0对应最后一个采样。
                samples[samples.Length - 1 - i] *= i / (float)taper;
            }
        }

        /// <summary>
        /// 返回缩短的缓冲区，使结尾与开头连续，无点击循环。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 按淡出长度裁短缓冲区，将丢弃尾部混入开头。循环接缝处最后保留的采样之后，
        /// 会接上原缓冲区中真正紧随的采样，保证连续。
        /// </para>
        /// <para>
        /// 以前原地将尾部向开头淡变而不缩短，最终采样落在开头的<em>淡出长度位置</em>，
        /// 循环却返回首采样，不连续只是被转移。对本来完整周期的缓冲区应用该处理，
        /// 还会在接缝前引入提前出现的幽灵起音。
        /// </para>
        /// </remarks>
        private static float[] MakeSeamless(float[] samples)
        {
            int fade = Mathf.Min(samples.Length / 8, SampleRate / 20);

            if (fade <= 0 || samples.Length <= fade * 2)
            {
                return samples;
            }

            int length = samples.Length - fade;
            var result = new float[length];
            Array.Copy(samples, result, length);

            for (int i = 0; i < fade; i++)
            {
                float blend = i / (float)fade;
                result[i] = Mathf.Lerp(samples[length + i], samples[i], blend);
            }

            return result;
        }

        /// <summary>将单次音效缓冲区包装成已淡出、归一化的音频。</summary>
        /// <param name="name">音频名称。</param>
        /// <param name="samples">采样缓冲区。</param>
        /// <param name="peak">提示音响度，范围零到一。</param>
        private static AudioClip BuildClip(string name, float[] samples, float peak)
        {
            if (samples == null || samples.Length == 0)
            {
                throw new ArgumentException("Cannot build a clip with no samples.", nameof(samples));
            }

            ApplyReleaseTaper(samples);
            NormalisePeak(samples, peak);

            return CreateClip(name, samples);
        }

        /// <summary>
        /// 将循环缓冲区包装成无缝且归一化的音频。
        /// </summary>
        /// <remarks>
        /// 刻意不在结尾淡出到静音，否则每轮循环都会出现声音空洞。
        /// </remarks>
        /// <param name="name">音频名称。</param>
        /// <param name="samples">采样缓冲区。</param>
        /// <param name="peak">该音乐层响度，范围零到一。</param>
        private static AudioClip BuildLoopingClip(string name, float[] samples, float peak)
        {
            if (samples == null || samples.Length == 0)
            {
                throw new ArgumentException("Cannot build a clip with no samples.", nameof(samples));
            }

            float[] seamless = MakeSeamless(samples);
            NormalisePeak(seamless, peak);

            return CreateClip(name, seamless);
        }

        /// <summary>将完成的缓冲区交给Unity。</summary>
        private static AudioClip CreateClip(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, channels: 1, SampleRate, stream: false);
            clip.SetData(samples, 0);

            return clip;
        }
    }
}
