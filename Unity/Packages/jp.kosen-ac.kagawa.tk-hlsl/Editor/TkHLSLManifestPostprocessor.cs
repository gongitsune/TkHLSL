// Compiled directly by Unity (this file lives under the package's Editor/ folder, imported without
// the RoslynAnalyzer label) — Unity's own C# compiler invocation is fixed at -langversion:9.0, so
// this file (and TkHLSLManifestMenu.cs) must stay within the C# 9 subset: no collection expressions,
// no primary constructors, no file-scoped namespaces, no raw string literals. Everything that needs
// newer C# lives in TkHLSL.Unity.Editor.dll instead (a prebuilt plugin Unity only loads, never
// recompiles) — see docs/IMPLEMENTATION_PLAN.md, "csc.rsp を廃止し..." plan §3.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TkHLSL.Unity.Editor;
using UnityEditor;

namespace TkHLSL.Unity.PackageEditor
{
    /// <summary>
    ///     Keeps <c>Assets/TkHLSL.Generated/*.additionalfile</c> shader manifests in sync with every
    ///     <c>.compute</c> in the project, so <c>TkHLSL.SourceGeneration</c> never needs a
    ///     hand-written <c>csc.rsp</c> to see a shader's <c>#include</c> closure — see the package
    ///     README's "Shader manifests (automatic)" section and
    ///     docs/IMPLEMENTATION_PLAN.md, "csc.rsp を廃止し..." plan §3.
    /// </summary>
    public sealed class TkHLSLManifestPostprocessor : AssetPostprocessor
    {
        internal const string OutputDirectory = "Assets/TkHLSL.Generated";

        private static readonly string[] ShaderExtensions =
        {
            ".compute", ".hlsl", ".cginc", ".hlslinc"
        };

        // Unity discovers this by exact name/signature on any type deriving from AssetPostprocessor
        // and calls it once per asset-database refresh with every asset that changed.
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            var changed = new HashSet<string>(StringComparer.Ordinal);
            CollectShaderPaths(importedAssets, changed);
            CollectShaderPaths(movedAssets, changed);

            var deletedRoots = new HashSet<string>(StringComparer.Ordinal);
            CollectShaderPaths(deletedAssets, deletedRoots);
            CollectShaderPaths(movedFromAssetPaths, deletedRoots);

            if (changed.Count == 0 && deletedRoots.Count == 0) return;

            RemoveOrphanedManifests(deletedRoots, changed);

            var rootsToRebuild = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in changed)
                if (path.EndsWith(".compute", StringComparison.OrdinalIgnoreCase))
                    rootsToRebuild.Add(path);

            // A changed .hlsl/.cginc might be included by shaders that weren't themselves touched —
            // find them via the 'input' list already recorded in every existing manifest.
            foreach (var manifestPath in ExistingManifestPaths())
            {
                var text = SafeReadAllText(manifestPath);
                if (text == null) continue;
                if (!ShaderManifestBuilder.TryReadRootAndInputs(text, out var root, out var inputs)) continue;
                foreach (var input in inputs)
                    if (changed.Contains(input))
                    {
                        rootsToRebuild.Add(root);
                        break;
                    }
            }

            if (rootsToRebuild.Count == 0) return;

            var filenameIndex = BuildFilenameIndex();
            var reimport = new List<string>();
            foreach (var root in rootsToRebuild)
            {
                if (!File.Exists(root)) continue; // deleted between scan and here — nothing to (re)build

                var manifestPath = ManifestPathFor(root);
                var manifestText = ShaderManifestBuilder.Build(root, SafeReadAllText, File.Exists, filenameIndex);

                var existing = SafeReadAllText(manifestPath);
                if (existing == manifestText) continue; // avoid a no-op re-import -> re-postprocess loop

                Directory.CreateDirectory(OutputDirectory);
                File.WriteAllText(manifestPath, manifestText);
                reimport.Add(manifestPath);
            }

            foreach (var path in reimport) AssetDatabase.ImportAsset(path);
        }

        private static void RemoveOrphanedManifests(HashSet<string> deletedRoots, HashSet<string> changedRoots)
        {
            foreach (var manifestPath in ExistingManifestPaths())
            {
                var text = SafeReadAllText(manifestPath);
                if (text == null) continue;
                if (!ShaderManifestBuilder.TryReadRootAndInputs(text, out var root, out _)) continue;
                if (deletedRoots.Contains(root) && !File.Exists(root)) AssetDatabase.DeleteAsset(manifestPath);
            }
        }

        private static void CollectShaderPaths(string[] paths, HashSet<string> into)
        {
            foreach (var path in paths)
            foreach (var ext in ShaderExtensions)
                if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    into.Add(path);
                    break;
                }
        }

        private static IEnumerable<string> ExistingManifestPaths()
        {
            if (!Directory.Exists(OutputDirectory)) yield break;
            foreach (var path in Directory.GetFiles(OutputDirectory, "*.additionalfile", SearchOption.TopDirectoryOnly))
                yield return path.Replace('\\', '/');
        }

        private static Dictionary<string, string> BuildFilenameIndex()
        {
            var index = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var root in new[] { "Assets", "Packages" })
            {
                if (!Directory.Exists(root)) continue;
                foreach (var ext in ShaderExtensions)
                foreach (var file in Directory.GetFiles(root, "*" + ext, SearchOption.AllDirectories))
                {
                    var relative = file.Replace('\\', '/');
                    var name = Path.GetFileName(relative);
                    if (!index.ContainsKey(name)) index[name] = relative;
                }
            }

            return index;
        }

        private static string ManifestPathFor(string rootPath)
        {
            var flat = rootPath.Replace('/', '-').Replace('\\', '-');
            return OutputDirectory + "/" + flat + ".additionalfile";
        }

        private static string SafeReadAllText(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}
