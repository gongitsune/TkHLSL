using Microsoft.CodeAnalysis;

namespace TkHLSL.SourceGeneration.Diagnostics;

/// <summary>
///     Every <see cref="DiagnosticDescriptor" />
///     <see>
///         <cref>Generator.ComputeShaderBindingGenerator</cref>
///     </see>
///     can report, collected in one place (see docs/IMPLEMENTATION_PLAN.md §9 Phase 9.3).
/// </summary>
internal static class TkHlslDiagnostics
{
    private const string Category = "TkHLSL.SourceGeneration";

    /// <summary>
    ///     An HLSL parse error, transcribed from a <see cref="TkHLSL.Diagnostics.Diagnostic" /> of
    ///     <see cref="TkHLSL.Diagnostics.DiagnosticSeverity.Error" />. Generation for this file is skipped.
    /// </summary>
    public static readonly DiagnosticDescriptor HlslError = new(
        "TKH0001", "HLSL の解析に失敗しました",
        "'{0}' の解析でエラーが発生しました: {1}",
        Category, DiagnosticSeverity.Error, true);

    /// <summary>
    ///     An HLSL parse warning, transcribed from a <see cref="TkHLSL.Diagnostics.Diagnostic" /> of
    ///     <see cref="TkHLSL.Diagnostics.DiagnosticSeverity.Warning" />.
    /// </summary>
    public static readonly DiagnosticDescriptor HlslWarning = new(
        "TKH0002", "HLSL の解析で警告が発生しました",
        "'{0}' の解析で警告が発生しました: {1}",
        Category, DiagnosticSeverity.Warning, true);

    /// <summary>No AdditionalFile matched the attribute's path.</summary>
    public static readonly DiagnosticDescriptor FileNotFound = new(
        "TKH1001", "コンピュートシェーダーファイルが見つかりません",
        "'{0}' に対応する AdditionalFiles が見つかりません。.compute/.hlsl ファイルが AdditionalFiles として渡されているか確認してください。",
        Category, DiagnosticSeverity.Error, true);

    /// <summary>More than one AdditionalFile matched the attribute's path.</summary>
    public static readonly DiagnosticDescriptor AmbiguousFile = new(
        "TKH1002", "コンピュートシェーダーファイルの指定が曖昧です",
        "'{0}' が複数の AdditionalFiles に一致しました: {1}。より詳しいパスを指定してください。",
        Category, DiagnosticSeverity.Error, true);

    /// <summary>The attributed type is not declared <c>partial</c>.</summary>
    public static readonly DiagnosticDescriptor TypeNotPartial = new(
        "TKH1003", "型に partial 修飾子がありません",
        "[ComputeShaderBinding] を付与した型 '{0}' には partial 修飾子が必要です。",
        Category, DiagnosticSeverity.Error, true);

    /// <summary>The parsed shader has no kernels.</summary>
    public static readonly DiagnosticDescriptor NoKernels = new(
        "TKH1004", "カーネルが見つかりません",
        "'{0}' に '#pragma kernel' が見つかりません。バインディングは生成されません。",
        Category, DiagnosticSeverity.Warning, true);

    /// <summary>A resource/type has no known mapping to a Unity/C# API, so no member was generated for it.</summary>
    public static readonly DiagnosticDescriptor UnmappedResource = new(
        "TKH1005", "対応する Unity API が見つかりません",
        "'{0}' ({1}) に対応する Unity API がないため、このメンバーは生成されませんでした。",
        Category, DiagnosticSeverity.Warning, true);

    /// <summary>
    ///     A generated element struct's C# layout may not match HLSL's constant-buffer packing rules.
    ///     Reserved for a struct-typed <c>cbuffer</c> member (not currently emitted): a
    ///     <c>StructuredBuffer&lt;T&gt;</c> element is packed tightly rather than under the 16-byte
    ///     rule, so <see cref="Emit.CodeEmitter" />'s element-struct path never needs it.
    /// </summary>
    public static readonly DiagnosticDescriptor StructPackingMismatch = new(
        "TKH1006", "構造体のレイアウトが HLSL のパッキング規則と一致しない可能性があります",
        "struct '{0}' は HLSL の 16 バイト境界パッキング規則と C# の Sequential レイアウトが一致しない可能性があります。GPU 側のレイアウトと目視で突き合わせてください。",
        Category, DiagnosticSeverity.Warning, true);

    /// <summary>
    ///     A <c>*.additionalfile</c> manifest matched the requested path, but none of its variants was
    ///     analyzed with the <c>Defines</c> the attribute requested — the manifest pipeline currently
    ///     only ever produces a no-defines variant (see docs/IMPLEMENTATION_PLAN.md, "csc.rsp を廃止し..."
    ///     plan §5). Passing the raw <c>.compute</c> (and its includes) as AdditionalFiles instead
    ///     resolves via the original parse-on-the-fly pipeline, which supports <c>Defines</c> fully.
    /// </summary>
    public static readonly DiagnosticDescriptor NoManifestForDefines = new(
        "TKH1007", "指定された Defines に一致するシェーダーマニフェストが見つかりません",
        "'{0}' に一致するマニフェストが見つかりましたが、Defines = [{1}] に一致するものがありません。" +
        "この Defines の組み合わせで生成するには、対象の .compute (と #include ファイル) を直接 AdditionalFiles として渡してください。",
        Category, DiagnosticSeverity.Error, true);

    /// <summary>
    ///     Every descriptor above, keyed by <see cref="DiagnosticDescriptor.Id" />, for reconstructing a
    ///     <see cref="Diagnostic" /> from a cached <see cref="SourceGeneration.EmitDiagnosticInfo" />.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, DiagnosticDescriptor> ById =
        new Dictionary<string, DiagnosticDescriptor>
        {
            [HlslError.Id] = HlslError,
            [HlslWarning.Id] = HlslWarning,
            [FileNotFound.Id] = FileNotFound,
            [AmbiguousFile.Id] = AmbiguousFile,
            [TypeNotPartial.Id] = TypeNotPartial,
            [NoKernels.Id] = NoKernels,
            [UnmappedResource.Id] = UnmappedResource,
            [StructPackingMismatch.Id] = StructPackingMismatch,
            [NoManifestForDefines.Id] = NoManifestForDefines
        };
}