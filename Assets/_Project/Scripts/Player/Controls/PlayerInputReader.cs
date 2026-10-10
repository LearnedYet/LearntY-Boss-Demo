using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AdaptiveBossArena.Player.Controls
{
    /// <summary>
    /// 通过 Input System 包读取控制。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 项目中唯一在运行时接触输入包的类型，因此运行模式测试可替换脚本伪输入，在没有设备时驱动角色。
    /// </para>
    /// <para>
    /// 初始化时解析一次动作，并缓存于以 <see cref="PlayerInputAction"/>索引的数组。
    /// 每帧按字符串查询既浪费，也容易容忍拼写错误而掩盖控制损坏。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour, IPlayerInput
    {
        [SerializeField]
        [Tooltip("Generated input actions asset. Produced by " +
                 "'Adaptive Boss Arena/Setup/Generate Input Actions'.")]
        private InputActionAsset _actions;

        private InputActionMap _gameplayMap;
        private InputAction _moveAction;
        private InputAction[] _actionLookup;
        private bool _isEnabled = true;

        /// <inheritdoc />
        public Vector2 MoveDirection
        {
            get
            {
                if (!_isEnabled || _moveAction == null)
                {
                    return Vector2.zero;
                }

                Vector2 raw = _moveAction.ReadValue<Vector2>();

                // 键盘组合绑定在斜向可能超出单位长度，使斜向跑动明显快于直行。
                return raw.sqrMagnitude > 1f ? raw.normalized : raw;
            }
        }

        /// <inheritdoc />
        public bool WasPressedThisFrame(PlayerInputAction action)
        {
            if (!_isEnabled)
            {
                return false;
            }

            InputAction resolved = _actionLookup[(int)action];

            if (resolved == null || !resolved.WasPressedThisFrame())
            {
                return false;
            }

            // Shift 加点击触发重击，而轻击也看到同次点击。重击优先。
            if (action == PlayerInputAction.LightAttack)
            {
                InputAction heavy = _actionLookup[(int)PlayerInputAction.HeavyAttack];
                return heavy == null || !heavy.WasPressedThisFrame();
            }

            return true;
        }

        /// <inheritdoc />
        public bool IsHeld(PlayerInputAction action)
        {
            if (!_isEnabled)
            {
                return false;
            }

            InputAction resolved = _actionLookup[(int)action];
            return resolved != null && resolved.IsPressed();
        }

        /// <inheritdoc />
        public void SetEnabled(bool enabled) => _isEnabled = enabled;

        private void Awake() => ResolveActions();

        private void OnEnable() => _gameplayMap?.Enable();

        private void OnDisable() => _gameplayMap?.Disable();

        /// <summary>缓存角色引用的全部动作，并报告缺失项。</summary>
        private void ResolveActions()
        {
            int actionCount = Enum.GetValues(typeof(PlayerInputAction)).Length;
            _actionLookup = new InputAction[actionCount];

            if (_actions == null)
            {
                Debug.LogError(
                    "[Adaptive Boss Arena] No input actions asset assigned. The player will not respond " +
                    "to input. Run 'Adaptive Boss Arena/Setup/Generate Input Actions' and assign the result.",
                    this);
                return;
            }

            _gameplayMap = _actions.FindActionMap(InputActionNames.GameplayMap, throwIfNotFound: false);

            if (_gameplayMap == null)
            {
                Debug.LogError(
                    $"[Adaptive Boss Arena] Action map '{InputActionNames.GameplayMap}' was not found in " +
                    $"'{_actions.name}'. The asset may predate a rename.",
                    this);
                return;
            }

            _moveAction = FindOrWarn(InputActionNames.Move);

            for (int i = 0; i < actionCount; i++)
            {
                _actionLookup[i] = FindOrWarn(InputActionNames.For((PlayerInputAction)i));
            }
        }

        /// <summary>解析单个动作，失败时警告而非抛出，使游戏仍能运行。</summary>
        private InputAction FindOrWarn(string actionName)
        {
            InputAction action = _gameplayMap.FindAction(actionName, throwIfNotFound: false);

            if (action == null)
            {
                Debug.LogWarning(
                    $"[Adaptive Boss Arena] Input action '{actionName}' was not found. " +
                    "The corresponding control will do nothing.",
                    this);
            }

            return action;
        }

        /// <summary>设置动作资源，供预制体生成器使用。</summary>
        /// <param name="actions">要读取的动作资源。</param>
        public void SetActionsAsset(InputActionAsset actions) => _actions = actions;
    }
}
