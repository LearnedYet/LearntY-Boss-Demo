using UnityEngine;

namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 视觉根上的挂接点，用于显示武器模型，并在武器变化时替换。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是武器美术资源的接入点。目前武器仅通过挥击颜色和轨迹体现；
    /// <see cref="WeaponDefinition"/> 不携带网格，因此 <see cref="Equip"/> 接收 null，不显示任何东西。
    /// 给武器指定模型预制体后，原本只改变轨迹颜色的切换也会将刀刃放入角色手中，无需其他代码改动。
    /// </para>
    /// <para>
    /// 挂接点位于视觉根上，比角色根低一层，因此手持模型会随程序化突进移动；
    /// 以后还能重新挂到导入骨架的手骨上，控制器无需知道或关心它最终挂到哪里。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WeaponSocket : MonoBehaviour
    {
        private GameObject _current;

        /// <summary>在此显示武器模型，替换此前显示的模型。</summary>
        /// <remarks>
        /// 传入 null 会清空挂接点，目前每把武器都是如此，因为还没有模型。旧实例直接销毁，而不是回收：
        /// 武器切换只允许在空闲和移动状态进行，因此较少发生；若保留挂接的活动实例，
        /// 每次切换就会叠加刀刃。
        /// </remarks>
        /// <param name="modelPrefab">要实例化的武器模型；为 null 则不显示。</param>
        public void Equip(GameObject modelPrefab)
        {
            if (_current != null)
            {
                Destroy(_current);
                _current = null;
            }

            if (modelPrefab == null)
            {
                return;
            }

            _current = Instantiate(modelPrefab, transform);
            _current.transform.localPosition = Vector3.zero;
            _current.transform.localRotation = Quaternion.identity;
        }
    }
}
