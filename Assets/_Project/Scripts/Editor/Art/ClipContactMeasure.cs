using System.Collections.Generic;
using System.IO;
using System.Linq;
using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Combat.Feel;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Editor.Art
{
    /// <summary>
    /// 通过采样角色右手在片段中的运动，测量每段攻击动画的打击时刻。
    /// </summary>
    /// <remarks>
    /// 可无图形界面运行：用片段摆姿无需 GPU。测量本身使用以下纯逻辑规则：<see cref="ClipContact"/>；
    /// 这里仅收集它要读取的手部速度。
    /// </remarks>
    public static class ClipContactMeasure
    {
        private const int Samples = 48;

        /// <summary>
        /// 其运动用于标记攻击部位接触时刻的骨骼。
        /// </summary>
        /// <remarks>
        /// 踢击在脚最快时命中而非手最快时；跃击在身体落下时命中。全部按右手计时，
        /// 曾让踢击命中跟随手臂偶然摆动，而跃击在半空就判定打击。
        /// </remarks>
        /// <param name="parts">此次攻击使用的部位。</param>
        /// <returns>要测量的骨骼。</returns>
        public static HumanBodyBones BoneFor(StrikerParts parts)
        {
            if ((parts & StrikerParts.RightFoot) != 0)
            {
                return HumanBodyBones.RightFoot;
            }

            if (parts == StrikerParts.Body)
            {
                return HumanBodyBones.Hips;
            }

            if ((parts & (StrikerParts.Weapon | StrikerParts.RightHand)) == 0 && (parts & StrikerParts.LeftHand) != 0)
            {
                return HumanBodyBones.LeftHand;
            }

            return HumanBodyBones.RightHand;
        }

        /// <summary>表格在某状态播放的片段的接触时间比例。</summary>
        /// <param name="table">战斗角色片段表。</param>
        /// <param name="state">攻击状态。</param>
        /// <param name="bone">其速度峰值对应打击时刻的骨骼；未另行指定时使用持武器的手。</param>
        /// <returns>测得的比例；缺少片段或骨架时为零。</returns>
        public static float ContactFor(CharacterClipTable table, string state, HumanBodyBones bone = HumanBodyBones.RightHand)
        {
            if (!table.States.TryGetValue(state, out string clipName))
            {
                return 0f;
            }

            AnimationClip clip = FindClip(table.ClipSources, clipName);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(table.RigModel);

            if (clip == null || model == null)
            {
                return 0f;
            }

            GameObject body = Object.Instantiate(model);

            try
            {
                Animator animator = body.GetComponentInChildren<Animator>();
                Transform hand = animator != null && animator.isHuman ? animator.GetBoneTransform(bone) : null;
                Transform hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;

                if (hand == null || hips == null)
                {
                    return 0f;
                }

                var speeds = new float[Samples];
                Vector3 previous = Vector3.zero;
                float step = clip.length / (Samples - 1);

                for (int i = 0; i < Samples; i++)
                {
                    clip.SampleAnimation(body, i * step);

                    // 相对于髋部测量，避免把跃起或突进带动的全身位移误认为手部挥动。
                    // 髋部自身按其实际位置测量，对应身体升起和落地。
                    Vector3 local = bone == HumanBodyBones.Hips ? hips.position : hand.position - hips.position;
                    speeds[i] = i == 0 ? 0f : (local - previous).magnitude / Mathf.Max(0.0001f, step);
                    previous = local;
                }

                return ClipContact.PeakFraction(speeds);
            }
            finally
            {
                Object.DestroyImmediate(body);
            }
        }

        private static AnimationClip FindClip(IEnumerable<string> sources, string name)
        {
            foreach (string source in sources)
            {
                IEnumerable<string> files = AssetDatabase.IsValidFolder(source)
                    ? Directory.GetFiles(source, "*.fbx", SearchOption.AllDirectories).Select(f => f.Replace(Path.DirectorySeparatorChar, '/'))
                    : new[] { source };

                AnimationClip found = files.SelectMany(AssetDatabase.LoadAllAssetsAtPath)
                    .OfType<AnimationClip>()
                    .FirstOrDefault(c => c.name == name);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
