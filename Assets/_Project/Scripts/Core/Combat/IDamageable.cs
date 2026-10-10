namespace AdaptiveBossArena.Core.Combat
{
    /// <summary>
    /// 能够接收攻击的对象。
    /// </summary>
    /// <remarks>
    /// 整个伤害链路仅使用此接口，因此对攻击者而言，玩家、Boss 和未来的可破坏场景物体可以互换。
    /// 实现自行决定是否命中，无敌帧和完美闪避也在此裁决。
    /// </remarks>
    public interface IDamageable
    {
        /// <summary>目标所属阵营。同阵营攻击会被拒绝。</summary>
        CombatantTeam Team { get; }

        /// <summary>目标被击败后为 false。</summary>
        bool IsAlive { get; }

        /// <summary>
        /// 向此目标提交攻击，并报告处理结果。
        /// </summary>
        /// <param name="damage">传入的攻击。通过引用传递以避免复制。</param>
        /// <returns>
        /// 处理结果。调用者不能假定伤害已造成：目标可能无敌、完成完美闪避，或已经死亡。
        /// </returns>
        DamageResult TakeDamage(in DamageInfo damage);
    }

    /// <summary>描述生命值或体力等可消耗资源池的变化。</summary>
    public readonly struct ResourceChangedArgs
    {
        /// <summary>变化前的值。</summary>
        public float Previous { get; init; }

        /// <summary>变化后的值。</summary>
        public float Current { get; init; }

        /// <summary>资源池可容纳的最大值。</summary>
        public float Maximum { get; init; }

        /// <summary>当前值占最大值的比例，限制在零到一之间。</summary>
        public float Normalized => Maximum <= 0f ? 0f : UnityEngine.Mathf.Clamp01(Current / Maximum);

        /// <summary>带符号的变化量。资源减少时为负。</summary>
        public float Delta => Current - Previous;

        /// <summary>创建变化描述。</summary>
        /// <param name="previous">变化前的值。</param>
        /// <param name="current">变化后的值。</param>
        /// <param name="maximum">资源池容量。</param>
        /// <returns>已填充的描述。</returns>
        public static ResourceChangedArgs Create(float previous, float current, float maximum) =>
            new ResourceChangedArgs { Previous = previous, Current = current, Maximum = maximum };
    }

    /// <summary>描述战斗参与者被击败的事件。</summary>
    public readonly struct DeathArgs
    {
        /// <summary>被击败者所属的阵营。</summary>
        public CombatantTeam Team { get; init; }

        /// <summary>致命攻击来源的实例标识。</summary>
        public int KillerInstanceId { get; init; }

        /// <summary>造成死亡的攻击。</summary>
        public DamageInfo FinalBlow { get; init; }
    }
}
