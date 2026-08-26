# TkHLSL for Unity

Generates typed C# `ComputeShader` bindings at compile time from your `.compute` files: a nested type
per kernel with `Set_<Name>` resource setters and `DispatchThreads`/`DispatchGroups`, outer-level
setters for plain globals and `cbuffer` members, and a `[StructLayout(Sequential)]` element `struct`
for any `StructuredBuffer<T>`-family resource whose element type is a user `struct`. See the root
repository's `docs/IMPLEMENTATION_PLAN.md` for the full design.

This package has not yet been verified inside an actual Unity project — see "Known unknowns" below
before relying on it.

## Install (local package)

1. Run `./build.sh` from this directory (requires the .NET SDK; builds `src/TkHLSL`,
   `src/TkHLSL.Unity`, and `src/TkHLSL.SourceGeneration` in Release and populates `Runtime/`).
2. In your Unity project's Package Manager, **Add package from disk...** and pick this folder's
   `package.json` — or add `"jp.keigo.tk-hlsl": "file:../relative/path/to/this/folder"` to your
   project's `Packages/manifest.json` directly.

## Usage

```csharp
using TkHLSL.Unity;

namespace MyGame
{
    [ComputeShaderBinding("Shaders/Blur.compute")]
    public partial class BlurShader { }
}
```

Construct it with the `ComputeShader` asset (`new BlurShader(myComputeShaderAsset)`), then call the
generated `Set_*` methods and `Dispatch*` per kernel — e.g. `shader.Blur.SetInput(tex);
shader.Blur.DispatchThreads(width, height, 1);`.

## Wiring `.compute` as an AdditionalFile (required)

The generator never reads from disk — it only sees files Unity's compiler passes it as a Roslyn
`AdditionalFile`. You must tell Unity to do that for every `.compute`/`.hlsl`/`.cginc` the generator
needs, via a `csc.rsp` response file next to the `.asmdef` of the assembly containing your
`[ComputeShaderBinding]` type (the Predefined Assembly — `Assets/csc.rsp` — if your scripts aren't in
an asmdef):

```
# Assets/csc.rsp (or next to your .asmdef)
/additionalfile:Assets/Shaders/Blur.compute
```

List every `.compute` file (and every file any of them `#include`s) your `[ComputeShaderBinding]`
attributes reference. Unity re-triggers compilation when `csc.rsp` or a listed file changes.
`ComputeShaderBindingAttribute.Path` is matched against these by path suffix, so
`"Shaders/Blur.compute"` matches `Assets/Shaders/Blur.compute`.

## What's in `Runtime/` (after running `build.sh`)

| File | Role |
|---|---|
| `TkHLSL.Unity.dll` | The `[ComputeShaderBinding]` attribute. An ordinary plugin — your scripts reference it directly. |
| `TkHLSL.SourceGeneration.dll` | The generator itself. Imported with the `RoslynAnalyzer` label, excluded from every platform build. |
| `TkHLSL.dll` | The HLSL parser the generator depends on. Also `RoslynAnalyzer`-labeled — it must ship alongside the generator DLL for Unity's analyzer loader to find it. |
| `System.Memory.dll` | Only present if the Release build of `TkHLSL` actually needed it for netstandard2.0 (whether it does depends on the .NET SDK version used to build — see the note in `src/TkHLSL/TkHLSL.csproj`). Also `RoslynAnalyzer`-labeled if present. |

## Known unknowns (verify before relying on this)

- **Whether a package-local `RoslynAnalyzer`-labeled DLL applies to your assembly at all.** Unity's
  documented mechanism is: a DLL with the `RoslynAnalyzer` label applies to every assembly compiled
  *after* it in the project (broadly, everything, once imported) — but this hasn't been confirmed
  against a real Unity 6 project as part of this work. If your `[ComputeShaderBinding]` type doesn't
  get a generated partial after importing this package and wiring `csc.rsp`, check the Inspector on
  `TkHLSL.SourceGeneration.dll` for the `RoslynAnalyzer` label and try moving the three analyzer
  DLLs into `Assets/` directly as a fallback.
- **Whether the generator needs `System.Memory.dll` at all in Unity's Roslyn host.** Its own
  `README`/csproj note explains why — worth confirming empirically once you can test in Unity 6.
- **GUID stability across `build.sh` re-runs.** The GUIDs baked into `build.sh` are fixed by design
  (so re-running it doesn't change asset identity), but this has not been exercised across an actual
  Unity re-import cycle.
