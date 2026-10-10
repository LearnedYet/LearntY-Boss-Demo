using System;

namespace AdaptiveBossArena.Combat
{
    /// <summary>
    /// 攻击用于打击的身体部位。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 采用标记集合，因为某些攻击用多个部位命中：抓取双手合拢，冲锋同时用刀刃与身体撞击。
    /// </para>
    /// <para>
    /// <see cref="None"/> 让攻击采用编写的体积，即攻击者周围的弧、球或盒。
    /// 适合任何在地面绘制范围的攻击，例如砸击圆环和冲击波，也作为无骨架、
    /// 没有肢体可用于打击的身体的回退方式。
    /// </para>
    /// </remarks>
    [Flags]
    public enum StrikerParts
    {
        /// <summary>不使用部位，以编写的攻击体积判定。</summary>
        None = 0,

        /// <summary>右手武器，从根部到尖端。</summary>
        Weapon = 1,

        /// <summary>右拳。</summary>
        RightHand = 2,

        /// <summary>左拳。</summary>
        LeftHand = 4,

        /// <summary>右脚，用于踢击。</summary>
        RightFoot = 8,

        /// <summary>整个身体，用于以重量落地的跳跃或冲锋。</summary>
        Body = 16
    }
}
