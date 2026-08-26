namespace TkHLSL.SourceGeneration;

/// <summary>One AdditionalFile the generator considered a candidate HLSL source (see docs/IMPLEMENTATION_PLAN.md §9 Phase 9.2).</summary>
internal readonly record struct AdditionalHlslFile(string NormalizedPath, string Text);
