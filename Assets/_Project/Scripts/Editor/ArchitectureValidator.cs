using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 验证程序集依赖图是否仍能阻止首领作弊。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本项目的核心设计保证是首领无法读取玩家输入。这依靠程序集依赖图而不是约定来约束：
    /// AI 和 Learning 程序集不引用 Player程序集或 Input System，
    /// 因此在那里编写读取输入作弊的代码将无法通过编译。
    /// </para>
    /// <para>
    /// 只要有人为解决一个棘手的编译错误而新增引用，这项保证就会失效。此验证器会明确报告这种变更，
    /// 而不是让设计约束悄然瓦解。编辑模式测试会执行它，使检查可以在持续集成中运行，
    /// 而不依赖人工记得调用。
    /// </para>
    /// <para>
    /// 检查会沿引用关系递归追踪。通过中间程序集绕接的引用与直接引用同样构成违规。
    /// </para>
    /// </remarks>
    public static class ArchitectureValidator
    {
        /// <summary>必须始终无法直接观察玩家的程序集。</summary>
        private static readonly string[] RestrictedAssemblies =
        {
            "AdaptiveBossArena.AI",
            "AdaptiveBossArena.Learning"
        };

        /// <summary>受限程序集绝不能访问的程序集。</summary>
        private static readonly string[] ForbiddenAssemblies =
        {
            "AdaptiveBossArena.Player",
            "Unity.InputSystem"
        };

        /// <summary>执行检查并将结果报告到控制台。</summary>
        [MenuItem(EditorMenus.Validate + "Check Architecture Firewall")]
        public static void ValidateFromMenu()
        {
            IReadOnlyList<string> violations = FindViolations();

            if (violations.Count == 0)
            {
                Debug.Log(
                    "[Adaptive Boss Arena] Architecture firewall intact: the AI and Learning " +
                    "assemblies cannot reach player input.");
                return;
            }

            foreach (string violation in violations)
            {
                Debug.LogError($"[Adaptive Boss Arena] Architecture violation: {violation}");
            }
        }

        /// <summary>
        /// 查找受限程序集访问禁止程序集的所有依赖路径。
        /// </summary>
        /// <returns>每项违规的人类可读说明；依赖图符合约束时为空。</returns>
        public static IReadOnlyList<string> FindViolations()
        {
            Dictionary<string, List<string>> graph = BuildReferenceGraph();
            var violations = new List<string>();

            foreach (string restricted in RestrictedAssemblies)
            {
                if (!graph.ContainsKey(restricted))
                {
                    violations.Add(
                        $"Assembly '{restricted}' was not found. The firewall cannot be verified.");
                    continue;
                }

                foreach (string forbidden in ForbiddenAssemblies)
                {
                    List<string> path = FindPath(graph, restricted, forbidden);

                    if (path != null)
                    {
                        violations.Add(
                            $"'{restricted}' can reach '{forbidden}' via {string.Join(" -> ", path)}. " +
                            "This would let the boss observe player input directly, which the design forbids.");
                    }
                }
            }

            return violations;
        }

        /// <summary>读取项目中所有程序集定义，构建引用关系图。</summary>
        private static Dictionary<string, List<string>> BuildReferenceGraph()
        {
            var graph = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var guidToName = new Dictionary<string, string>(StringComparer.Ordinal);
            var parsed = new List<AssemblyDefinitionData>();

            foreach (string guid in AssetDatabase.FindAssets("t:AssemblyDefinitionAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(path);

                if (asset == null)
                {
                    continue;
                }

                var data = JsonUtility.FromJson<AssemblyDefinitionData>(asset.text);
                if (data == null || string.IsNullOrEmpty(data.name))
                {
                    continue;
                }

                parsed.Add(data);
                guidToName[guid] = data.name;
            }

            foreach (AssemblyDefinitionData data in parsed)
            {
                var references = new List<string>();

                foreach (string reference in data.references ?? Array.Empty<string>())
                {
                    references.Add(ResolveReference(reference, guidToName));
                }

                graph[data.name] = references;
            }

            return graph;
        }

        /// <summary>
        /// 将引用条目转换为程序集名称。
        /// </summary>
        /// <remarks>
        /// Unity 将引用保存为普通名称；启用“Use GUIDs”时则保存为<c>GUID:</c>前缀的标识符。
        /// 必须同时识别两种形式，否则项目仅切换表示方式就会被验证器悄然判定为通过。
        /// </remarks>
        private static string ResolveReference(string reference, IReadOnlyDictionary<string, string> guidToName)
        {
            const string guidPrefix = "GUID:";

            if (!reference.StartsWith(guidPrefix, StringComparison.Ordinal))
            {
                return reference;
            }

            string guid = reference.Substring(guidPrefix.Length);
            return guidToName.TryGetValue(guid, out string name) ? name : reference;
        }

        /// <summary>通过广度优先搜索查找两个程序集之间的引用路径。</summary>
        /// <returns>包含两个端点的路径；不存在路径时返回 null。</returns>
        private static List<string> FindPath(
            IReadOnlyDictionary<string, List<string>> graph,
            string from,
            string to)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { from };
            var previous = new Dictionary<string, string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            queue.Enqueue(from);

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();

                if (!graph.TryGetValue(current, out List<string> references))
                {
                    continue;
                }

                foreach (string reference in references)
                {
                    if (!visited.Add(reference))
                    {
                        continue;
                    }

                    previous[reference] = current;

                    if (reference == to)
                    {
                        return ReconstructPath(previous, from, to);
                    }

                    queue.Enqueue(reference);
                }
            }

            return null;
        }

        /// <summary>沿前驱映射反向回溯，生成可读的路径。</summary>
        private static List<string> ReconstructPath(
            IReadOnlyDictionary<string, string> previous,
            string from,
            string to)
        {
            var path = new List<string> { to };
            string current = to;

            while (current != from && previous.TryGetValue(current, out string parent))
            {
                path.Insert(0, parent);
                current = parent;
            }

            return path;
        }

        /// <summary>用于 JSON 反序列化的程序集定义文件最小数据结构。</summary>
        [Serializable]
        private sealed class AssemblyDefinitionData
        {
            /// <summary>程序集名称。</summary>
            public string name = string.Empty;

            /// <summary>按名称或 GUID 指定的程序集引用。</summary>
            public string[] references = Array.Empty<string>();
        }
    }
}
