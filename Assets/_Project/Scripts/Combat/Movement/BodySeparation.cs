using UnityEngine;

namespace AdaptiveBossArena.Combat.Movement
{
    /// <summary>
    /// 将两个重叠的身体推开，较轻的一方移动更远。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 过去战斗者的角色控制器直接相互碰撞，这让它们具有实体但缺少物理感：走向首领会让骑士完全停止，
    /// 而首领以每秒十六米冲向骑士也会完全停止。双方都不退让，因此接触在任何方向都没有重量。
    /// </para>
    /// <para>
    /// 这里按质量分摊并移动双方身体以消除重叠。玩家向首领挤压时，稍微推动它，自己则被大幅推回；
    /// 冲锋首领会将玩家推在前方。每帧纠正全部重叠，因此身体不会出现可见的相互穿透。
    /// </para>
    /// <para>
    /// 有一个重要保护：控制器不再互相阻挡后，足够长的帧可能让一个身体中心越过另一个，
    /// 简单处理会把它从另一侧推出——穿过首领。记录双方上帧所在的一侧，越过的双方会被推回原侧。
    /// </para>
    /// </remarks>
    public static class BodySeparation
    {
        /// <summary>分开两个身体所需的纠正位移。</summary>
        public readonly struct Result
        {
            /// <summary>身体是否发生重叠。</summary>
            public bool Overlapped { get; init; }

            /// <summary>要施加给第一个身体的水平位移。</summary>
            public Vector3 PushFirst { get; init; }

            /// <summary>要施加给第二个身体的水平位移。</summary>
            public Vector3 PushSecond { get; init; }

            /// <summary>
            /// 处理后从第一个身体指向第二个身体的单位方向。下一帧将其作为上一方向传回，
            /// 以识别双方交叉。
            /// </summary>
            public Vector3 Direction { get; init; }
        }

        /// <summary>处理水平面上两个直立身体之间的重叠。</summary>
        /// <param name="first">第一个身体的位置。</param>
        /// <param name="firstRadius">第一个身体的水平半径。</param>
        /// <param name="firstMass">第一个身体的质量，更重的身体移动更少。</param>
        /// <param name="second">第二个身体的位置。</param>
        /// <param name="secondRadius">第二个身体的水平半径。</param>
        /// <param name="secondMass">第二个身体的质量。</param>
        /// <param name="previousDirection">
        /// 上帧从第一个身体指向第二个身体的方向，未知时为零。
        /// </param>
        /// <returns>纠正位移以及需要记住的方向。</returns>
        public static Result Resolve(
            Vector3 first,
            float firstRadius,
            float firstMass,
            Vector3 second,
            float secondRadius,
            float secondMass,
            Vector3 previousDirection)
        {
            Vector3 between = second - first;
            between.y = 0f;

            float distance = between.magnitude;
            float contact = Mathf.Max(0f, firstRadius) + Mathf.Max(0f, secondRadius);

            Vector3 direction = distance > Mathf.Epsilon
                ? between / distance
                : FallbackDirection(previousDirection);

            // 双方中心从上帧起发生交叉：身体穿过了彼此。沿新方向处理会完成穿透，因此优先使用原侧，
            // 并且除了接触间隙，还必须撤销双方之间的完整距离。但仅在仍处于接触范围内时处理。
            // 若双方相比上帧位于相反侧但相距很远，说明它们被主动移动——重试会传送双方——此时不能猛拉它
            // 们重新靠近。
            bool crossed = distance < contact &&
                           previousDirection.sqrMagnitude > Mathf.Epsilon &&
                           Vector3.Dot(direction, previousDirection) < 0f;

            if (crossed)
            {
                direction = previousDirection.normalized;
            }

            float overlap = crossed ? contact + distance : contact - distance;

            if (overlap <= 0f)
            {
                return new Result { Overlapped = false, Direction = direction };
            }

            float totalMass = Mathf.Max(Mathf.Epsilon, Mathf.Max(0f, firstMass) + Mathf.Max(0f, secondMass));

            // 每个身体按对方占总质量的比例移动：重的一方几乎不动，轻的一方几乎承担全部位移。
            float firstShare = Mathf.Max(0f, secondMass) / totalMass;

            return new Result
            {
                Overlapped = true,
                PushFirst = -direction * (overlap * firstShare),
                PushSecond = direction * (overlap * (1f - firstShare)),
                Direction = direction
            };
        }

        private static Vector3 FallbackDirection(Vector3 previousDirection)
        {
            previousDirection.y = 0f;

            // 完全重合且没有历史时，不存在有意义的一侧；任何一致的选择都比让身体卡在彼此内部更好。
            return previousDirection.sqrMagnitude > Mathf.Epsilon
                ? previousDirection.normalized
                : Vector3.forward;
        }
    }
}
