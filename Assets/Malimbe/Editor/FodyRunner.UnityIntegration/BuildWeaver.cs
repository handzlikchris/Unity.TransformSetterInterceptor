using System.Diagnostics;

namespace Malimbe.FodyRunner.UnityIntegration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEditor;
    using UnityEditor.Build;
    using UnityEditor.Build.Reporting;
    using UnityEngine;

    // ReSharper disable once UnusedMember.Global
    internal sealed class BuildWeaver : IPostprocessBuildWithReport
    {
        public int callbackOrder =>
            0;

        // ReSharper disable once AnnotateNotNullParameter
        public void OnPostprocessBuild(BuildReport buildReport)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            
            const string managedLibraryRoleName = "ManagedLibrary";
            IReadOnlyCollection<string> managedLibraryFilePaths = buildReport.files
                .Where(file => string.Equals(file.role, managedLibraryRoleName, StringComparison.OrdinalIgnoreCase))
                .Select(file => file.path)
                .ToList();
            if (managedLibraryFilePaths.Count == 0)
            {
                Debug.LogWarning(
                    $"The build didn't create any files of role '{managedLibraryRoleName}'. No weaving will be done.");
                return;
            }

            IEnumerable<string> dependentManagedLibraryFilePaths = buildReport.files.Where(
                    file => string.Equals(file.role, "DependentManagedLibrary", StringComparison.OrdinalIgnoreCase))
                .Select(file => file.path);
            IEnumerable<string> managedEngineApiFilePaths = buildReport.files
                .Where(file => string.Equals(file.role, "ManagedEngineAPI", StringComparison.OrdinalIgnoreCase))
                .Select(file => file.path);
            IReadOnlyCollection<string> potentialReferences = managedLibraryFilePaths
                .Concat(dependentManagedLibraryFilePaths)
                .Concat(managedEngineApiFilePaths)
                .ToList();
            List<string> scriptingDefineSymbols = PlayerSettings
                .GetScriptingDefineSymbolsForGroup(buildReport.summary.platformGroup)
                .Split(
                    new[]
                    {
                        ';'
                    },
                    StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            bool isDebugBuild = buildReport.summary.options.HasFlag(BuildOptions.Development);

            IReadOnlyCollection<string> searchPaths = WeaverPathsHelper.GetSearchPaths().ToList();
            Runner runner = new Runner(new Logger());
            runner.Configure(searchPaths, searchPaths);

            try
            {
                foreach (string managedLibraryFilePath in managedLibraryFilePaths)
                {
                    runner.RunAsync(
                            managedLibraryFilePath,
                            potentialReferences.Except(
                                new[]
                                {
                                    managedLibraryFilePath
                                }),
                            scriptingDefineSymbols,
                            isDebugBuild)
                        .GetAwaiter()
                        .GetResult();
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            
            Debug.Log($"Weaved via: {nameof(OnPostprocessBuild)} done: {sw.ElapsedMilliseconds} ms");
        }
    }
}
