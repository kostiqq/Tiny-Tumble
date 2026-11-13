using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TinyTumble.Bootstrap.Editor
{
    /// <summary>
    /// Development-сборка Windows для проверки сети двумя экземплярами (ручной или автотест -tt-autotest).
    /// Меню Tiny Tumble → Build → Windows Development, или batchmode
    /// -executeMethod TinyTumble.Bootstrap.Editor.DevBuild.BuildWindowsBatch [-tt-build-path Builds/Dev]
    /// </summary>
    public static class DevBuild
    {
        private const string DefaultOutput = "Builds/Dev";
        private const string ExecutableName = "TinyTumble.exe";
        private const string OutputKey = "-tt-build-path";

        [MenuItem("Tiny Tumble/Build/Windows Development")]
        public static void BuildWindows()
        {
            Build(DefaultOutput);
        }

        public static void BuildWindowsBatch()
        {
            try
            {
                bool ok = Build(GetArgument(OutputKey) ?? DefaultOutput);
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static bool Build(string outputFolder)
        {
            // Контент Addressables (арены, сетевые префабы) собираем явно, не полагаясь на настройку «собирать вместе с плеером».
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult content);
            if (!string.IsNullOrEmpty(content.Error))
            {
                Debug.LogError($"[Build] Addressables build failed: {content.Error}");
                return false;
            }

            var options = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                locationPathName = Path.Combine(outputFolder, ExecutableName),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            bool succeeded = report.summary.result == BuildResult.Succeeded;
            Debug.Log($"[Build] {report.summary.result}: {options.locationPathName}, {report.summary.totalErrors} errors.");
            return succeeded;
        }

        private static string[] GetEnabledScenes()
        {
            EditorBuildSettingsScene[] all = EditorBuildSettings.scenes;
            int count = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].enabled)
                {
                    count++;
                }
            }

            var result = new string[count];
            int index = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].enabled)
                {
                    result[index++] = all[i].path;
                }
            }

            return result;
        }

        private static string GetArgument(string key)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == key)
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
