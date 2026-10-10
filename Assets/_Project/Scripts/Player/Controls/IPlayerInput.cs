using UnityEngine;

namespace AdaptiveBossArena.Player.Controls
{
    /// <summary>玩家可触发的离散动作。</summary>
    /// <remarks>
    /// 值从零连续排列，可以直接作为缓存时间戳数组索引，使输入缓存无分配且为常数时间。
    /// </remarks>
    public enum PlayerInputAction
    {
        /// <summary>用于回避攻击的闪避。</summary>
        Dash = 0,

        /// <summary>快速普通攻击。</summary>
        LightAttack = 1,

        /// <summary>缓慢、投入风险较高的攻击。</summary>
        HeavyAttack = 2,

        /// <summary>特殊技能。</summary>
        Special = 3,

        /// <summary>恢复生命值。</summary>
        Heal = 4,

        /// <summary>举起防御。在恰当瞬间按下时成为弹反。</summary>
        Guard = 5,

        /// <summary>切换到下一武器。</summary>
        SwapWeapon = 6,

        /// <summary>用刀刃拨开攻击。只能追加，不能插入。</summary>
        Parry = 7
    }

    /// <summary>运行时在生成的输入动作资源中查询的名称。</summary>
    /// <remarks>
    /// 动作名称拼写错误会在运行时取得 null 而非编译时报错，表现为按键静默失效。
    /// 生成器和读取器均使用这些常量，确保两者不会逐渐不一致。
    /// </remarks>
    public static class InputActionNames
    {
        /// <summary>包含全部战斗控制的动作映射。</summary>
        public const string GameplayMap = "Gameplay";

        /// <summary>相机绕行：鼠标移动或右摇杆。由相机读取，而非角色。</summary>
        public const string Look = "Look";

        /// <summary>切换相机对 Boss 的锁定。由相机读取。</summary>
        public const string LockOn = "LockOn";

        /// <summary>在第三人称、俯视和第一人称相机之间循环。由相机读取。</summary>
        public const string CycleCamera = "CycleCamera";

        /// <summary>二维移动轴。</summary>
        public const string Move = "Move";

        /// <summary>用于回避攻击的闪避。</summary>
        public const string Dash = "Dash";

        /// <summary>快速普通攻击。</summary>
        public const string LightAttack = "LightAttack";

        /// <summary>缓慢、投入风险较高的攻击。</summary>
        public const string HeavyAttack = "HeavyAttack";

        /// <summary>特殊技能。</summary>
        public const string Special = "Special";

        /// <summary>恢复生命值。</summary>
        public const string Heal = "Heal";

        /// <summary>举起防御，并在恰当时机弹反。</summary>
        public const string Guard = "Guard";

        /// <summary>切换到下一武器。</summary>
        public const string SwapWeapon = "SwapWeapon";

        /// <summary>独立主动招架，区别于举起防御。</summary>
        public const string Parry = "Parry";

        /// <summary>打开暂停菜单。</summary>
        public const string Pause = "Pause";

        /// <summary>解析缓存动作在资源中的动作名称。</summary>
        /// <param name="action">要解析的动作。</param>
        /// <returns>输入动作资源使用的名称。</returns>
        public static string For(PlayerInputAction action)
        {
            switch (action)
            {
                case PlayerInputAction.Dash: return Dash;
                case PlayerInputAction.LightAttack: return LightAttack;
                case PlayerInputAction.HeavyAttack: return HeavyAttack;
                case PlayerInputAction.Special: return Special;
                case PlayerInputAction.Heal: return Heal;
                case PlayerInputAction.Guard: return Guard;
                case PlayerInputAction.SwapWeapon: return SwapWeapon;
                case PlayerInputAction.Parry: return Parry;
                default: return string.Empty;
            }
        }
    }

    /// <summary>
    /// 玩家角色看到的控制接口。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 置于 Input System 与角色之间，使玩法代码不直接访问输入包。
    /// 这样运行模式测试可用脚本伪输入驱动角色，切换输入后端也只需改一个类。
    /// </para>
    /// <para>
    /// 此接口必须留在 Player 程序集。移到 Core 会让 Boss 程序集触及玩家意图，
    /// 破坏项目最核心的设计限制。
    /// </para>
    /// </remarks>
    public interface IPlayerInput
    {
        /// <summary>移动轴，归一化后长度最多为一。</summary>
        Vector2 MoveDirection { get; }

        /// <summary>动作被触发的单帧内为 true。</summary>
        /// <param name="action">要检查的动作。</param>
        /// <returns>动作是否在本帧开始。</returns>
        bool WasPressedThisFrame(PlayerInputAction action);

        /// <summary>动作对应按键持续按住时为 true。</summary>
        /// <param name="action">要检查的动作。</param>
        /// <returns>当前是否按住控制键。</returns>
        bool IsHeld(PlayerInputAction action);

        /// <summary>启用或暂停输入，在暂停或死亡时使用。</summary>
        /// <param name="enabled">为 true 时接收输入。</param>
        void SetEnabled(bool enabled);
    }
}
