namespace TkHLSL.Unity;

/// <summary>
///     Marks a <c>partial class</c> or <c>partial struct</c> as the binding surface for a compute
///     shader.
///     <see>
///         <cref>TkHLSL.SourceGeneration</cref>
///     </see>
///     's generator looks for this attribute (by its
///     fully-qualified metadata name, via <c>ForAttributeWithMetadataName</c>) and, for each type it
///     is applied to, generates a nested type per kernel with typed <c>Set*</c> members for every
///     resource that kernel uses, plus <c>DispatchThreads</c>/<c>DispatchGroups</c> methods — see
///     docs/IMPLEMENTATION_PLAN.md §9 Phase 9.
/// </summary>
/// <remarks>
///     The generator resolves <see cref="Path" /> against the project's Roslyn <c>AdditionalFiles</c>
///     only — it never touches disk itself. In Unity, that means the target <c>.compute</c> (and any
///     file it <c>#include</c>s) must be listed as an additional file for the compiling assembly,
///     typically via a <c>csc.rsp</c> next to the assembly's <c>.asmdef</c>
///     (<c>/additionalfile:Assets/Shaders/Blur.compute</c>).
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class ComputeShaderBindingAttribute : Attribute
{
    /// <param name="path">
    ///     The compute shader's path, as it appears among the compiling assembly's Roslyn
    ///     <c>AdditionalFiles</c>. Matched by segment-wise suffix against every additional file's
    ///     normalized path (see
    ///     <see>
    ///         <cref>TkHLSL.SourceGeneration</cref>
    ///     </see>
    ///     's generator), so
    ///     <c>"Shaders/Blur.compute"</c> matches an additional file at
    ///     <c>Assets/Shaders/Blur.compute</c>.
    /// </param>
    public ComputeShaderBindingAttribute(string path)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
    }

    /// <summary>The compute shader's path (see the constructor parameter of the same name).</summary>
    public string Path { get; }

    /// <summary>
    ///     Preprocessor symbols to define while parsing the shader — the source-generator
    ///     equivalent of Unity's <c>multi_compile</c>/<c>shader_feature</c> variant keywords, passed
    ///     through to
    ///     <see>
    ///         <cref>TkHLSL.Preprocessing.HlslParseOptions.DefinedSymbols</cref>
    ///     </see>
    ///     . A shader
    ///     that branches on an undefined symbol generates bindings for whichever branch is taken with
    ///     no symbols defined.
    /// </summary>
    public string[]? Defines { get; set; }
}