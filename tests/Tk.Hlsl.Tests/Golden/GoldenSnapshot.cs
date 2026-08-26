using System.Text;
using Tk.Hlsl.Ir;
using Tk.Hlsl.Model;

namespace Tk.Hlsl.Tests.Golden;

/// <summary>
///     Renders an <see cref="HlslCompilationResult" /> into a deterministic, human-reviewable text form
///     for the Phase 6 golden-corpus tests (see docs/IMPLEMENTATION_PLAN.md §9 Phase 6, §11).
/// </summary>
internal static class GoldenSnapshot
{
    public static string Render(HlslCompilationResult result)
    {
        var sb = new StringBuilder();

        if (result.Kernels.Count == 0)
        {
            sb.Append("(no kernels)\n");
        }
        else
        {
            foreach (var kernel in result.Kernels)
            {
                sb.Append("kernel ").Append(kernel.Name).Append(' ').Append(kernel.ThreadGroupSize).Append('\n');
                if (kernel.Bindings.Count == 0)
                    sb.Append("  (no bindings)\n");
                else
                    foreach (var binding in kernel.Bindings)
                        AppendBinding(sb, binding, "  ");
            }
        }

        var unused = result.AllResources.Where(r => !result.Kernels.Any(k => k.Bindings.Contains(r))).ToArray();
        if (unused.Length > 0)
        {
            sb.Append("unused:\n");
            foreach (var resource in unused)
                AppendBinding(sb, resource, "  ");
        }

        if (result.Diagnostics.Count == 0)
        {
            sb.Append("diagnostics: (none)\n");
        }
        else
        {
            sb.Append("diagnostics:\n");
            foreach (var diagnostic in result.Diagnostics)
            {
                var location = result.Source.TryGetLocation(diagnostic.Span.Start, out var segment, out var offset)
                    ? $"{(segment.Path.Length == 0 ? "<root>" : segment.Path)}:{offset}"
                    : $"composite:{diagnostic.Span.Start}";
                sb.Append("  ").Append(diagnostic.Severity).Append(": ").Append(diagnostic.Message)
                    .Append(" @ ").Append(location).Append('\n');
            }
        }

        return sb.ToString().Replace("\r\n", "\n");
    }

    private static void AppendBinding(StringBuilder sb, ResourceBinding binding, string indent)
    {
        sb.Append(indent).Append(binding.ResourceKind);
        if (binding.ElementTypeName is { } elementType)
            sb.Append('<').Append(elementType).Append('>');
        sb.Append(' ').Append(binding.Name);
        if (binding.ExplicitRegister is { } register)
            sb.Append(" : ").Append(register);
        sb.Append('\n');
    }
}
