using TkHLSL.Preprocessing;

namespace TkHLSL.Tests.Golden;

/// <summary>
///     Resolves <c>#include</c> targets used by the golden corpus (see docs/IMPLEMENTATION_PLAN.md §9
///     Phase 6) against <c>Fixtures/Includes/</c> on disk. <see cref="TryResolve" /> returns a
///     machine-independent <c>resolvedPath</c> (a forward-slash relative path, never the absolute disk
///     path) so snapshots stay stable across checkouts.
/// </summary>
internal sealed class FixtureIncludeResolver(string includesDirectory) : IIncludeResolver
{
    public bool TryResolve(string requestedPath, string? includerPath, out string? resolvedPath, out string? content)
    {
        var normalized = requestedPath.Replace('\\', '/').TrimStart('/');
        var fullPath = Path.Combine(includesDirectory, normalized.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(fullPath))
        {
            resolvedPath = null;
            content = null;
            return false;
        }

        resolvedPath = "Includes/" + normalized;
        content = File.ReadAllText(fullPath);
        return true;
    }
}
