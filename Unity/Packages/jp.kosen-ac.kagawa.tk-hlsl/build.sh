#!/usr/bin/env bash
# Builds TkHLSL, TkHLSL.Unity, TkHLSL.SourceGeneration, and TkHLSL.Unity.Editor in Release and copies
# the resulting assemblies into Runtime/ and Editor/ respectively, alongside a generated .meta file
# for each (see docs/IMPLEMENTATION_PLAN.md §9 Phase 10). The DLLs and their .meta files are NOT
# checked into source control — run this script to (re)populate Runtime/ and Editor/ before importing
# this folder as a Unity package. .meta GUIDs below are fixed, so re-running this script does not
# change a DLL's asset identity in a Unity project that already references this package.
set -euo pipefail

PACKAGE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# Pre-existing bug fix: this package lives at <repo>/Unity/Packages/<name>/, three levels below the
# repo root, not two — ../.. only reached <repo>/Unity.
ROOT="$(cd "$PACKAGE_DIR/../../.." && pwd)"
RUNTIME_DIR="$PACKAGE_DIR/Runtime"
EDITOR_DIR="$PACKAGE_DIR/Editor"

echo "Building Release..."
dotnet build "$ROOT/src/TkHLSL.Unity/TkHLSL.Unity.csproj" -c Release
dotnet build "$ROOT/src/TkHLSL/TkHLSL.csproj" -c Release -f netstandard2.0
dotnet build "$ROOT/src/TkHLSL.SourceGeneration/TkHLSL.SourceGeneration.csproj" -c Release
dotnet build "$ROOT/src/TkHLSL.Unity.Editor/TkHLSL.Unity.Editor.csproj" -c Release

mkdir -p "$RUNTIME_DIR" "$EDITOR_DIR"

copy_dll() {
  local src="$1" dest_dir="$2" name="$3"
  if [ -f "$src" ]; then
    cp "$src" "$dest_dir/$name"
    echo "Copied $name"
  else
    echo "WARNING: $src not found, skipping $name" >&2
  fi
}

copy_dll "$ROOT/src/TkHLSL.Unity/bin/Release/netstandard2.0/TkHLSL.Unity.dll" "$RUNTIME_DIR" "TkHLSL.Unity.dll"
copy_dll "$ROOT/src/TkHLSL/bin/Release/netstandard2.0/TkHLSL.dll" "$RUNTIME_DIR" "TkHLSL.dll"
copy_dll "$ROOT/src/TkHLSL.SourceGeneration/bin/Release/netstandard2.0/TkHLSL.SourceGeneration.dll" \
  "$RUNTIME_DIR" "TkHLSL.SourceGeneration.dll"
copy_dll "$ROOT/src/TkHLSL.Unity.Editor/bin/Release/netstandard2.1/TkHLSL.Unity.Editor.dll" \
  "$EDITOR_DIR" "TkHLSL.Unity.Editor.dll"

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

echo "Done. Runtime/ and Editor/ are ready to import as a local Unity package."
