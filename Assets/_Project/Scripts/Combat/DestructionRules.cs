using AdaptiveBossArena.Core.Services;
using UnityEngine;

namespace AdaptiveBossArena.Combat
{
    /// <summary>
    /// 决定破坏什么以及碎片飞向何方的石材破坏规则。
    /// </summary>
    /// <remarks>
    /// 保持纯逻辑，使应该和不应该打碎旁边矮墙的砸击都能测试。
    /// 每个随机选择都使用 <see cref="IRandomProvider"/>，绝不使用 <c>UnityEngine.Random</c>，
    /// 确保相同种子的运行以相同方式破坏相同石材。
    /// </remarks>
    public static class DestructionRules
    {
        /// <summary>沿地面测量冲击是否到达可破坏对象。</summary>
        /// <remarks>
        /// 只计算水平面。砸击落在地面，而石材位于其上一米；直线距离会导致旁边物体越高，
        /// 同一砸击能破坏的范围越小。
        /// </remarks>
        /// <param name="impact">攻击落点。</param>
        /// <param name="reach">从落点向外的破坏距离。</param>
        /// <param name="target">可破坏对象中心。</param>
        /// <returns>目标在范围内时为 true。</returns>
        public static bool Reaches(Vector3 impact, float reach, Vector3 target)
        {
            float dx = target.x - impact.x;
            float dz = target.z - impact.z;

            return dx * dx + dz * dz <= reach * reach;
        }

        /// <summary>单块碎片离开时的速度。</summary>
        /// <remarks>
        /// 先沿地面远离冲击点并向上，再加入散射，使单侧被击中的石材沿攻击方向喷散，
        /// 而非像烟花一样垂直弹起。
        /// </remarks>
        /// <param name="random">散射所用的带种子随机源。</param>
        /// <param name="impact">攻击落点。</param>
        /// <param name="piece">碎片初始位置。</param>
        /// <param name="speed">典型发射速度，单位为米每秒。</param>
        /// <returns>碎片初速度。</returns>
        public static Vector3 PieceVelocity(IRandomProvider random, Vector3 impact, Vector3 piece, float speed)
        {
            Vector3 away = piece - impact;
            away.y = 0f;

            if (away.sqrMagnitude < 0.0001f)
            {
                Vector2 any = random.NextDirectionOnPlane();
                away = new Vector3(any.x, 0f, any.y);
            }

            away.Normalize();

            Vector2 scatter = random.NextDirectionOnPlane() * random.NextFloat(0f, 0.45f);
            Vector3 horizontal = (away + new Vector3(scatter.x, 0f, scatter.y)).normalized;

            return horizontal * (speed * random.NextFloat(0.6f, 1f)) + Vector3.up * (speed * random.NextFloat(0.4f, 0.9f));
        }

        /// <summary>按平台级别确定最多同时存在的碎片数量。</summary>
        /// <param name="isWeb">是否为 WebGL 构建。</param>
        /// <param name="webCapacity">WebGL 上限。</param>
        /// <param name="desktopCapacity">桌面上限。</param>
        /// <returns>不小于一的上限。</returns>
        public static int Capacity(bool isWeb, int webCapacity, int desktopCapacity) =>
            Mathf.Max(1, isWeb ? webCapacity : desktopCapacity);
    }
}
