using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DuneVector.Editor
{
    public static class DuneVectorTrainingBuild
    {
        private const string ScenePath = "Assets/DuneVector/Scenes/DuneVector.unity";
        private const string OutputPath = "Build/Training/DuneVectorTraining.exe";

        [MenuItem("Dune Vector/Training/Build Headless Training Player")]
        public static void BuildWindowsHeadless()
        {
            string outputDirectory = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                // The stripped runtime and -nographics launch flags provide the
                // headless training contract without requiring Unity's optional
                // Windows Dedicated Server editor module.
                subtarget = (int)StandaloneBuildSubtarget.Player,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Dune Vector training build failed: {report.summary.result} " +
                    $"({report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings).");
            }

            Debug.Log($"Built stripped headless training player at {Path.GetFullPath(OutputPath)}");
        }
    }
}
