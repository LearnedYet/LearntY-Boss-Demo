using System;

namespace AdaptiveBossArena.Core.Combat
{
    /// <summary>
    /// 带变化通知的可消耗资源池。
    /// </summary>
    /// <remarks>
    /// 生命、体力和架势结构相同，因此共享同一契约。UI 条绑定此接口，无需修改就能用于玩家、
    /// Boss 和以后新增的对象。
    /// </remarks>
    public interface IResourcePool
    {
        /// <summary>当前持有的数量。</summary>
        float Current { get; }

        /// <summary>资源池容量。</summary>
        float Maximum { get; }

        /// <summary>当前数量占容量的比例，范围零到一。</summary>
        float Normalized { get; }

        /// <summary>持有数量变化时触发。</summary>
        event Action<ResourceChangedArgs> Changed;
    }

    /// <summary>战斗参与者的生命值。</summary>
    public interface IHealth : IResourcePool
    {
        /// <summary>当前生命值大于零时为 true。</summary>
        bool IsAlive { get; }

        /// <summary>生命值降为零时仅触发一次。</summary>
        event Action<DeathArgs> Died;

        /// <summary>恢复生命值，不超过最大值。</summary>
        /// <param name="amount">要恢复的生命值。非正值会被忽略。</param>
        /// <returns>范围限制后实际恢复的生命值。</returns>
        float Heal(float amount);
    }

    /// <summary>
    /// 限制玩家闪避和重击的体力。
    /// </summary>
    /// <remarks>
    /// 刻意不包含在 <see cref="AdaptiveBossArena.Core.Perception.IObservablePlayer"/>中。
    /// 人类对手无法看到准确体力数值，因此 Boss 也不能；
    /// 它必须根据未能使出的闪避等可观察行为推断体力耗尽。
    /// </remarks>
    public interface IStaminaPool : IResourcePool
    {
        /// <summary>资源池当前数量至少达到请求数量时为 true。</summary>
        /// <param name="amount">要检查的数量。</param>
        /// <returns>消耗能够成功时为 true。</returns>
        bool CanSpend(float amount);

        /// <summary>体力足够时消耗体力。</summary>
        /// <param name="amount">要消耗的数量。</param>
        /// <returns>消耗成功且资源池减少时为 true。</returns>
        bool TrySpend(float amount);

        /// <summary>恢复体力，不超过最大值。</summary>
        /// <param name="amount">要恢复的数量。</param>
        void Restore(float amount);
    }

    /// <summary>
    /// 架势：决定攻击是否使目标硬直的隐藏资源池。
    /// </summary>
    /// <remarks>
    /// 拥有者未被攻击时架势会恢复，因此零散擦伤不会造成硬直，而连续投入的连招可以。
    /// 这使与 Boss对攻成为需要判断的选择，而非单纯反射动作。
    /// </remarks>
    public interface IPoise : IResourcePool
    {
        /// <summary>拥有者处于架势崩溃状态且无法行动时为 true。</summary>
        bool IsBroken { get; }

        /// <summary>累积架势伤害击破拥有者站姿时触发。</summary>
        event Action Broken;

        /// <summary>施加架势伤害。</summary>
        /// <param name="amount">要施加的架势伤害。</param>
        /// <returns>此次处理击破拥有者架势时为 true。</returns>
        bool ApplyPoiseDamage(float amount);
    }
}
