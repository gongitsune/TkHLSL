#!/usr/bin/env bash
# Builds Tk.Hlsl, Tk.Hlsl.Unity, and Tk.Hlsl.SourceGeneration in Release and copies the resulting
# assemblies into Runtime/, alongside a generated .meta file for each (see docs/IMPLEMENTATION_PLAN.md
# §9 Phase 10). The DLLs and their .meta files are NOT checked into source control — run this script
# to (re)populate Runtime/ before importing this folder as a Unity package. .meta GUIDs below are
# fixed, so re-running this script does not change a DLL's asset identity in a Unity project that
# already references this package.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
RUNTIME_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/Runtime"

echo "Building Release..."
dotnet build "$ROOT/src/Tk.Hlsl.Unity/Tk.Hlsl.Unity.csproj" -c Release
dotnet build "$ROOT/src/Tk.Hlsl/Tk.Hlsl.csproj" -c Release -f netstandard2.0
dotnet build "$ROOT/src/Tk.Hlsl.SourceGeneration/Tk.Hlsl.SourceGeneration.csproj" -c Release

mkdir -p "$RUNTIME_DIR"

copy_dll() {
  local src="$1" name="$2"
  if [ -f "$src" ]; then
    cp "$src" "$RUNTIME_DIR/$name"
    echo "Copied $name"
  else
    echo "WARNING: $src not found, skipping $name" >&2
  fi
}

copy_dll "$ROOT/src/Tk.Hlsl.Unity/bin/Release/netstandard2.0/Tk.Hlsl.Unity.dll" "Tk.Hlsl.Unity.dll"
copy_dll "$ROOT/src/Tk.Hlsl/bin/Release/netstandard2.0/Tk.Hlsl.dll" "Tk.Hlsl.dll"
copy_dll "$ROOT/src/Tk.Hlsl.SourceGeneration/bin/Release/netstandard2.0/Tk.Hlsl.SourceGeneration.dll" \
  "Tk.Hlsl.SourceGeneration.dll"
# Only present if System.Memory's runtime assembly was actually needed for this SDK/TFM combo —
# see the "System.Memory" note in this package's README.
copy_dll "$ROOT/src/Tk.Hlsl/bin/Release/netstandard2.0/System.Memory.dll" "System.Memory.dll"

# --- .meta generation --------------------------------------------------------------------------

write_runtime_plugin_meta() {
  local guid="$1" file="$2"
  cat > "$RUNTIME_DIR/$file.meta" <<EOF
fileFormatVersion: 2
guid: $guid
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Any:
    second:
      enabled: 1
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 0
      settings:
        DefaultValueInitialized: true
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
}

write_analyzer_plugin_meta() {
  local guid="$1" file="$2"
  cat > "$RUNTIME_DIR/$file.meta" <<EOF
fileFormatVersion: 2
guid: $guid
labels:
- RoslynAnalyzer
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Any:
    second:
      enabled: 0
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 0
      settings:
        DefaultValueInitialized: true
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
}

# Fixed GUIDs — do not change once this package has shipped; a changed GUID breaks every scene/
# asmdef reference a consuming project already has to that DLL.
write_runtime_plugin_meta  "a1b6c8f0d3e4415a9b6c7d2e1f4a5b6c" "Tk.Hlsl.Unity.dll"
write_analyzer_plugin_meta "b2c7d9e1f4053526ac7d8e3f2a5b6c7d" "Tk.Hlsl.dll"
write_analyzer_plugin_meta "c3d8eaf205164637bd8e9f4a3b6c7d8e" "Tk.Hlsl.SourceGeneration.dll"
write_analyzer_plugin_meta "d4e9fb0316275748ce9fa05b4c7d8e9f" "System.Memory.dll"

echo "Done. Runtime/ is ready to import as a local Unity package."
