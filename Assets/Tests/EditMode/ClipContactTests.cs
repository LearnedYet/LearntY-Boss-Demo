using AdaptiveBossArena.Combat.Feel;
using NUnit.Framework;
using UnityEditor;

namespace AdaptiveBossArena.Tests.EditMode
{
    /// <summary>
    /// 测试如何从持武器手运动找出攻击片段接触时刻。
    /// </summary>
    /// <remarks>
    /// 原先假定所有片段在 45% 处接触。Mixamo 从一秒斩击到两秒跃击差异大，
    /// 统一猜值让多数刀刃弧线早于或晚于命中。挥击打击处最快，因此测量速度峰值。
    /// </remarks>
    [TestFixture]
    public sealed class ClipContactTests
    {
        [Test]
        public void ContactIsWhereTheHandMovesFastest()
        {
            float[] speeds = { 0.1f, 0.2f, 0.5f, 2.4f, 1.1f, 0.3f, 0.2f, 0.1f, 0.1f };

            Assert.AreEqual(3f / 8f, ClipContact.PeakFraction(speeds), 0.0001f);
        }

        [Test]
        public void AFlourishAtTheVeryStartOrEndIsNotTakenForTheStrike()
        {
            // 片段最后快速收手回防，不能被误认作结尾打击。
            float[] speeds = { 3f, 0.4f, 0.9f, 1.2f, 0.8f, 0.3f, 0.2f, 0.4f, 5f };

            float contact = ClipContact.PeakFraction(speeds);

            Assert.GreaterOrEqual(contact, ClipContact.EarliestContact);
            Assert.LessOrEqual(contact, ClipContact.LatestContact);
            Assert.AreEqual(3f / 8f, contact, 0.0001f);
        }

        [Test]
        public void TooFewSamplesFallBackToTheDefault()
        {
            Assert.AreEqual(ClipContact.DefaultContact, ClipContact.PeakFraction(new[] { 1f, 2f }), 0.0001f);
            Assert.AreEqual(ClipContact.DefaultContact, ClipContact.PeakFraction(null), 0.0001f);
        }

        [TestCase("DefaultPlayerAnimation")]
        [TestCase("DefaultBossAnimation")]
        public void EveryBoundAttackStrikesAtItsOwnMeasuredMoment(string configName)
        {
            var config = AssetDatabase.LoadAssetAtPath<CharacterAnimationConfig>(
                "Assets/_Project/ScriptableObjects/Config/" + configName + ".asset");
            Assume.That(config != null && config.RigPrefab != null, "The character is not rigged.");

            var serialized = new SerializedObject(config);
            SerializedProperty bindings = serialized.FindProperty("_attackClips");
            Assert.Greater(bindings.arraySize, 0, configName + " binds no attacks.");

            for (int i = 0; i < bindings.arraySize; i++)
            {
                SerializedProperty binding = bindings.GetArrayElementAtIndex(i);
                float contact = binding.FindPropertyRelative("_contactFraction").floatValue;
                string state = binding.FindPropertyRelative("_state").stringValue;

                Assert.That(contact, Is.InRange(ClipContact.EarliestContact, ClipContact.LatestContact),
                    $"{configName}: the {state} clip has no measured contact ({contact:F2}).");
            }
        }
    }
}
