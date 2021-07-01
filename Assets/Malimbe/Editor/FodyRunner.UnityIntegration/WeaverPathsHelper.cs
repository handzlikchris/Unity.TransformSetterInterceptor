namespace Malimbe.FodyRunner.UnityIntegration
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using JetBrains.Annotations;
    using UnityEditor;
    using UnityEditor.PackageManager;
    using UnityEditor.PackageManager.Requests;
    using UnityEngine;

    [InitializeOnLoad]
    internal static class WeaverPathsHelper
    {
        private static readonly string _projectPath;

        static WeaverPathsHelper() =>
            _projectPath = Directory.GetParent(Application.dataPath).FullName;

        [NotNull]
        public static IEnumerable<string> GetSearchPaths()
        {
            ListRequest listRequest = Client.List(true);
            while (listRequest.Status == StatusCode.InProgress)
            {
                Thread.Sleep(10);
            }

            return listRequest.Result.Select(info => info.resolvedPath)
                .Concat(
                    new[]
                    {
                        _projectPath
                    });
        }

        public static string AddProjectPathRootIfNeeded(string path) =>
            Path.IsPathRooted(path) ? path : Path.Combine(_projectPath, path);
    }
}
