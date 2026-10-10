using System.Collections;
using System.Reflection;
using AdaptiveBossArena.AI;
using AdaptiveBossArena.Core.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AdaptiveBossArena.Tests.PlayMode
{
    /// <summary>
    /// 测试首领招架是带可惩罚尾段的窗口，而非无敌站姿。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 规则本身已在编辑模式下针对以下类型测试： <c>DefenceResolver</c>。编辑模式无法覆盖的是连接配置：
    /// 首领是否将自己的站姿计时和配置窗口交给解析器，从而使发布场景中的尾段确实可被惩罚。
    /// 此前站姿会拒绝半秒内所有攻击，玩家只能等它结束，无法击败它；
    /// 不存在投入该动作被证明是错误决策的时刻。
    /// </para>
    /// <para>
    /// 通过反射驱动站姿，而非等待首领自行选择。进入站姿依赖针对感知重击的种子概率，
    /// 正常诱导会使测试变成抛硬币；所测差异窗口只有十分之一秒，也不应依赖帧时机。
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class BossParryTests
    {
        private const int WarmUpFrames = 10;

        private BossController _boss;

        [UnitySetUp]
        public IEnumerator LoadTheArena()
        {
            SceneManager.LoadScene("Arena", LoadSceneMode.Single);

            for (int i = 0; i < WarmUpFrames; i++)
            {
                yield return null;
            }

            _boss = Object.FindAnyObjectByType<BossController>();
        }

        /// <summary>从实例读取私有字段，不存在时使测试失败。</summary>
        private static object Field(object owner, string fieldName)
        {
            FieldInfo field = owner.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, $"{owner.GetType().Name} has no field '{fieldName}'.");

            return field.GetValue(owner);
        }

        /// <summary>首领调节参数资源，无需任何模块公开暴露。</summary>
        private BossConfig Config() => (BossConfig)Field(_boss, "_config");

        /// <summary>使首领进入招架站姿，并推进至指定时间。</summary>
        private void ForceStance(float secondsIntoStance)
        {
            var context = (BossContext)Field(_boss, "_context");
            context.IsParrying = true;

            object parryState = Field(_boss, "_parryState");

            // 具有私有 setter 的自动属性，因此编译器生成的后备字段是唯一入口。按名称而非搜索找到它，
            // 使重命名在此明确失败。
            FieldInfo timeInState = parryState.GetType().BaseType?.GetField(
                "<TimeInState>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(timeInState, "StateBase no longer stores TimeInState in a backing field.");
            timeInState.SetValue(parryState, secondsIntoStance);
        }

        /// <summary>普通玩家挥击，正是该站姿要应对的攻击。</summary>
        private static DamageInfo Swing() =>
            DamageInfo.Create(30f, DamageType.Heavy, CombatantTeam.Player, 1);

        [UnityTest]
        public IEnumerator AHitInsideTheWindowIsRefused()
        {
            Assert.IsNotNull(_boss, "No boss in the arena scene.");

            BossConfig config = Config();
            Assert.IsNotNull(config, "The boss has no config.");

            ForceStance(config.ParryWindowSeconds * 0.5f);

            Assert.AreEqual(
                DamageOutcome.Deflected, _boss.TakeDamage(Swing()).Outcome,
                "A hit met inside the parry window was not refused.");

            yield return null;
        }

        [UnityTest]
        public IEnumerator AHitInTheTailLandsInFull()
        {
            Assert.IsNotNull(_boss, "No boss in the arena scene.");

            BossConfig config = Config();
            float healthBefore = _boss.Health.Current;

            // 已过窗口，但仍投入站姿。这就是惩罚机会，也是诱导该站姿值得做的全部理由。
            ForceStance(config.ParryWindowSeconds + config.ParryTailSeconds * 0.5f);

            Assert.AreEqual(
                DamageOutcome.Applied, _boss.TakeDamage(Swing()).Outcome,
                "A hit landed in the parry's tail was still refused.");

            Assert.Less(_boss.Health.Current, healthBefore, "The punishing hit dealt no damage.");

            yield return null;
        }

        [UnityTest]
        public IEnumerator AnUnparryableExecutionIsNotRefusedEvenOnTheBeat()
        {
            Assert.IsNotNull(_boss, "No boss in the arena scene.");

            BossConfig config = Config();

            ForceStance(config.ParryWindowSeconds * 0.5f);

            DamageInfo execution = DamageInfo.Create(30f, DamageType.Heavy, CombatantTeam.Player, 1);
            execution = new DamageInfo
            {
                Amount = execution.Amount,
                Type = execution.Type,
                SourceTeam = execution.SourceTeam,
                SourceInstanceId = execution.SourceInstanceId,
                HitDirection = execution.HitDirection,
                Stagger = execution.Stagger,
                Unparryable = true
            };

            // 玩家通过破防赢得的惩罚机会。若用已经失败的相同站姿拒绝它，
            // 就会收回玩家已付出代价获得的奖励。
            Assert.AreEqual(
                DamageOutcome.Applied, _boss.TakeDamage(execution).Outcome,
                "The boss parried an attack marked unparryable.");

            yield return null;
        }
    }
}
