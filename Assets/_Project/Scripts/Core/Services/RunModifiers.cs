using System;
using System.Collections.Generic;

namespace AdaptiveBossArena.Core.Services
{
    /// <summary>
    /// 战斗前选择的可选挑战规则，改变战斗配置而不改变机制。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 每个修正项都表示为现有系统已经遵守的<em>数值</em>：适应速度倍率、伤害倍率或一组标志，
    /// 绝不引入新规则。这与 Boss 自身适应遵循相同原则，使挑战仍易于理解：
    /// 快速学习模式下还是同一个 Boss，只是更快看穿你。
    /// </para>
    /// <para>
    /// 与 <see cref="SettingsData"/> 一样是普通可序列化记录，默认值使未启用修正的挑战就是普通战斗。
    /// 会话期间由 <see cref="RunSettings"/>保存，使标题界面的选择在加载竞技场后仍保留。
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class RunModifiers
    {
        /// <summary>写入此记录时使用的结构版本。</summary>
        public int SchemaVersion = CurrentSchemaVersion;

        /// <summary>当前代码写入和理解的版本。</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Boss 更快看穿玩家，使适应更早发生。</summary>
        public bool FastLearner;

        /// <summary>玩家无法治疗，因此每次受击损失都必须通过交锋弥补。</summary>
        public bool NoHealing;

        /// <summary>玩家承受更多伤害，更严厉地惩罚松懈防御。</summary>
        public bool FragilePlayer;

        /// <summary>训练模式：提示面板保持显示，玩家不会死亡。</summary>
        public bool TrainingMode;

        /// <summary>快速学习模式下 Boss 适应加快的倍率。</summary>
        private const float FastLearnerScale = 2f;

        /// <summary>脆弱玩家受到伤害的增强倍率。</summary>
        private const float FragileDamageMultiplier = 1.5f;

        /// <summary>Boss 适应速度倍率。</summary>
        public float AdaptationRateScale => FastLearner ? FastLearnerScale : 1f;

        /// <summary>玩家承受的伤害倍率。</summary>
        public float IncomingDamageMultiplier => FragilePlayer ? FragileDamageMultiplier : 1f;

        /// <summary>玩家是否可以治疗。</summary>
        public bool HealingEnabled => !NoHealing;

        /// <summary>任意修正项使挑战不同于普通战斗时为 true。</summary>
        public bool AnyActive => FastLearner || NoHealing || FragilePlayer || TrainingMode;

        /// <summary>
        /// 这些规则下的尝试是否可以写入永久纪录。
        /// </summary>
        /// <remarks>
        /// 训练模式让玩家无法死亡，因此这里的“胜利”不是真正胜利。若记录这种不会失败的挑战，
        /// 会永久写入最快时间和最少适应数，且无法撤回。其他三个修正都让战斗<em>更难</em>，
        /// 因此取胜至少与普通胜利等值，正常计入纪录。
        /// </remarks>
        public bool CountsTowardRecords => !TrainingMode;

        /// <summary>已启用修正项的短名称，用于战后报告。</summary>
        /// <returns>每个已启用修正对应一个标签，普通挑战为空。</returns>
        public IReadOnlyList<string> ActiveLabels()
        {
            var labels = new List<string>();

            if (FastLearner)
            {
                labels.Add("快速学习");
            }

            if (NoHealing)
            {
                labels.Add("禁止治疗");
            }

            if (FragilePlayer)
            {
                labels.Add("脆弱之躯");
            }

            if (TrainingMode)
            {
                labels.Add("训练模式");
            }

            return labels;
        }
    }

    /// <summary>
    /// 当前会话生效的修正项，从标题界面带入战斗。
    /// </summary>
    /// <remarks>
    /// 采用静态持有者，因为标题和竞技场是使用独立服务注册表的两个场景。静态值能跨场景加载存活，
    /// 场景引用则不能。只有标题界面写入，战斗组合根读取，
    /// 因此 <c>AI</c>或 <c>Learning</c> 都不会依赖它。
    /// </remarks>
    public static class RunSettings
    {
        /// <summary>本次会话选择的修正项，永不为 null。</summary>
        public static RunModifiers Current { get; set; } = new RunModifiers();
    }
}
