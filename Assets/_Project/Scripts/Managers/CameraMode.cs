namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 相机可以采用的观察视角。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 需要明确这种取舍，因为它不只是审美差异。此遭遇战围绕俯视视角设计：攻击预警平铺在地面，
    /// 首领的距离控制要求玩家一眼判断间距，而闪避预测适应只有在玩家能够看见来袭挥击时才显得公平。
    /// </para>
    /// <para>
    /// 相机越低，这些前提越弱。第三人称仍然可读，而且可能更有戏剧性。第一人称会直接破坏其中多项：
    /// 地面预警大多位于视野外，背后的攻击也无法判断，因此这里只将它作为可尝试的选项，
    /// 而非正式支持的战斗方式。
    /// </para>
    /// </remarks>
    public enum CameraMode
    {
        /// <summary>倾斜俯视视角，遭遇战围绕此模式设计。</summary>
        TopDown = 0,

        /// <summary>越肩视角，更近且更有戏剧性。</summary>
        ThirdPerson = 1,

        /// <summary>角色头部高度的平视视角。</summary>
        FirstPerson = 2
    }
}
