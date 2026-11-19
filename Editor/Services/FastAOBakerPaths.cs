using System;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace DennokoWorks.Tool.AOBaker
{
    /// <summary>
    /// Resolves editor-only assets belonging to FastAOBaker regardless of where the tool folder is placed.
    /// </summary>
    internal static class FastAOBakerPaths
    {
        private const string DefaultEditorRoot = "Assets/dennokoworks/FastAOBaker/Editor";
        private const string ScriptSuffix = "/Services/FastAOBakerPaths.cs";

        private static string _cachedEditorRoot;

        public static T LoadAsset<T>(string editorRelativePath) where T : UnityEngine.Object
        {
            string relative = editorRelativePath.TrimStart('/');

            // 1. Try cached root first
            if (!string.IsNullOrEmpty(_cachedEditorRoot))
            {
                var cached = AssetDatabase.LoadAssetAtPath<T>($"{_cachedEditorRoot}/{relative}");
                if (cached != null) return cached;
                _cachedEditorRoot = null;
            }

            // 2. Locate this script via AssetDatabase (handles moves without recompile and UPM Packages/)
            string[] guids = AssetDatabase.FindAssets("FastAOBakerPaths t:Script");
            foreach (string guid in guids)
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(scriptPath)) continue;

                string normalized = scriptPath.Replace('\\', '/');
                if (!normalized.EndsWith(ScriptSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string candidateRoot = normalized.Substring(0, normalized.Length - ScriptSuffix.Length);
                var asset = AssetDatabase.LoadAssetAtPath<T>($"{candidateRoot}/{relative}");
                if (asset != null)
                {
                    _cachedEditorRoot = candidateRoot;
                    return asset;
                }
            }

            // 3. Derive root from compile-time source path
            string callerRoot = TryGetRootFromCallerPath();
            if (!string.IsNullOrEmpty(callerRoot))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>($"{callerRoot}/{relative}");
                if (asset != null)
                {
                    _cachedEditorRoot = callerRoot;
                    return asset;
                }
            }

            // 4. Fallback to default install path
            var fallback = AssetDatabase.LoadAssetAtPath<T>($"{DefaultEditorRoot}/{relative}");
            if (fallback != null)
            {
                _cachedEditorRoot = DefaultEditorRoot;
                return fallback;
            }

            return null;
        }

        public static string ResolveAssetPath(string editorRelativePath)
        {
            string relative = editorRelativePath.TrimStart('/');
            string root = !string.IsNullOrEmpty(_cachedEditorRoot)
                ? _cachedEditorRoot
                : (TryGetRootFromCallerPath() ?? DefaultEditorRoot);
            return $"{root}/{relative}";
        }

        private static string TryGetRootFromCallerPath([CallerFilePath] string sourceFilePath = null)
        {
            if (string.IsNullOrEmpty(sourceFilePath)) return null;

            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
                if (!projectRoot.EndsWith("/")) projectRoot += "/";

                string fullSource = Path.GetFullPath(sourceFilePath).Replace('\\', '/');
                if (!fullSource.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                    return null;

                string assetPath = fullSource.Substring(projectRoot.Length);
                if (!assetPath.EndsWith(ScriptSuffix, StringComparison.OrdinalIgnoreCase))
                    return null;

                return assetPath.Substring(0, assetPath.Length - ScriptSuffix.Length);
            }
            catch
            {
                return null;
            }
        }
    }
}
