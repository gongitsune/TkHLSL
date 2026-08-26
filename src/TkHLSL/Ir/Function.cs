using TkHLSL.Text;

namespace TkHLSL.Ir;

/// <summary>
/// A helper function reachable (directly or indirectly) from a kernel. Its body is recorded only
/// as a <see cref="TokenRange"/> — Phase 3 skips over it (brace-depth counting) without parsing
/// statements or expressions; the Phase 4 Analyzer scans <see cref="BodyTokenRange"/> directly
/// against the original token list (see docs/IMPLEMENTATION_PLAN.md §9 Phase 3/4).
/// </summary>
public sealed record Function(string Name, TokenRange BodyTokenRange, TextSpan Location);
