# TkHLSL for Unity

Generates typed C# `ComputeShader` bindings at compile time from your `.compute` files: a nested type
per kernel with `Set_<Name>` resource setters and `DispatchThreads`/`DispatchGroups`, outer-level
setters for plain globals and `cbuffer` members, and a `[StructLayout(Sequential)]` element `struct`
for any `StructuredBuffer<T>`-family resource whose element type is a user `struct`. See the root
repository's `docs/IMPLEMENTATION_PLAN.md` for the full design.

## Install (local package)

```shell

```

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

## Shader manifests (automatic — no `csc.rsp` needed)

The generator never reads `.compute`/`.hlsl`/`.cginc` from disk itself — it only sees files Unity's
compiler passes it as a Roslyn `AdditionalFile`. This package's `Editor/` importer does that wiring
for you: whenever a `.compute` (or anything it `#include`s) changes, it parses the shader's full
`#include` closure and writes a structured summary — every kernel, resource, `cbuffer`, `struct`, and
diagnostic, but **no shader source text** — to `Assets/TkHLSL.Generated/<flattened-path>.additionalfile`.
Unity imports `*.additionalfile` as a Roslyn AdditionalFile natively, so that's all the generator
needs; there is nothing to add to a `csc.rsp`.

`ComputeShaderBindingAttribute.Path` is matched against a manifest's shader path the same way it
matched a raw AdditionalFile before: by path suffix, so `"Shaders/Blur.compute"` matches a manifest
generated for `Assets/Shaders/Blur.compute`.

If manifests are ever missing or out of sync (e.g. right after installing the package, since nothing
has changed yet to trigger the importer), run **Tools → TkHLSL → Rebuild Shader Manifests** to
regenerate every manifest from scratch.

### `Defines` limitation

The Editor importer has no way to know about a `[ComputeShaderBinding(Defines = new[] { "FOO" })]`
attribute elsewhere in your scripts — it only ever analyzes a shader with no symbols defined, so a
`Defines`-using binding reports `TKH1007` ("no manifest matches these Defines") against the automatic
manifest. To generate that specific variant, fall back to the old mechanism for that one shader: add a
`csc.rsp` next to the `.asmdef` of the assembly containing the `[ComputeShaderBinding]` type (or
`Assets/csc.rsp` for the Predefined Assembly) listing the shader and its includes directly —

```
# Assets/csc.rsp (or next to your .asmdef)
/additionalfile:Assets/Shaders/Blur.compute
/additionalfile:Assets/Shaders/Common.hlsl
```

— and the generator will prefer that raw AdditionalFile over the automatic manifest for this shader.

## What's in `Runtime/` and `Editor/` (after running `build.sh`)

| File | Role |
|---|---|
| `Runtime/TkHLSL.Unity.dll` | The `[ComputeShaderBinding]` attribute. An ordinary plugin — your scripts reference it directly. |
| `Runtime/TkHLSL.SourceGeneration.dll` | The generator itself. Imported with the `RoslynAnalyzer` label, excluded from every platform build. |
| `Runtime/TkHLSL.dll` | The HLSL parser the generator depends on. Also `RoslynAnalyzer`-labeled — it must ship alongside the generator DLL for Unity's analyzer loader to find it. |
| `Editor/TkHLSL.Unity.Editor.dll` | The same HLSL parser, built as an ordinary (non-`RoslynAnalyzer`) Editor-only plugin, so `Editor/*.cs` below can call into it. |
| `Editor/TkHLSLManifestPostprocessor.cs` | An `AssetPostprocessor` that (re)writes `Assets/TkHLSL.Generated/*.additionalfile` whenever a `.compute`/`.hlsl`/`.cginc` changes. |
| `Editor/TkHLSLManifestMenu.cs` | Adds **Tools → TkHLSL → Rebuild Shader Manifests** for a full from-scratch rebuild. |

