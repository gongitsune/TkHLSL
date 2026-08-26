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
///     only — it never touches disk itself. In the <c>TkHLSL.Unity</c> package, a <c>.compute</c> (and
///     everything it <c>#include</c>s) never needs to be listed manually: the package's Editor-side
///     importer parses it and writes a structured <c>*.additionalfile</c> manifest to
///     <c>Assets/TkHLSL.Generated/</c> automatically, which Unity then passes to the compiler as a
///     Roslyn AdditionalFile on its own. Outside Unity (or for a <see cref="Defines" />-using binding,
///     which the importer cannot know about — see the package README), the target file and its
///     includes can instead be passed as raw AdditionalFiles directly
///     (<c>/additionalfile:Assets/Shaders/Blur.compute</c> in a <c>csc.rsp</c>).
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