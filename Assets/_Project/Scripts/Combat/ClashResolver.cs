using UnityEngine;

namespace AdaptiveBossArena.Combat
{
    /// <summary>
    /// 判断两次有效挥击是否击中彼此而非各自目标。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 刀刃相撞是决斗需要的第三种结果：招架是一方识破另一方，互换伤害是双方都命中，相撞则双方都未赢。
    /// 若没有它，同时挥击会退化为数值更大一方总能胜出的伤害竞赛。
    /// </para>
    /// <para>
    /// 决策保持纯逻辑并与检测位置分离，因为这是规则，而本项目的规则用编辑模式测试固定。
    /// 检测必须位于能同时看到双方的位置，也就是遭遇战导演。
    /// </para>
    /// </remarks>
    public static class ClashResolver
    {
        /// <summary>
        /// 双方必须多么正对彼此，以点积表示。
        /// </summary>
        /// <remarks>
        /// 大致为四十五度。挥击朝向彼此时刀刃才相遇；从侧面被击中的角色是露出破绽，而非旗鼓相当，
        /// 将其判作相撞会剥夺攻击者已经赢得的命中。
        /// </remarks>
        private const float MinimumFacingDot = 0.7f;

        /// <summary>
        /// 两次挥击是否应互相弹开而非命中。
        /// </summary>
        /// <param name="firstIsSwinging">第一名角色的攻击判定是否开启。</param>
        /// <param name="secondIsSwinging">第二名角色的攻击判定是否开启。</param>
        /// <param name="separation">双方距离。</param>
        /// <param name="combinedReach">两次有效攻击的范围之和。</param>
        /// <param name="facingDot">
        /// 双方正对程度：各自朝向与连线的对齐程度取较小值。取较小值意味着任一角色背对对手，
        /// 就足以判为命中而非相撞，这才公平。
        /// </param>
        /// <returns>双方挥击都应被拒绝时为 true。</returns>
        public static bool ShouldClash(
            bool firstIsSwinging,
            bool secondIsSwinging,
            float separation,
            float combinedReach,
            float facingDot)
        {
            if (!firstIsSwinging || !secondIsSwinging)
            {
                return false;
            }

            // 使用合计范围的一半，而非全部；攻击范围仅在某处重叠不代表刀刃位于同一位置。
            // 要求双方距离小于平均范围，可确保相撞只发生在真正相遇的挥击之间。
            if (separation > combinedReach * 0.5f)
            {
                return false;
            }

            return facingDot >= MinimumFacingDot;
        }

        /// <summary>
        /// 为 <see cref="ShouldClash"/> 计算双方正对程度。
        /// </summary>
        /// <remarks>
        /// 仅在水平面测量，与命中弧一致；角色稍微站高一些不意味着正对程度降低。
        /// </remarks>
        /// <param name="firstPosition">第一名角色的位置。</param>
        /// <param name="firstForward">第一名角色的朝向。</param>
        /// <param name="secondPosition">第二名角色的位置。</param>
        /// <param name="secondForward">第二名角色的朝向。</param>
        /// <returns>双方朝向与连线对齐程度中的较小值。</returns>
        public static float FacingDot(
            Vector3 firstPosition,
            Vector3 firstForward,
            Vector3 secondPosition,
            Vector3 secondForward)
        {
            Vector3 between = secondPosition - firstPosition;
            between.y = 0f;

            if (between.sqrMagnitude < Mathf.Epsilon)
            {
                return 1f;
            }

            between.Normalize();

            return Mathf.Min(
                Vector3.Dot(Flatten(firstForward), between),
                Vector3.Dot(Flatten(secondForward), -between));
        }

        /// <summary>去掉竖直分量并重新归一化；退化时返回前方。</summary>
        private static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;

            return direction.sqrMagnitude < Mathf.Epsilon ? Vector3.forward : direction.normalized;
        }
    }
}
