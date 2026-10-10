namespace AdaptiveBossArena.Combat
{
    /// <summary>
    /// 交战者被打断的原因。
    /// </summary>
    /// <remarks>
    /// 随时长一起传递，因为硬直状态会宣告自己；宣告错误比不宣告更糟。
    /// 姿态崩溃会触发游戏最响亮的爆发与提示，若招架后仰也触发它，就会误告玩家防御已经崩溃。
    /// 按严重程度排序，使已有硬直期间的新请求可以合并，而非覆盖正在播放的内容。
    /// </remarks>
    public enum StaggerReason
    {
        /// <summary>力量足以打断动作的普通命中。</summary>
        Hit = 0,

        /// <summary>挥击被精准接下并拒绝，使攻击者后仰。</summary>
        Parried = 1,

        /// <summary>姿态池耗尽，防御完全崩溃。</summary>
        PoiseBreak = 2
    }
}
