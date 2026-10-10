using System;

namespace AdaptiveBossArena.Core.Services
{
    /// <summary>
    /// 当前生效的画质，以及由玩家还是游戏选择。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 <see cref="LookSettings"/> 一样放在 Core，因为设置菜单写入，
    /// 照明和特效池等多个程序集中的系统读取，而它们不能互相依赖。
    /// </para>
    /// <para>
    /// 变化会发出通知，因为画质可在战斗中改变：菜单立即应用选择，
    /// 浏览器也会根据机器性能测量自动提高或降低等级。
    /// </para>
    /// </remarks>
    public static class GraphicsTier
    {
        /// <summary>当前生效的画质。</summary>
        public static GraphicsQuality Current { get; private set; } = GraphicsQuality.Medium;

        /// <summary>玩家选择画质前为 true；游戏仅在此期间可自行调整。</summary>
        public static bool IsAutomatic { get; private set; } = true;

        /// <summary>生效画质变化时触发。</summary>
        public static event Action<GraphicsQuality> Changed;

        /// <summary>设置游戏自动选择的画质。玩家已选择后忽略。</summary>
        /// <param name="quality">要使用的画质。</param>
        public static void SetAutomatic(GraphicsQuality quality)
        {
            if (IsAutomatic)
            {
                Apply(quality);
            }
        }

        /// <summary>设置玩家选择的画质，游戏永不覆盖该选择。</summary>
        /// <param name="quality">要使用的画质。</param>
        public static void SetChosen(GraphicsQuality quality)
        {
            IsAutomatic = false;
            Apply(quality);
        }

        private static void Apply(GraphicsQuality quality)
        {
            if (quality == Current)
            {
                return;
            }

            Current = quality;
            Changed?.Invoke(quality);
        }
    }
}
