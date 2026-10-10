using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Combat.Feel;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AdaptiveBossArena.Editor.Art
{
    /// <summary>
    /// 将巨兽每次直接打击的攻击范围设为实际攻击部位能够到达的距离。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 命中从扇形判定改为刀刃判定后，手工设置的攻击距离成了身体无法兑现的承诺：横扫设为四米，
    /// 而巨剑只能伸到约三米。首领按该距离规划每次挥击，结果会站在够不到的地方挥空；挥空又会增加决心，
    /// 反而因此得到奖励。
    /// </para>
    /// <para>
    /// 因此改为测量范围。让带真实砍刀和攻击部位的生成巨兽在每段攻击片段的接触时刻附近摆姿，
    /// 取攻击部位距离中心的最远距离，再加上骑士自身厚度作为范围。测量数据在每次设置时、
    /// 预制体已生成后写入。这里不读取玩家信息，只测量巨兽自己的身体。
    /// </para>
    /// </remarks>
    public static class AttackReachMeasure
    {
        /// <summary>接触时刻前后有多少片段区间算作此次打击。</summary>
        private const float ContactHalfWindow = 0.1f;

        private const int Samples = 12;

        /// <summary>测量每个首领直接攻击并写入其范围。</summary>
        [MenuItem(EditorMenus.Setup + "Measure Boss Reach", priority = EditorMenus.SetupPriorityBuildScene - 1)]
        public static void MeasureBossReach()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabBuilder.PrefabPath);
            var config = GeneratedAssets.Config<CharacterAnimationConfig>("DefaultBossAnimation");
            var controller = config != null ? config.AnimatorController as AnimatorController : null;

            if (prefab == null || controller == null)
            {
                return;
            }

            GameObject body = Object.Instantiate(prefab);

            try
            {
                Animator animator = body.GetComponentInChildren<Animator>();
                StrikeVolume[] strikers = body.GetComponentsInChildren<StrikeVolume>(includeInactive: true);

                if (animator == null || strikers.Length == 0)
                {
                    return;
                }

                foreach (AttackClipBinding binding in config.AttackClips)
                {
                    AttackDefinition attack = binding.Attack;

                    if (attack == null || attack.Strikers == StrikerParts.None ||
                        !animator.HasState(0, Animator.StringToHash(binding.State)))
                    {
                        continue;
                    }

                    float contact = binding.ContactFraction > 0f ? binding.ContactFraction : ClipContact.DefaultContact;
                    float reach = Reach(body.transform, animator, binding.State, contact, attack.Strikers, strikers);

                    if (reach <= 0f)
                    {
                        continue;
                    }

                    using (AssetAuthoring.AssetWriter writer = AssetAuthoring.Edit(attack))
                    {
                        writer.Float("_range", reach + PlayerPrefabBuilder.HurtboxRadius);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(body);
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>接触时刻附近，攻击部位在地面平面内距离身体中心的最远距离。</summary>
        /// <remarks>
        /// 通过 Animator 按实际游戏方式摆姿：进入攻击状态、用时间参数定位，并丢弃根运动；
        /// 而不是直接采样片段。直接采样保留游戏中会丢弃的地面位移，曾把滑步攻击测成六米以上。
        /// </remarks>
        private static float Reach(
            Transform root, Animator animator, string state, float contact, StrikerParts parts, StrikeVolume[] strikers)
        {
            float reach = 0f;
            int attackTime = Animator.StringToHash(CharacterAnimatorParameters.AttackTime);

            animator.Rebind();

            for (int i = 0; i < Samples; i++)
            {
                float fraction = Mathf.Clamp01(contact + Mathf.Lerp(-ContactHalfWindow, ContactHalfWindow, i / (Samples - 1f)));
                animator.SetFloat(attackTime, fraction);
                animator.Play(state, 0, fraction);
                animator.Update(0f);

                foreach (StrikeVolume striker in strikers)
                {
                    if ((parts & striker.Part) == 0)
                    {
                        continue;
                    }

                    striker.Segment(out Vector3 start, out Vector3 end);
                    reach = Mathf.Max(reach, Across(root.position, start) + striker.Radius, Across(root.position, end) + striker.Radius);
                }
            }

            return reach;
        }

        private static float Across(Vector3 from, Vector3 to)
        {
            Vector3 offset = to - from;
            offset.y = 0f;

            return offset.magnitude;
        }
    }
}
