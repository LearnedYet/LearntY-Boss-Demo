using AdaptiveBossArena.Editor.Environment;
using NUnit.Framework;
using UnityEngine;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 固定生成纹理用于遮罩的边缘渐变规则。
    /// </summary>
    /// <remarks>
    /// <c>Mathf.SmoothStep(a, b, t)</c> 名称看起来像 GLSL 的 <c>smoothstep(edge0, edge1, x)</c> ，
    /// 实际不同：返回值位于 <c>a</c> 和 <c>b</c>之间。用作遮罩永远不到零或一，
    /// 首版屋顶 Cookie 因此把全场阳光压至约五分之一。
    /// </remarks>
    [TestFixture]
    public sealed class EdgeRampTests
    {
        [Test]
        public void TheRampIsAMaskFromZeroToOneAcrossItsEdges()
        {
            Assert.AreEqual(0f, CathedralBuilder.Edge(0.6f, 0.7f, 0.5f), 0.0001f);
            Assert.AreEqual(1f, CathedralBuilder.Edge(0.6f, 0.7f, 0.8f), 0.0001f);
            Assert.AreEqual(0.5f, CathedralBuilder.Edge(0.6f, 0.7f, 0.65f), 0.0001f);
        }

        [Test]
        public void MathfSmoothStepIsNotTheSameThing()
        {
            // 保留以记录陷阱：把边缘作为参数，它返回边缘之间的值。
            float misused = Mathf.SmoothStep(0.6f, 0.7f, 0.5f);

            Assert.AreEqual(0.65f, misused, 0.0001f);
            Assert.AreNotEqual(CathedralBuilder.Edge(0.6f, 0.7f, 0.5f), misused);
        }
    }
}
