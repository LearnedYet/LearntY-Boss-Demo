using AdaptiveBossArena.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace AdaptiveBossArena.UI
{
    /// <summary>
    /// 显示当前手中武器的名称。
    /// </summary>
    /// <remarks>
    /// 三种武器有不同招式，当前装备具有实际意义。挥击颜色有所提示，
    /// 但战斗中玩家不应靠闪光推测自己的武器；生命附近的标签直接说明。
    /// 首次装备和每次切换的同一信号更新标签，使其始终显示当前武器。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WeaponDisplay : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Carries the name of a newly drawn weapon.")]
        private StringEventChannel _weaponChannel;

        [SerializeField]
        [Tooltip("Label the weapon name is written into.")]
        private Text _label;

        private void OnEnable()
        {
            if (_weaponChannel != null)
            {
                _weaponChannel.Raised += OnWeaponDrawn;
            }
        }

        private void OnDisable()
        {
            if (_weaponChannel != null)
            {
                _weaponChannel.Raised -= OnWeaponDrawn;
            }
        }

        /// <summary>指定通道和标签，供界面生成器使用。</summary>
        /// <param name="weaponChannel">武器装备通道。</param>
        /// <param name="label">要写入的标签。</param>
        public void Bind(StringEventChannel weaponChannel, Text label)
        {
            _weaponChannel = weaponChannel;
            _label = label;
        }

        private void OnWeaponDrawn(string weaponName)
        {
            if (_label != null)
            {
                _label.text = weaponName;
            }
        }
    }
}
