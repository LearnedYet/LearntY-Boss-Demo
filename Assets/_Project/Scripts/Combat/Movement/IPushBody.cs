using UnityEngine;

namespace AdaptiveBossArena.Combat.Movement
{
    /// <summary>
    /// 供阻止两个身体占据相同空间的对象观察的战斗者身体。
    /// </summary>
    /// <remarks>
    /// 让组合根能够推开玩家和首领，而不需要任一程序集引用另一方。防火墙禁止 AI 直接查看玩家；
    /// 这个狭窄接口让已经能看到双方的对象——遭遇导演——处理双方物理交互，
    /// 同时不暴露任何一方可用于作弊的信息。
    /// </remarks>
    public interface IPushBody
    {
        /// <summary>身体地面中心的世界位置。</summary>
        Vector3 BodyPosition { get; }

        /// <summary>身体的水平半径。</summary>
        float BodyRadius { get; }

        /// <summary>相对质量，较重的身体在接触时退让更少。</summary>
        float BodyMass { get; }

        /// <summary>身体当前是否参与接触。</summary>
        bool IsSolid { get; }

        /// <summary>
        /// 按水平偏移移动身体，同时仍与世界碰撞。
        /// </summary>
        /// <param name="offset">要施加的位移，忽略垂直分量。</param>
        void Displace(Vector3 offset);
    }
}
