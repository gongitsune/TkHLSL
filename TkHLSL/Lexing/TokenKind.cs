namespace TkHLSL.Lexing;

/// <summary>
/// Generic lexical classification of a <see cref="Token"/>. Keyword recognition (e.g.
/// distinguishing <c>struct</c> from an ordinary identifier) is deliberately not performed here
/// and is deferred to the Syntax layer.
/// </summary>
public enum TokenKind
{
    EndOfFile,
    Identifier,
    IntLiteral,
    FloatLiteral,
    StringLiteral,
    NewLine,
    Hash,
    LineComment,
    BlockComment,

    OpenParen,
    CloseParen,
    OpenBrace,
    CloseBrace,
    OpenBracket,
    CloseBracket,
    Comma,
    Semicolon,
    Colon,
    Dot,
    Question,
    Tilde,

    Plus,
    Minus,
    Star,
    Slash,
    Percent,

    Assign,
    PlusAssign,
    MinusAssign,
    StarAssign,
    SlashAssign,
    PercentAssign,
    AmpAssign,
    PipeAssign,
    CaretAssign,

    Equal,
    NotEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,

    LeftShift,
    RightShift,
    LeftShiftAssign,
    RightShiftAssign,

    Amp,
    AmpAmp,
    Pipe,
    PipePipe,
    Caret,
    Bang,

    Increment,
    Decrement,

    /// <summary>
    /// Invalid input (unterminated string/comment, unrecognized character). Always accompanied
    /// by a matching <see cref="TkHLSL.Diagnostics.Diagnostic"/> rather than an exception.
    /// </summary>
    Error,
}
