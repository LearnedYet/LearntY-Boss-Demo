using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Editor.Art
{
    /// <summary>
    /// 从着地脚读取移动片段带动身体的方向与速度。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mixamo 片段原地播放，髋部不位移，因此导入器报告没有运动。但脚仍能说明方向：较低的脚处于着地状态，
    /// 身体向前行走时着地脚会在其下方向后滑动。整个循环中着地脚滑动的平均反向速度，
    /// 就是片段的移动速度。
    /// </para>
    /// <para>
    /// 使用测量结果而非文件名，因为名称会误导。“great sword run” 实际向后迈步；它曾被用作巨兽跑步动作，
    /// 让向前跑的巨兽双腿却在后退。
    /// </para>
    /// </remarks>
    public static class ClipTravelMeasure
    {
        private const int Samples = 60;

        /// <summary>模型自身坐标中的片段移动速度：x 为横向，y 为前向，单位为米每秒。</summary>
        /// <param name="table">用于播放片段姿势的战斗角色。</param>
        /// <param name="clipName">动画片段。</param>
        /// <returns>移动速度；缺少片段或模型时为零。</returns>
        public static Vector2 TravelOf(CharacterClipTable table, string clipName)
        {
            AnimationClip clip = FindClip(table.ClipSources, clipName);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(table.RigModel);

            if (clip == null || model == null)
            {
                return Vector2.zero;
            }

            GameObject body = Object.Instantiate(model);

            try
            {
                Animator animator = body.GetComponentInChildren<Animator>();

                if (animator == null || !animator.isHuman)
                {
                    return Vector2.zero;
                }

                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                Transform left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform right = animator.GetBoneTransform(HumanBodyBones.RightFoot);

                float step = clip.length / Samples;
                Vector3 slide = Vector3.zero, previousLeft = Vector3.zero, previousRight = Vector3.zero;

                for (int i = 0; i <= Samples; i++)
                {
                    clip.SampleAnimation(body, i * step);

                    Vector3 leftFoot = body.transform.InverseTransformVector(left.position - hips.position);
                    Vector3 rightFoot = body.transform.InverseTransformVector(right.position - hips.position);

                    if (i > 0 && step > 0f)
                    {
                        Vector3 moved = left.position.y < right.position.y ? leftFoot - previousLeft : rightFoot - previousRight;
                        slide += new Vector3(moved.x, 0f, moved.z) / step;
                    }

                    previousLeft = leftFoot;
                    previousRight = rightFoot;
                }

                Vector3 travel = -slide / Samples;
                return new Vector2(travel.x, travel.z);
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
