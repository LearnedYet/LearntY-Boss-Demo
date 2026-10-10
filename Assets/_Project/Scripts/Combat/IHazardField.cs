using UnityEngine;

namespace AdaptiveBossArena.Combat
{
    /// <summary>
    /// 按需生成持续地面危险区域，调用者无需了解对象池管理。
    /// </summary>
    /// <remarks>
    /// 作为可选依赖注入首领的 <see cref="AttackExecutor"/>。玩家执行器不获得此依赖，
    /// 因此只有首领能在地面留下痕迹；通过依赖缺席封闭能力，而非运行时检查。
    /// </remarks>
    public interface IHazardField
    {
        /// <summary>在地面点位生成危险区域。</summary>
        /// <param name="center">位于地面高度的世界空间中心。</param>
        /// <param name="radius">危险区域半径。</param>
        /// <param name="damagePerTick">每跳对站在其中的目标造成的伤害。</param>
        /// <param name="durationSeconds">区域保持危险的时长。</param>
        void Spawn(Vector3 center, float radius, float damagePerTick, float durationSeconds);
    }
}
