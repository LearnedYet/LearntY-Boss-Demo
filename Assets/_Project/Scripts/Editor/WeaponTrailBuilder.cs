using AdaptiveBossArena.Combat.Feel;
using UnityEngine;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 将挥击拖影挂到战斗角色武器上。
    /// </summary>
    /// <remarks>
    /// 双方构建器共用，玩家与首领需要同样效果但长度、颜色不同。集中维护让拖影只调一次，
    /// 避免两角色逐渐不一致。
    /// </remarks>
    public static class WeaponTrailBuilder
    {
        /// <summary>沿挂点前向两个距离之间添加扫过的拖影。</summary>
        /// <param name="mount">刀刃所沿 Transform：武器挂点，无挂点时为身体。</param>
        /// <param name="localPosition">发射器相对挂点的位置。</param>
        /// <param name="baseDistance">沿挂点前向的拖影内缘距离。</param>
        /// <param name="tipDistance">沿挂点前向的外缘距离。</param>
        /// <param name="colour">拖影头部颜色；以光叠加，因此保持较暗。</param>
        public static void Attach(Transform mount, Vector3 localPosition, float baseDistance, float tipDistance, Color colour)
        {
            var trailObject = new GameObject("WeaponTrail");
            trailObject.transform.SetParent(mount, false);
            trailObject.transform.localPosition = localPosition;
            trailObject.transform.localRotation = Quaternion.identity;

            trailObject.AddComponent<WeaponTrail>()
                .Configure(baseDistance, tipDistance, colour, MaterialLibrary.GetOrCreateWeaponTrail());
        }
    }
}
