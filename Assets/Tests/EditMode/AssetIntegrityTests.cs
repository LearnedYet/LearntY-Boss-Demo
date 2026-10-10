using System.Collections.Generic;
using AdaptiveBossArena.Editor;
using NUnit.Framework;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 断言发布配置资产的引用确实可解析。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 源于其他检查均遗漏的故障：重建攻击资产使旧标识符失效，双方攻击列表全为null。编译、测试、场景构建、
    /// 启动都成功，但双方无法攻击。
    /// </para>
    /// <para>
    /// 需要固化的教训是：数据坏掉的项目从代码看仍可健康。资产完整性必须像行为一样明确断言。
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class AssetIntegrityTests
    {
        [Test]
        public void EveryConfigurationAssetResolvesItsReferences()
        {
            IReadOnlyList<string> problems = AssetIntegrityValidator.FindBrokenReferences();

            Assert.IsEmpty(
                problems,
                "Configuration assets contain unresolved references. The game will run but be " +
                "unplayable:\n" + string.Join("\n", problems));
        }
    }
}
