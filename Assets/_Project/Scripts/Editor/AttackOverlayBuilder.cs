using AdaptiveBossArena.Combat.Feel;
using UnityEngine;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 挂载用于显示战斗角色攻击的地面覆盖图。
    /// </summary>
    /// <remarks>
    /// 双方预制体构建器共用，因为需求相同且遗漏后果严重：没有覆盖图的攻击仍产生伤害和 hit-stop，
    /// 却没有可见提示，玩家甚至无法判断自己是否发动了攻击。
    /// </remarks>
    internal static class AttackOverlayBuilder
    {
        /// <summary>将覆盖图添加为战斗角色根对象的子对象。</summary>
        /// <param name="parent">战斗角色的 Transform。</param>
        public static void Attach(Transform parent)
        {
            var overlayObject = new GameObject("AttackOverlay");
            overlayObject.transform.SetParent(parent, false);

            var visualizer = overlayObject.AddComponent<AttackVisualizer>();
            visualizer.SetMaterial(MaterialLibrary.GetOrCreateAttackOverlay());
        }
    }
}
