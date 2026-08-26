using TkHLSL.Text;

namespace TkHLSL.Diagnostics;

/// <summary>
/// TkHLSL's sole diagnostic representation, used across every phase (Lexer, Preprocessor,
/// Parser, public API). Deliberately independent of <c>Microsoft.CodeAnalysis.Diagnostic</c>.
/// </summary>
public readonly struct Diagnostic(DiagnosticSeverity severity, string message, TextSpan span)
{
    public DiagnosticSeverity Severity { get; } = severity;

    public string Message { get; } = message;

    public TextSpan Span { get; } = span;

    public override string ToString() => $"{Severity}: {Message} {Span}";
}
