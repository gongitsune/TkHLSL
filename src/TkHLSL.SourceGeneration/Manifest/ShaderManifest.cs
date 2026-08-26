using System.Globalization;
using System.Text;
using TkHLSL.Diagnostics;
using TkHLSL.Ir;
using TkHLSL.Model;
using TkHLSL.Text;

namespace TkHLSL.SourceGeneration.Manifest;

/// <summary>
///     A resolved source location for one manifest element, independent of any composite
///     <see cref="SourceText" /> — a manifest never carries HLSL source text, so every
///     <see cref="TextSpan" /> in a <see cref="HlslCompilationResult" /> read from one is a synthetic
///     lookup key into a <see cref="ManifestData.Locations" /> table of these instead of a real
///     composite offset (see docs/IMPLEMENTATION_PLAN.md, "csc.rsp を廃止し..." plan §2).
/// </summary>
internal readonly struct ManifestLocation
{
    public ManifestLocation(string path, int startLine, int startChar, int endLine, int endChar)
    {
        Path = path;
        StartLine = startLine;
        StartChar = startChar;
        EndLine = endLine;
        EndChar = endChar;
    }

    public string Path { get; }
    public int StartLine { get; }
    public int StartChar { get; }
    public int EndLine { get; }
    public int EndChar { get; }
}

/// <summary>The fully-parsed contents of one <c>*.additionalfile</c> shader manifest.</summary>
internal sealed class ManifestData
{
    public ManifestData(string root, IReadOnlyList<string> defines, IReadOnlyList<string> inputs,
        HlslCompilationResult result, IReadOnlyDictionary<int, ManifestLocation> locations)
    {
        Root = root;
        Defines = defines;
        Inputs = inputs;
        Result = result;
        Locations = locations;
    }

    /// <summary>The shader's root path, matched against a <c>[ComputeShaderBinding]</c> path the same way a raw AdditionalFile path is.</summary>
    public string Root { get; }

    /// <summary>The preprocessor symbols this manifest was analyzed with.</summary>
    public IReadOnlyList<string> Defines { get; }

    /// <summary>Every file (root + resolved includes) that contributed to <see cref="Result" />.</summary>
    public IReadOnlyList<string> Inputs { get; }

    public HlslCompilationResult Result { get; }

    /// <summary>Every <see cref="TextSpan" /> appearing in <see cref="Result" />, keyed by <see cref="TextSpan.Start" /> (a synthetic id, not a real offset).</summary>
    public IReadOnlyDictionary<int, ManifestLocation> Locations { get; }
}

/// <summary>
///     Reads and writes the <c>tkhlsl-manifest</c> file format: a structured, source-text-free
///     serialization of an <see cref="HlslCompilationResult" /> (kernels, resources, structs,
///     diagnostics — no shader source), produced by a Unity Editor-side importer and consumed by
///     <see cref="PipelineCompute" /> in place of re-parsing HLSL. Tab-separated fields, one record per
///     line, so paths and messages may contain spaces freely (only a real tab/newline in a field value
///     needs escaping — see <see cref="Escape" />). See docs/IMPLEMENTATION_PLAN.md, "csc.rsp を廃止し
///     Unity 側で解析結果を .additionalfile にキャッシュする" plan §1.
/// </summary>
/// <remarks>
///     This file is the sole source of truth for the format: it is linked, unmodified, into both
///     <c>TkHLSL.SourceGeneration</c> (this project) and the Unity package's Editor assembly that
///     writes manifests, so a change here can never desync reader from writer. It intentionally has
///     no dependency on <c>Microsoft.CodeAnalysis</c> or <c>UnityEditor</c> — only on
///     <see cref="TkHLSL.Model" />/<see cref="TkHLSL.Ir" />/<see cref="TkHLSL.Diagnostics" /> types,
///     which the Editor assembly also has (by linking TkHLSL's own sources) — so it can compile
///     unchanged in both places.
/// </remarks>
internal static class ShaderManifest
{
    private const string MagicToken = "tkhlsl-manifest";
    private const string FormatVersion = "1";
    private const string NullPlaceholder = "-";

    public static string Write(string root, IReadOnlyList<string> defines, IReadOnlyList<string> inputs,
        HlslCompilationResult result, Func<TextSpan, ManifestLocation> resolveLocation)
    {
        var locIds = new Dictionary<TextSpan, int>();
        var locLines = new List<string>();

        int LocId(TextSpan span)
        {
            if (locIds.TryGetValue(span, out var existing)) return existing;
            var id = locIds.Count;
            locIds[span] = id;
            var loc = resolveLocation(span);
            locLines.Add(Join("loc", Inv(id), Escape(loc.Path), Inv(loc.StartLine), Inv(loc.StartChar),
                Inv(loc.EndLine), Inv(loc.EndChar)));
            return id;
        }

        var body = new StringBuilder();

        foreach (var s in result.Structs)
        {
            body.Append(Join("struct", Escape(s.Name), Inv(LocId(s.Location)))).Append('\n');
            foreach (var f in s.Fields) WriteField(body, f, LocId);
        }

        foreach (var r in result.AllResources)
        {
            var (registerToken, spaceToken) = EncodeRegister(r.ExplicitRegister);
            body.Append(Join("resource", Escape(r.Name), r.ResourceKind.ToString(),
                r.ElementTypeName is null ? NullPlaceholder : Escape(r.ElementTypeName),
                registerToken, spaceToken, Inv(LocId(r.Location)))).Append('\n');
            foreach (var f in r.Fields) WriteField(body, f, LocId);
        }

        foreach (var k in result.Kernels)
        {
            body.Append(Join("kernel", Escape(k.Name), Inv(k.ThreadGroupSize.X), Inv(k.ThreadGroupSize.Y),
                Inv(k.ThreadGroupSize.Z), Inv(LocId(k.Location)))).Append('\n');
            foreach (var binding in k.Bindings)
                body.Append(Join("bind", Escape(binding.Name))).Append('\n');
        }

        foreach (var d in result.Diagnostics)
            body.Append(Join("diag", d.Severity.ToString(), Inv(LocId(d.Span)), Escape(d.Message))).Append('\n');

        var sb = new StringBuilder();
        sb.Append(Join(MagicToken, FormatVersion)).Append('\n');
        sb.Append(Join("root", Escape(root))).Append('\n');

        var definesLine = new StringBuilder("defines");
        foreach (var d in defines) definesLine.Append('\t').Append(Escape(d));
        sb.Append(definesLine).Append('\n');

        foreach (var input in inputs) sb.Append(Join("input", Escape(input))).Append('\n');
        foreach (var line in locLines) sb.Append(line).Append('\n');
        sb.Append(body);

        return sb.ToString();
    }

    public static bool TryRead(string text, out ManifestData? data)
    {
        data = null;
        if (string.IsNullOrEmpty(text)) return false;

        var lines = text.Split('\n');
        var header = SplitLine(lines[0]);
        if (header.Length < 2 || header[0] != MagicToken || header[1] != FormatVersion) return false;

        string? root = null;
        var defines = new List<string>();
        var inputs = new List<string>();
        var locations = new Dictionary<int, ManifestLocation>();
        var structs = new List<HlslStruct>();
        var resources = new List<ResourceBinding>();
        var resourcesByName = new Dictionary<string, ResourceBinding>(StringComparer.Ordinal);
        var kernels = new List<KernelBindingInfo>();
        var diagnostics = new List<Diagnostic>();

        string? pendingStructName = null;
        TextSpan pendingStructLoc = default;
        List<HlslField> pendingStructFields = new();

        string? pendingResourceName = null;
        ResourceKind pendingResourceKind = default;
        string? pendingResourceElementType = null;
        ResourceRegister? pendingResourceRegister = null;
        TextSpan pendingResourceLoc = default;
        List<HlslField> pendingResourceFields = new();

        string? pendingKernelName = null;
        ThreadGroupSize pendingKernelSize = default;
        TextSpan pendingKernelLoc = default;
        List<string> pendingKernelBindNames = new();

        void FlushStruct()
        {
            if (pendingStructName is null) return;
            structs.Add(new HlslStruct(pendingStructName, pendingStructFields.ToArray(), pendingStructLoc));
            pendingStructName = null;
            pendingStructFields = new List<HlslField>();
        }

        void FlushResource()
        {
            if (pendingResourceName is null) return;
            var resource = new ResourceBinding(pendingResourceName, pendingResourceKind, pendingResourceElementType,
                pendingResourceRegister, pendingResourceLoc, pendingResourceFields.ToArray());
            resources.Add(resource);
            resourcesByName[pendingResourceName] = resource;
            pendingResourceName = null;
            pendingResourceFields = new List<HlslField>();
        }

        void FlushKernel()
        {
            if (pendingKernelName is null) return;
            var bindings = new ResourceBinding[pendingKernelBindNames.Count];
            for (var i = 0; i < bindings.Length; i++)
                bindings[i] = resourcesByName.TryGetValue(pendingKernelBindNames[i], out var r)
                    ? r
                    // A bind referencing a name that has no 'resource' line is a malformed manifest
                    // (should never happen from ShaderManifestBuilder); fail soft with a placeholder
                    // rather than throwing, so one bad manifest doesn't crash the whole compilation.
                    : new ResourceBinding(pendingKernelBindNames[i], ResourceKind.PlainGlobal, null, null,
                        default);
            kernels.Add(new KernelBindingInfo(pendingKernelName, pendingKernelSize, bindings, pendingKernelLoc));
            pendingKernelName = null;
            pendingKernelBindNames = new List<string>();
        }

        var nextLocId = 0;

        for (var i = 1; i < lines.Length; i++)
        {
            var raw = lines[i];
            if (raw.Length == 0) continue;
            var fields = SplitLine(raw);
            if (fields.Length == 0) continue;

            switch (fields[0])
            {
                case "root":
                    FlushStruct();
                    FlushResource();
                    FlushKernel();
                    if (fields.Length >= 2) root = Unescape(fields[1]);
                    break;

                case "defines":
                    FlushStruct();
                    FlushResource();
                    FlushKernel();
                    for (var f = 1; f < fields.Length; f++) defines.Add(Unescape(fields[f]));
                    break;

                case "input":
                    FlushStruct();
                    FlushResource();
                    FlushKernel();
                    if (fields.Length >= 2) inputs.Add(Unescape(fields[1]));
                    break;

                case "loc":
                    FlushStruct();
                    FlushResource();
                    FlushKernel();
                    if (fields.Length >= 7 && int.TryParse(fields[1], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var id))
                    {
                        locations[id] = new ManifestLocation(Unescape(fields[2]),
                            ParseInt(fields[3]), ParseInt(fields[4]), ParseInt(fields[5]), ParseInt(fields[6]));
                        if (id >= nextLocId) nextLocId = id + 1;
                    }

                    break;

                case "struct":
                    FlushStruct();
                    FlushResource();
                    FlushKernel();
                    if (fields.Length >= 3)
                    {
                        pendingStructName = Unescape(fields[1]);
                        pendingStructLoc = new TextSpan(ParseInt(fields[2]), 0);
                    }

                    break;

                case "resource":
                    FlushStruct();
                    FlushResource();
                    FlushKernel();
                    if (fields.Length >= 7)
                    {
                        pendingResourceName = Unescape(fields[1]);
                        pendingResourceKind = ParseResourceKind(fields[2]);
                        pendingResourceElementType = fields[3] == NullPlaceholder ? null : Unescape(fields[3]);
                        pendingResourceRegister = DecodeRegister(fields[4], fields[5]);
                        pendingResourceLoc = new TextSpan(ParseInt(fields[6]), 0);
                    }

                    break;

                case "field":
                    if (fields.Length >= 6)
                    {
                        var field = new HlslField(
                            Unescape(fields[1]),
                            Unescape(fields[2]),
                            fields[3] == NullPlaceholder ? null : ParseInt(fields[3]),
                            fields[4] == NullPlaceholder ? null : Unescape(fields[4]),
                            new TextSpan(ParseInt(fields[5]), 0));
                        if (pendingStructName is not null) pendingStructFields.Add(field);
                        else if (pendingResourceName is not null) pendingResourceFields.Add(field);
                    }

                    break;

                case "kernel":
                    FlushStruct();
                    FlushResource();
                    FlushKernel();
                    if (fields.Length >= 6)
                    {
                        pendingKernelName = Unescape(fields[1]);
                        pendingKernelSize = new ThreadGroupSize(ParseInt(fields[2]), ParseInt(fields[3]),
                            ParseInt(fields[4]));
                        pendingKernelLoc = new TextSpan(ParseInt(fields[5]), 0);
                    }

                    break;

                case "bind":
                    if (fields.Length >= 2 && pendingKernelName is not null)
                        pendingKernelBindNames.Add(Unescape(fields[1]));
                    break;

                case "diag":
                    FlushStruct();
                    FlushResource();
                    FlushKernel();
                    if (fields.Length >= 4 && Enum.TryParse<DiagnosticSeverity>(fields[1], out var severity))
                        diagnostics.Add(new Diagnostic(severity, Unescape(fields[3]),
                            new TextSpan(ParseInt(fields[2]), 0)));
                    break;

                default:
                    // Unknown record type: ignore for forward compatibility with a newer writer.
                    break;
            }
        }

        FlushStruct();
        FlushResource();
        FlushKernel();

        if (root is null) return false;

        var result = new HlslCompilationResult(kernels, resources, diagnostics, structs);
        data = new ManifestData(root, defines, inputs, result, locations);
        return true;
    }

    private static void WriteField(StringBuilder body, HlslField field, Func<TextSpan, int> locId)
    {
        body.Append(Join("field", Escape(field.Name), Escape(field.TypeName),
            field.ArrayLength is { } len ? Inv(len) : NullPlaceholder,
            field.Semantic is null ? NullPlaceholder : Escape(field.Semantic),
            Inv(locId(field.Location)))).Append('\n');
    }

    private static (string RegisterToken, string SpaceToken) EncodeRegister(ResourceRegister? register)
    {
        if (register is not { } r) return (NullPlaceholder, NullPlaceholder);
        return ($"{r.SlotType}{Inv(r.SlotIndex)}", r.Space is { } space ? Inv(space) : NullPlaceholder);
    }

    private static ResourceRegister? DecodeRegister(string registerToken, string spaceToken)
    {
        if (registerToken == NullPlaceholder || registerToken.Length < 2) return null;
        var slotType = registerToken[0];
        var slotIndex = ParseInt(registerToken.Substring(1));
        var space = spaceToken == NullPlaceholder ? (int?)null : ParseInt(spaceToken);
        return new ResourceRegister(slotType, slotIndex, space);
    }

    private static ResourceKind ParseResourceKind(string token)
    {
        return Enum.TryParse<ResourceKind>(token, out var kind) ? kind : ResourceKind.PlainGlobal;
    }

    private static int ParseInt(string token)
    {
        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static string Inv(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Join(params string[] fields)
    {
        return string.Join("\t", fields);
    }

    private static string[] SplitLine(string line)
    {
        var trimmed = line.Length > 0 && line[line.Length - 1] == '\r' ? line.Substring(0, line.Length - 1) : line;
        return trimmed.Split('\t');
    }

    /// <summary>Escapes a real tab/newline/backslash inside a field value — the delimiter split above operates on the raw characters, so an escaped value can never be mistaken for a field boundary.</summary>
    private static string Escape(string value)
    {
        if (value.IndexOfAny(['\\', '\t', '\n', '\r']) < 0) return value;
        return value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    private static string Unescape(string value)
    {
        if (value.IndexOf('\\') < 0) return value;
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                i++;
                sb.Append(value[i] switch
                {
                    't' => '\t',
                    'n' => '\n',
                    'r' => '\r',
                    _ => value[i]
                });
            }
            else
            {
                sb.Append(value[i]);
            }
        }

        return sb.ToString();
    }
}
