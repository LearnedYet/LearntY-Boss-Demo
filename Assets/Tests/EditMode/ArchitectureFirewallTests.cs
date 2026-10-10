using System.Collections.Generic;
using AdaptiveBossArena.Editor;
using NUnit.Framework;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 保护项目核心约束：首领不能读取玩家输入。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是项目最重要且最不容易因普通原因失败的测试。有人为消除棘手编译错误新增程序集引用时，
    /// 它会失败，避免首领悄然从学习者变成窥探者。
    /// </para>
    /// <para>
    /// 失败时不应放宽测试，而应把需要的玩家状态移到以下接口后面：
    /// <see cref="AdaptiveBossArena.Core.Perception.IObservablePlayer"/>，
    /// 并确认人类对手确实能够感知这种状态。
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class ArchitectureFirewallTests
    {
        [Test]
        public void AiAndLearningAssemblies_CannotReachPlayerInput()
        {
            IReadOnlyList<string> violations = ArchitectureValidator.FindViolations();

            Assert.IsEmpty(
                violations,
                "The architecture firewall has been breached:\n" + string.Join("\n", violations));
        }
    }
}
