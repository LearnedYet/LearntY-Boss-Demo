using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdaptiveBossArena.Learning
{
    /// <summary>
    /// 将首领的玩家画像转为战后报告中的易懂语句。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 整个遭遇战围绕首领研究玩家展开，但此前这段叙事只存在于F1 后的开发者叠加层。
    /// 这里将同一画像渲染为战斗结束后向玩家展示的档案：“这是它识破的你的习惯”，
    /// 让玩家得到机制在整场战斗中逐渐积累的回报。
    /// </para>
    /// <para>
    /// 与 <see cref="AdaptiveBossArena.Combat.DefenceResolver"/> 一样为纯静态逻辑，
    /// 因此哪些习惯值得提及、如何排序，可以通过编辑模式测试固定，无需在结算界面目测。
    /// 这里只报告<em>强烈</em>的习惯：数值接近零但可信度高的读数（“你几乎不空挥”）不是首领据以反制的习
    /// 惯，列出它会歪曲战斗实际情况。
    /// </para>
    /// </remarks>
    public static class ProfileReport
    {
        /// <summary>值得向玩家报告某项习惯所需的最低可信度。</summary>
        public const float ConfidenceFloor = 0.35f;

        /// <summary>将特征视为习惯而非习惯缺失所需的最低强度。</summary>
        public const float ValueFloor = 0.4f;

        /// <summary>
        /// 以面向玩家的语句报告首领确信的习惯，最强的排在前面。
        /// </summary>
        /// <param name="profile">战斗结束时首领的玩家画像。</param>
        /// <param name="confidenceFloor">报告某项习惯所需的最低可信度。</param>
        /// <param name="valueFloor">报告某项习惯所需的最低强度。</param>
        /// <returns>排序后的语句；首领未明确识破玩家时为空。</returns>
        public static IReadOnlyList<string> HabitLines(
            BehaviorProfile profile,
            float confidenceFloor = ConfidenceFloor,
            float valueFloor = ValueFloor)
        {
            var scored = new List<(float weighted, string line)>();

            foreach (BehaviorFeature feature in Enum.GetValues(typeof(BehaviorFeature)))
            {
                FeatureReading reading = profile.Get(feature);

                if (reading.Confidence < confidenceFloor || reading.Value < valueFloor)
                {
                    continue;
                }

                int percent = Mathf.RoundToInt(reading.Confidence * 100f);
                scored.Add((reading.Weighted, $"{Describe(feature)}  （可信度 {percent}%）"));
            }

            // 最充分的证据排在前面，使档案以首领最确信的判断开头。
            scored.Sort((a, b) => b.weighted.CompareTo(a.weighted));

            var lines = new List<string>(scored.Count);
            foreach ((float _, string line) in scored)
            {
                lines.Add(line);
            }

            return lines;
        }

        /// <summary>对强烈习惯使用的玩家可见语句。</summary>
        /// <remarks>
        /// 每句描述对应特征的<em>高</em>值含义，因为只有强烈读数才会进入这里。
        /// 习惯的缺失（例如被动）则通过首领采纳的反制策略及其提示来报告。
        /// </remarks>
        private static string Describe(BehaviorFeature feature) => feature switch
        {
            BehaviorFeature.AttackFrequency => "你持续发动猛烈的进攻。",
            BehaviorFeature.HeavyAttackRatio => "你偏爱使用重攻击。",
            BehaviorFeature.PostDodgeAggression => "你总在闪避结束后立刻出手。",
            BehaviorFeature.DashDirectionBias => "你习惯朝同一方向翻滚。",
            BehaviorFeature.PreferredDistance => "你倾向于保持距离作战。",
            BehaviorFeature.Aggression => "你不断逼近，不肯退让。",
            BehaviorFeature.WhiffRatio => "你的攻击经常落空。",
            BehaviorFeature.HealFrequency => "你经常中断交战来治疗。",
            BehaviorFeature.DodgeTimingConsistency => "你的完美闪避总遵循相同的节奏。",
            BehaviorFeature.EvasionSuccess => "你躲过了它几乎所有的攻击。",
            BehaviorFeature.GuardReliance => "你依赖防御来抵挡攻击，而很少移动。",
            BehaviorFeature.DeflectSkill => "你一次次精准把握弹反的时机。",
            _ => feature.ToString()
        };
    }
}
