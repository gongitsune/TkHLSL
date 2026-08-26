// See the C# 9 note at the top of TkHLSLManifestPostprocessor.cs — it applies here too.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using TkHLSL.Unity.Editor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    ///     Full-rebuild recovery path for <c>Assets/TkHLSL.Generated/*.additionalfile</c> — use this
    ///     after first installing the package (no manifests exist yet) or if a manifest and its
    ///     shader ever fall out of sync. <see cref="TkHlslManifestPostprocessor" /> otherwise keeps
    ///     manifests updated incrementally as shaders change.
    /// </summary>
    internal static class TkHlslManifestMenu
    {
        private static readonly string[] ShaderExtensions =
        {
            ".compute", ".hlsl", ".cginc", ".hlslinc"
        };

        [MenuItem("Tools/TkHLSL/Rebuild Shader Manifests")]
        private static void RebuildAll()
        {
            var roots = new List<string>();
            foreach (var searchRoot in new[] { "Assets", "Packages" })
            {
                if (!Directory.Exists(searchRoot)) continue;
                foreach (var file in Directory.GetFiles(searchRoot, "*.compute", SearchOption.AllDirectories))
                    roots.Add(file.Replace('\\', '/'));
            }

            if (Directory.Exists(TkHlslManifestPostprocessor.OutputDirectory))
                foreach (var stale in Directory.GetFiles(TkHlslManifestPostprocessor.OutputDirectory,
                             "*.additionalfile", SearchOption.TopDirectoryOnly))
                    AssetDatabase.DeleteAsset(stale.Replace('\\', '/'));

            var filenameIndex = BuildFilenameIndex();
            Directory.CreateDirectory(TkHlslManifestPostprocessor.OutputDirectory);

            var written = 0;
            foreach (var root in roots)
            {
                var manifestText = ShaderManifestBuilder.Build(root, ReadAllTextOrNull, File.Exists, filenameIndex);
                var manifestPath = TkHlslManifestPostprocessor.OutputDirectory + "/" +
                                    root.Replace('/', '-').Replace('\\', '-') + ".additionalfile";
                File.WriteAllText(manifestPath, manifestText);
                written++;
            }

            AssetDatabase.Refresh();
            Debug.Log("TkHLSL: rebuilt " + written + " shader manifest(s) under " +
                      TkHlslManifestPostprocessor.OutputDirectory + ".");
        }

        private static Dictionary<string, string> BuildFilenameIndex()
        {
            var index = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var searchRoot in new[] { "Assets", "Packages" })
            {
                if (!Directory.Exists(searchRoot)) continue;
                foreach (var ext in ShaderExtensions)
                foreach (var file in Directory.GetFiles(searchRoot, "*" + ext, SearchOption.AllDirectories))
                {
                    var relative = file.Replace('\\', '/');
                    var name = Path.GetFileName(relative);
                    if (!index.ContainsKey(name)) index[name] = relative;
                }
            }

            return index;
        }

        private static string ReadAllTextOrNull(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }
}
