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

