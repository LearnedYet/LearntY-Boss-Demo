using System.Collections.Generic;
using AdaptiveBossArena.AI;
using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Player;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 检查所有配置资产的引用是否确实可解析。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 此工具源于一次浪费完整试玩的问题：重新生成攻击资产时删除了它们的 <c>.meta</c> 文件，
    /// 新资产获得新标识符，而双方角色配置仍引用旧标识符，导致全部攻击引用变为 null。
    /// </para>
    /// <para>
    /// 没有任何报警：项目能编译、所有测试通过、场景可构建、游戏可运行，但首领攻击目录为空，只会不停绕圈；
    /// 玩家攻击按钮解析成 null，毫无作用。外观看起来像 AI 和视觉缺失问题，实际都不是。
    /// </para>
    /// <para>
    /// 配置资产中的 null 引用绝不可接受，因此明确报错而非仅警告。该检查是设置流程的一部分，
    /// 也由编辑模式测试断言。
    /// </para>
    /// </remarks>
    public static class AssetIntegrityValidator
    {
        /// <summary>运行检查并报告到控制台。</summary>
        [MenuItem(EditorMenus.Validate + "Check Asset References")]
        public static void ValidateFromMenu()
        {
            IReadOnlyList<string> problems = FindBrokenReferences();

            if (problems.Count == 0)
            {
                Debug.Log("[Adaptive Boss Arena] Asset references intact.");
                return;
            }

            foreach (string problem in problems)
            {
                Debug.LogError($"[Adaptive Boss Arena] Broken reference: {problem}");
            }
        }

        /// <summary>
        /// 查找游戏配置资产中所有无法解析的引用。
        /// </summary>
        /// <returns>人类可读说明；所有引用可解析时为空。</returns>
        public static IReadOnlyList<string> FindBrokenReferences()
        {
            var problems = new List<string>();

            ValidatePlayerConfigs(problems);
            ValidateBossConfigs(problems);

            return problems;
        }

        private static void ValidatePlayerConfigs(ICollection<string> problems)
        {
            foreach (PlayerConfig config in LoadAll<PlayerConfig>())
            {
                AttackDefinition[] chain = config.LightComboChain;

                if (chain == null || chain.Length == 0)
                {
                    problems.Add($"{config.name}: light combo chain is empty.");
                }
                else
                {
                    for (int i = 0; i < chain.Length; i++)
                    {
                        if (chain[i] == null)
                        {
                            problems.Add($"{config.name}: light combo chain entry {i} is unresolved.");
                        }
                    }
                }

                if (config.HeavyAttack == null)
                {
                    problems.Add($"{config.name}: heavy attack is unresolved.");
                }

                if (config.SpecialAttack == null)
                {
                    problems.Add($"{config.name}: special attack is unresolved.");
                }

                ValidateExecution(config, problems);
            }
        }

        /// <summary>
        /// 断言处决攻击存在且无法被弹反。
        /// </summary>
        /// <remarks>
        /// 两个条件都重要，且在编辑时都不明显。反击状态使用<c>ExecutionAttack</c>；
        /// 它会经武器反击回退到普通重攻击。因此未配置处决不会报错，而是悄然将惩罚降级为普通挥击，
        /// 首领仍能用架势防御。验证器正是用于发现这类问题：可以运行，外观看似正常，但机制悄然失效。
        /// </remarks>
        /// <param name="config">要检查的玩家配置。</param>
        /// <param name="problems">用于记录失败项的集合。</param>
        private static void ValidateExecution(PlayerConfig config, ICollection<string> problems)
        {
            if (config.ExecutionAttack == null)
            {
                problems.Add(
                    $"{config.name}: execution attack is unresolved, so a riposte falls back to " +
                    "the ordinary heavy and can be parried away.");

                return;
            }

            if (!config.ExecutionAttack.Unparryable)
            {
                problems.Add(
                    $"{config.name}: execution attack '{config.ExecutionAttack.name}' is " +
                    "parryable. A punish earned by breaking a guard must not be refused by it.");
            }
        }

        private static void ValidateBossConfigs(ICollection<string> problems)
        {
            foreach (BossConfig config in LoadAll<BossConfig>())
            {
                if (config.ReactionProfile == null)
                {
                    // 没有它，首领按设计会完全拒绝运行。
                    problems.Add($"{config.name}: reaction profile is unresolved.");
                }

                BossPhaseDefinition[] phases = config.Phases;

                if (phases == null || phases.Length == 0)
                {
                    problems.Add($"{config.name}: has no phases.");
                    continue;
                }

                for (int phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
                {
                    ValidatePhase(config.name, phaseIndex, phases[phaseIndex], problems);
                }

                CounterStrategyIntegrity(config, problems);
            }
        }

        private static void ValidatePhase(
            string configName,
            int phaseIndex,
            BossPhaseDefinition phase,
            ICollection<string> problems)
        {
            AttackDefinition[] attacks = phase.Attacks;

            if (attacks == null || attacks.Length == 0)
            {
                problems.Add(
                    $"{configName}: phase {phaseIndex} ('{phase.Name}') has no attacks, so the boss " +
                    "can never attack during it.");
                return;
            }

            for (int i = 0; i < attacks.Length; i++)
            {
                if (attacks[i] == null)
                {
                    problems.Add($"{configName}: phase {phaseIndex} attack {i} is unresolved.");
                }
            }
        }

        private static void CounterStrategyIntegrity(BossConfig config, ICollection<string> problems)
        {
            Learning.CounterStrategy[] strategies = config.CounterStrategies;

            if (strategies == null)
            {
                return;
            }

            for (int i = 0; i < strategies.Length; i++)
            {
                if (strategies[i] == null)
                {
                    problems.Add($"{config.name}: counter-strategy {i} is unresolved.");
                    continue;
                }

                // 玩家无法感知的策略与首领作弊没有区别，因此缺少提示是实际缺陷，而非装饰遗漏。
                if (string.IsNullOrWhiteSpace(strategies[i].TellMessage))
                {
                    problems.Add(
                        $"{strategies[i].name}: has no tell message, so its adaptation would be invisible.");
                }

                AdjustmentsAreConsumed(strategies[i], problems);
            }
        }

        /// <summary>
        /// 策略调整的每个参数必须被 AI 实际读取。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 这里记录了一个长期藏在全绿测试后的问题：两个参数<c>AttackDelay</c> 和
        /// <c>ComboExtensionChance</c> 由策略写入、每帧平滑变化并显示在调试面板上，
        /// 却没有任何状态读取。首领发出“It waits for your opening now.”提示后，行为却没有等待。
        /// </para>
        /// <para>
        /// 玩家无法感知的适应与作弊无异；如果适应已经<em>宣布</em> 却毫无作用，反而更糟，
        /// 因为提示承诺了战斗没有兑现的行为。其他检查发现不了：代码可编译、资产可解析、数值在变化。
        /// 因此这里明确列出被实际消费的参数并检查。
        /// </para>
        /// <para>
        /// 接入新参数时将其加入此列表。若参数已经列出但后来删除了消费者，检查不会发现，
        /// 因此列表只是最低要求而非充分证明。
        /// </para>
        /// </remarks>
        private static void AdjustmentsAreConsumed(
            Learning.CounterStrategy strategy, ICollection<string> problems)
        {
            if (strategy.Adjustments == null)
            {
                return;
            }

            foreach (Learning.TuningAdjustment adjustment in strategy.Adjustments)
            {
                if (!ConsumedTuningParameters.Contains(adjustment.Parameter))
                {
                    problems.Add(
                        $"{strategy.name}: adjusts {adjustment.Parameter}, which no AI code reads. " +
                        "Its tell would promise a behaviour change that never happens.");
                }
            }
        }

        /// <summary>
        /// AI 某部分确实会使用的调参项。
        /// </summary>
        /// <remarks>
        /// 每个条目都对应一次真实的 <c>Tuning.Get</c> 调用：
        /// 攻击节奏和连击长度位于<c>BossAttackState</c>，攻击权重位于 <c>BossAttackSelector</c>，
        /// 保持距离位于<c>BossContext</c>，招架位于 <c>BossController</c>，闪避预判位于攻击状态，
        /// 压迫行为位于 <c>BossPositioningStates</c>.
        /// </remarks>
        private static readonly HashSet<Learning.BossTuningParameter> ConsumedTuningParameters = new()
        {
            Learning.BossTuningParameter.AttackDelay,
            Learning.BossTuningParameter.PreferredRange,
            Learning.BossTuningParameter.ParryChance,
            Learning.BossTuningParameter.GapCloserWeight,
            Learning.BossTuningParameter.DodgePredictionStrength,
            Learning.BossTuningParameter.Aggression,
            Learning.BossTuningParameter.ComboExtensionChance,
            Learning.BossTuningParameter.FeintChance,
            Learning.BossTuningParameter.AreaDenialWeight,
            Learning.BossTuningParameter.UnblockableWeight
        };

        /// <summary>加载项目中某类型的全部资产。</summary>
        private static IEnumerable<TAsset> LoadAll<TAsset>() where TAsset : Object
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(TAsset).Name}"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<TAsset>(AssetDatabase.GUIDToAssetPath(guid));

                if (asset != null)
                {
                    yield return asset;
                }
            }
        }
    }
}
