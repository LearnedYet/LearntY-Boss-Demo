using System.Collections.Generic;
using System.Reflection;
using AdaptiveBossArena.Core.Combat;
using NUnit.Framework;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 断言复制命中描述不会悄然丢失字段。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="DamageInfo"/> 仅可初始化，C# 9 对结构体还没有 <c>with</c> ，
    /// 因此每次复制都在私有辅助方法手工写出。其文档要求新增字段别遗漏，但此前无人约束。
    /// 游戏每次命中到受击框前都经过<c>AtPoint</c> ，复制遗漏字段就会让它在所有地方悄然失效，
    /// 尽管仍可编译且声明处正确。
    /// </para>
    /// <para>
    /// 通过反射检查属性列表，而非逐字段编写，因此也覆盖未来字段。下一位新增字段的人不必记得此测试存在，
    /// 正是目的。
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class DamageInfoCopyTests
    {
        /// <summary>调用者允许修改的三个字段，排除检查。</summary>
        private static readonly HashSet<string> DeliberatelyVaried = new HashSet<string>
        {
            nameof(DamageInfo.Amount),
            nameof(DamageInfo.HitPoint),
            nameof(DamageInfo.HitDirection)
        };

        /// <summary>每个字段都偏离默认值的命中数据，便于发现丢失。</summary>
        private static DamageInfo Distinctive() => new DamageInfo
        {
            Amount = 37f,
            Type = DamageType.BossProjectile,
            SourceTeam = CombatantTeam.Boss,
            SourceInstanceId = 4242,
            HitPoint = new Vector3(1f, 2f, 3f),
            HitDirection = Vector3.left,
            KnockbackSpeed = 9f,
            PoiseDamage = 55f,
            Stagger = StaggerStrength.Break,
            Reaction = ImpactReaction.Launch,
            HitStopSeconds = 0.12f,
            IgnoresInvulnerability = true,
            Unblockable = true,
            Unparryable = true
        };

        private static void AssertCarriedOver(in DamageInfo source, in DamageInfo copy)
        {
            foreach (PropertyInfo property in typeof(DamageInfo).GetProperties(
                         BindingFlags.Instance | BindingFlags.Public))
            {
                if (DeliberatelyVaried.Contains(property.Name) || !property.CanRead)
                {
                    continue;
                }

                Assert.AreEqual(
                    property.GetValue(BoxOf(source)),
                    property.GetValue(BoxOf(copy)),
                    $"Copying a hit dropped '{property.Name}'. Add it to DamageInfo.Rebuild.");
            }
        }

        /// <summary>结构体仅装箱一次，让反射对每个属性读取同一实例。</summary>
        private static object BoxOf(in DamageInfo damage) => damage;

        [Test]
        public void AtPointKeepsEveryFieldItWasNotAskedToChange()
        {
            DamageInfo source = Distinctive();
            DamageInfo copy = source.AtPoint(new Vector3(9f, 9f, 9f), Vector3.up);

            AssertCarriedOver(source, copy);

            Assert.AreEqual(new Vector3(9f, 9f, 9f), copy.HitPoint);
            Assert.AreEqual(Vector3.up, copy.HitDirection);
            Assert.AreEqual(source.Amount, copy.Amount, 0.001f);
        }

        [Test]
        public void WithAmountKeepsEveryFieldItWasNotAskedToChange()
        {
            DamageInfo source = Distinctive();
            DamageInfo copy = source.WithAmount(96f);

            AssertCarriedOver(source, copy);

            Assert.AreEqual(96f, copy.Amount, 0.001f);
            Assert.AreEqual(source.HitPoint, copy.HitPoint);
            Assert.AreEqual(source.HitDirection, copy.HitDirection);
        }

        [Test]
        public void UnparryableSurvivesTheCopyEveryHitMakes()
        {
            // 单独命名，因为测试就是为此字段编写：执行器到每个受击框前都调用 AtPoint，
            // 处决若在此丢失不可招架标志，会在游戏中可被弹反，资产却看似正确。
            Assert.IsTrue(Distinctive().AtPoint(Vector3.zero, Vector3.forward).Unparryable);
        }
    }
}
