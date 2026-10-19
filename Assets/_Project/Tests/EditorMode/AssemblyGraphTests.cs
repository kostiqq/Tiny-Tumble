using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace TinyTumble.Tests
{
    /// <summary>
    /// Проверяет граф сборок из docs/assembly-definitions.md: ссылки на наши сборки идут только вниз.
    /// </summary>
    public sealed class AssemblyGraphTests
    {
        private const string Prefix = "TinyTumble.";
        private const string EditorSuffix = ".Editor";

        [Serializable]
        private sealed class AsmdefData
        {
            public string name;
            public string rootNamespace;
            public string[] references;
            public string[] includePlatforms;
            public bool autoReferenced = true;
        }

        private static readonly string[] s_runtime =
        {
            "TinyTumble.Core",
            "TinyTumble.Infrastructure",
            "TinyTumble.Networking",
            "TinyTumble.Gameplay",
            "TinyTumble.AI",
            "TinyTumble.UI",
            "TinyTumble.Bootstrap",
        };

        // Разрешённые ссылки на наши сборки. Сводная таблица из assembly-definitions.md.
        private static readonly Dictionary<string, string[]> s_allowed = new Dictionary<string, string[]>
        {
            ["TinyTumble.Core"] = new string[0],
            ["TinyTumble.Infrastructure"] = new[] { "TinyTumble.Core" },
            ["TinyTumble.Networking"] = new[] { "TinyTumble.Core", "TinyTumble.Infrastructure" },
            ["TinyTumble.Gameplay"] = new[] { "TinyTumble.Core", "TinyTumble.Infrastructure", "TinyTumble.Networking" },
            ["TinyTumble.AI"] = new[] { "TinyTumble.Core", "TinyTumble.Gameplay" },
            ["TinyTumble.UI"] = new[] { "TinyTumble.Core", "TinyTumble.Infrastructure", "TinyTumble.Gameplay" },
            ["TinyTumble.Bootstrap"] = new[]
            {
                "TinyTumble.Core", "TinyTumble.Infrastructure", "TinyTumble.Networking",
                "TinyTumble.Gameplay", "TinyTumble.AI", "TinyTumble.UI",
            },
            ["TinyTumble.Editor"] = new[]
            {
                "TinyTumble.Core", "TinyTumble.Infrastructure", "TinyTumble.Networking",
                "TinyTumble.Gameplay", "TinyTumble.AI", "TinyTumble.UI",
            },
            ["TinyTumble.Tests.EditMode"] = new[]
            {
                "TinyTumble.Core", "TinyTumble.Infrastructure", "TinyTumble.Gameplay", "TinyTumble.AI",
            },
            ["TinyTumble.Tests.PlayMode"] = s_runtime,
        };

        private static Dictionary<string, AsmdefData> LoadAssemblies()
        {
            string root = Path.Combine(Application.dataPath, "_Project");
            var result = new Dictionary<string, AsmdefData>();
            foreach (string path in Directory.GetFiles(root, "*.asmdef", SearchOption.AllDirectories))
            {
                var data = JsonUtility.FromJson<AsmdefData>(File.ReadAllText(path));
                Assert.IsFalse(result.ContainsKey(data.name), $"Duplicate assembly name {data.name} ({path})");
                result.Add(data.name, data);
            }

            return result;
        }

        private static bool TryGetAllowed(string assembly, out HashSet<string> allowed)
        {
            if (s_allowed.TryGetValue(assembly, out string[] list))
            {
                allowed = new HashSet<string>(list);
                return true;
            }

            // Редакторная сборка слоя (TinyTumble.Gameplay.Editor): вниз как её слой плюс сам слой.
            if (assembly.EndsWith(EditorSuffix, StringComparison.Ordinal))
            {
                string layer = assembly.Substring(0, assembly.Length - EditorSuffix.Length);
                if (s_allowed.TryGetValue(layer, out list))
                {
                    allowed = new HashSet<string>(list) { layer };
                    return true;
                }
            }

            allowed = null;
            return false;
        }

        [Test]
        public void AllRuntimeAssembliesExist()
        {
            Dictionary<string, AsmdefData> assemblies = LoadAssemblies();
            foreach (string name in s_runtime)
            {
                Assert.IsTrue(assemblies.ContainsKey(name), $"Missing assembly {name}");
            }

            Assert.IsTrue(assemblies.ContainsKey("TinyTumble.Editor"));
            Assert.IsTrue(assemblies.ContainsKey("TinyTumble.Tests.EditMode"));
            Assert.IsTrue(assemblies.ContainsKey("TinyTumble.Tests.PlayMode"));
        }

        [Test]
        public void ReferencesGoOnlyDown()
        {
            var errors = new List<string>();
            foreach (KeyValuePair<string, AsmdefData> pair in LoadAssemblies())
            {
                if (!TryGetAllowed(pair.Key, out HashSet<string> allowed))
                {
                    errors.Add($"{pair.Key}: assembly is not described in assembly-definitions.md");
                    continue;
                }

                foreach (string reference in pair.Value.references ?? new string[0])
                {
                    if (reference.StartsWith(Prefix, StringComparison.Ordinal) && !allowed.Contains(reference))
                    {
                        errors.Add($"{pair.Key} -> {reference} is not allowed");
                    }
                }
            }

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void NobodyReferencesEditorOrTestAssemblies()
        {
            var errors = new List<string>();
            foreach (KeyValuePair<string, AsmdefData> pair in LoadAssemblies())
            {
                foreach (string reference in pair.Value.references ?? new string[0])
                {
                    if (reference.StartsWith(Prefix, StringComparison.Ordinal)
                        && (reference.EndsWith(EditorSuffix, StringComparison.Ordinal) || reference.StartsWith(Prefix + "Tests", StringComparison.Ordinal)))
                    {
                        errors.Add($"{pair.Key} -> {reference}");
                    }
                }
            }

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void OurAssembliesAreNotAutoReferenced()
        {
            foreach (KeyValuePair<string, AsmdefData> pair in LoadAssemblies())
            {
                Assert.IsFalse(pair.Value.autoReferenced, $"{pair.Key} must have Auto Referenced = false");
                StringAssert.StartsWith(Prefix, pair.Value.rootNamespace ?? string.Empty, $"{pair.Key} root namespace");
            }
        }

        [Test]
        public void EditorAssembliesAreEditorOnly()
        {
            foreach (KeyValuePair<string, AsmdefData> pair in LoadAssemblies())
            {
                bool editorOnly = pair.Key.EndsWith(EditorSuffix, StringComparison.Ordinal) || pair.Key == "TinyTumble.Tests.EditMode";
                if (editorOnly)
                {
                    CollectionAssert.AreEqual(new[] { "Editor" }, pair.Value.includePlatforms, pair.Key);
                }
            }
        }
    }
}
