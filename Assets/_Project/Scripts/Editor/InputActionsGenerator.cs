using System.IO;
using AdaptiveBossArena.Player.Controls;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 从代码生成项目输入动作资产。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 通过 Input System 自身编辑 API 在内存构建资产，再用以下方法写出：
    /// <see cref="InputActionAsset.ToJson"/>。手写 JSON 需要手工生成绑定标识符，
    /// 任一格式错误都会导致导入失败；让包序列化自身格式可避免这类错误。
    /// </para>
    /// <para>
    /// 所有绑定按以下常量定义： <see cref="InputActionNames"/>，
    /// 保证生成器和运行时读取器对动作名称一致。
    /// </para>
    /// </remarks>
    public static class InputActionsGenerator
    {
        /// <summary>生成资产的写入位置。</summary>
        public const string AssetPath = "Assets/_Project/Settings/PlayerControls.inputactions";

        private const string KeyboardMouseScheme = "Keyboard&Mouse";
        private const string GamepadScheme = "Gamepad";

        /// <summary>创建或覆盖输入动作资产。</summary>
        [MenuItem(EditorMenus.Setup + "Generate Input Actions", priority = EditorMenus.SetupPriorityGenerateAssets + 1)]
        public static void GenerateInputActions()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();

            try
            {
                asset.name = Path.GetFileNameWithoutExtension(AssetPath);

                BuildControlSchemes(asset);
                BuildGameplayMap(asset);

                AssetAuthoring.EnsureFolderExists(Path.GetDirectoryName(AssetPath));
                File.WriteAllText(AssetPath, asset.ToJson());
            }
            finally
            {
                // 内存资产仅用于序列化，磁盘文件才是实际产物，Unity 导入器会据此创建自身实例。
                Object.DestroyImmediate(asset);
            }

            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[Adaptive Boss Arena] Input actions generated at '{AssetPath}'.");
        }

        /// <summary>声明设备分组，便于之后按方案筛选绑定。</summary>
        private static void BuildControlSchemes(InputActionAsset asset)
        {
            asset.AddControlScheme(KeyboardMouseScheme)
                .WithRequiredDevice<Keyboard>()
                .WithOptionalDevice<Mouse>();

            asset.AddControlScheme(GamepadScheme)
                .WithRequiredDevice<Gamepad>();
        }

        /// <summary>构建战斗动作映射及其全部绑定。</summary>
        private static void BuildGameplayMap(InputActionAsset asset)
        {
            InputActionMap map = asset.AddActionMap(InputActionNames.GameplayMap);

            BuildMoveAction(map);
            BuildLookAction(map);

            // 闪避绑定在拇指无需离开移动键就可触及的按键，因为它最常在承压和移动中使用。
            BuildButton(map, InputActionNames.Dash, "<Keyboard>/space", "<Gamepad>/buttonEast");

            // 鼠标右键持续格挡，由已握鼠标的手操作；玩家要求鼠标控制格挡和弹反，
            // Shift 现在改为重攻击修饰键。
            BuildButton(map, InputActionNames.Guard, "<Mouse>/rightButton", "<Gamepad>/leftShoulder");

            BuildButton(map, InputActionNames.LightAttack, "<Mouse>/leftButton", "<Gamepad>/buttonWest");
            BuildHeavyAttack(map);

            // 招架是独立投入攻击，不是时机正确的格挡，因此有独立按键。
            BuildButton(map, InputActionNames.Parry, "<Keyboard>/q", "<Gamepad>/rightShoulder");

            // 使用 E 而非 Q；Q 用于招架。
            BuildButton(map, InputActionNames.Special, "<Keyboard>/e", "<Gamepad>/buttonNorth");
            BuildButton(map, InputActionNames.Heal, "<Keyboard>/r", "<Gamepad>/dpad/up");

            // 刻意不使用负责锁定的 Tab。两个动作绑定同键会同时切武器和解除锁定，看起来像游戏出错。
            BuildButton(map, InputActionNames.SwapWeapon, "<Keyboard>/v", "<Gamepad>/dpad/right");
            BuildButton(map, InputActionNames.Pause, "<Keyboard>/escape", "<Gamepad>/start");

            // 摄像机控制定义在资产中而非硬编码按键，因此有手柄绑定，也能像其他动作一样重绑。
            InputAction lockOn = map.AddAction(InputActionNames.LockOn, InputActionType.Button);
            lockOn.AddBinding("<Keyboard>/tab", groups: KeyboardMouseScheme);
            lockOn.AddBinding("<Mouse>/middleButton", groups: KeyboardMouseScheme);
            lockOn.AddBinding("<Gamepad>/rightStickPress", groups: GamepadScheme);

            BuildButton(map, InputActionNames.CycleCamera, "<Keyboard>/c", "<Gamepad>/dpad/down");
        }

        /// <summary>
        /// 重攻击：按住 Shift 加鼠标左键，或手柄右扳机。
        /// </summary>
        /// <remarks>
        /// 使用修饰键组合，根据是否按住 Shift 将同一按钮解释为轻或重攻击。两者都收到点击，
        /// 因此读取器在同帧触发重攻击时丢弃轻攻击。
        /// </remarks>
        private static void BuildHeavyAttack(InputActionMap map)
        {
            InputAction heavy = map.AddAction(InputActionNames.HeavyAttack, InputActionType.Button);

            heavy.AddCompositeBinding("OneModifier")
                .With("Modifier", "<Keyboard>/leftShift", KeyboardMouseScheme)
                .With("Binding", "<Mouse>/leftButton", KeyboardMouseScheme);

            heavy.AddBinding("<Gamepad>/rightTrigger", groups: GamepadScheme);
        }

        /// <summary>摄像机环绕：鼠标原始像素位移，或右摇杆控制速率。</summary>
        private static void BuildLookAction(InputActionMap map)
        {
            InputAction look = map.AddAction(
                InputActionNames.Look,
                InputActionType.Value,
                expectedControlLayout: nameof(Vector2));

            look.AddBinding("<Mouse>/delta", groups: KeyboardMouseScheme);
            look.AddBinding("<Gamepad>/rightStick", groups: GamepadScheme).WithProcessor("stickDeadzone");
        }

        /// <summary>用键盘组合和手柄摇杆构建移动轴。</summary>
        private static void BuildMoveAction(InputActionMap map)
        {
            // 注意参数名称：编辑 API 称其为预期控制 layout，
            // 但序列化到 .inputactions 的字段名为 expecte dControlType。混用会导致编译错误，
            // 而不是运行时问题。
            InputAction move = map.AddAction(
                InputActionNames.Move,
                InputActionType.Value,
                expectedControlLayout: nameof(Vector2));

            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w", KeyboardMouseScheme)
                .With("Down", "<Keyboard>/s", KeyboardMouseScheme)
                .With("Left", "<Keyboard>/a", KeyboardMouseScheme)
                .With("Right", "<Keyboard>/d", KeyboardMouseScheme);

            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow", KeyboardMouseScheme)
                .With("Down", "<Keyboard>/downArrow", KeyboardMouseScheme)
                .With("Left", "<Keyboard>/leftArrow", KeyboardMouseScheme)
                .With("Right", "<Keyboard>/rightArrow", KeyboardMouseScheme);

            // 磨损手柄摇杆轻微偏转不得让角色自行移动。
            move.AddBinding("<Gamepad>/leftStick", groups: GamepadScheme)
                .WithProcessor("stickDeadzone");
        }

        /// <summary>构建同时绑定一个键鼠控制和一个手柄控制的按钮动作。</summary>
        private static void BuildButton(
            InputActionMap map,
            string actionName,
            string keyboardPath,
            string gamepadPath)
        {
            InputAction action = map.AddAction(actionName, InputActionType.Button);

            action.AddBinding(keyboardPath, groups: KeyboardMouseScheme);
            action.AddBinding(gamepadPath, groups: GamepadScheme);
        }
    }
}
