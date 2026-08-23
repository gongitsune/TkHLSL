using TkHLSL.Text;

namespace TkHLSL.Diagnostics;

/// <summary>
/// TkHLSL's sole diagnostic representation, used across every phase (Lexer, Preprocessor,
/// Parser, public API). Deliberately independent of <c>Microsoft.CodeAnalysis.Diagnostic</c>.
/// </summary>
public readonly struct Diagnostic
{
    public Diagnostic(DiagnosticSeverity severity, string message, TextSpan span)
    {
        Severity = severity;
        Message = message;
        Span = span;
    }

    public DiagnosticSeverity Severity { get; }

    public string Message { get; }

    public TextSpan Span { get; }

    public override string ToString() => $"{Severity}: {Message} {Span}";
}
