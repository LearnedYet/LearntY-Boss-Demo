using UnityEngine;

namespace AdaptiveBossArena.Combat.Vitals
{
    /// <summary>
    /// 根据姿态完整程度，决定战斗参与者抵消多少推力。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 过去首领完全无视击退，看起来像墙而不是身体：游戏中最重的打击与最轻的打击推动距离完全相同。
    /// 承受全部推力则是相反的错误——两米半的巨兽会像骑士一样滑行。
    /// </para>
    /// <para>
    /// 将抗性绑定到姿态，让玩家通过努力克服重量。姿态完整的首领吸收大部分打击；
    /// 姿态被玩家逐渐削弱时会退让；破防时则被抛开。姿态条显示的同一个数值决定命中能否推动它，
    /// 因此反馈和机制不会互相矛盾。
    /// </para>
    /// </remarks>
    public static class KnockbackResistance
    {
        /// <summary>仍然生效的推力比例，从零（完全无效）到一（全部生效）。</summary>
        /// <param name="poiseFraction">姿态完整程度，从零到一。</param>
        /// <param name="isBroken">姿态当前是否被打破。</param>
        /// <param name="resistanceAtFullPoise">姿态完整时抵消的比例。</param>
        /// <param name="resistanceWhenBroken">破防时抵消的比例。</param>
        /// <returns>应用到击退速度的倍率。</returns>
        public static float ShoveMultiplier(
            float poiseFraction,
            bool isBroken,
            float resistanceAtFullPoise,
            float resistanceWhenBroken)
        {
            float resistance = isBroken
                ? resistanceWhenBroken
                : Mathf.Lerp(resistanceWhenBroken, resistanceAtFullPoise, Mathf.Clamp01(poiseFraction));

            return 1f - Mathf.Clamp01(resistance);
        }
    }
}
