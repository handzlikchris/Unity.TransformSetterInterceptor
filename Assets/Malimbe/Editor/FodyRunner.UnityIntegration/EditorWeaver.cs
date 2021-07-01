using System.Diagnostics;

namespace Malimbe.FodyRunner.UnityIntegration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using JetBrains.Annotations;
    using UnityEditor;
    using UnityEditor.Compilation;
    using UnityEngine;

    internal static class EditorWeaver
    {
        [InitializeOnLoadMethod]
        private static void OnEditorInitialization()
        {
            CompilationPipeline.assemblyCompilationFinished += OnCompilationFinished;
            WeaveAllAssemblies();
        }

        private static void WeaveAllAssemblies()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            
            EditorApplication.LockReloadAssemblies();
            bool didChangeAnyAssembly = false;

            try
            {
                IReadOnlyCollection<string> searchPaths = WeaverPathsHelper.GetSearchPaths().ToList();
                Runner runner = new Runner(new Logger());
                runner.Configure(searchPaths, searchPaths);

                foreach (Assembly assembly in GetAllAssemblies())
                {
                    if (!WeaveAssembly(assembly, runner))
                    {
                        continue;
                    }

                    string sourceFilePath = assembly.sourceFiles.FirstOrDefault();
                    if (sourceFilePath == null)
                    {
                        continue;
                    }

                    AssetDatabase.ImportAsset(sourceFilePath, ImportAssetOptions.ForceUpdate);
                    didChangeAnyAssembly = true;
                }
            }
            finally
            {
                EditorApplication.UnlockReloadAssemblies();
                if (didChangeAnyAssembly)
                {
                    AssetDatabase.Refresh();
                }
                
                Debug.Log($"{nameof(WeaveAllAssemblies)} done: {sw.ElapsedMilliseconds} ms");
            }
        }

        private static void OnCompilationFinished(string path, CompilerMessage[] messages)
        {
            Assembly foundAssembly = GetAllAssemblies()
                .FirstOrDefault(assembly => string.Equals(assembly.outputPath, path, StringComparison.Ordinal));
            if (foundAssembly == null)
            {
                return;
            }

            IReadOnlyCollection<string> searchPaths = WeaverPathsHelper.GetSearchPaths().ToList();
            Runner runner = new Runner(new Logger());
            runner.Configure(searchPaths, searchPaths);

            WeaveAssembly(foundAssembly, runner);
        }

        [NotNull]
        private static IEnumerable<Assembly> GetAllAssemblies() =>
            CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                .GroupBy(assembly => assembly.outputPath)
                .Select(grouping => grouping.First());

        private static bool WeaveAssembly(Assembly assembly, Runner runner)
        {
            try
            {
                string assemblyPath = WeaverPathsHelper.AddProjectPathRootIfNeeded(assembly.outputPath);
                IEnumerable<string> references =
                    assembly.allReferences.Select(WeaverPathsHelper.AddProjectPathRootIfNeeded);
                return runner.RunAsync(assemblyPath, references, assembly.defines.ToList(), true)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }
    }
}
