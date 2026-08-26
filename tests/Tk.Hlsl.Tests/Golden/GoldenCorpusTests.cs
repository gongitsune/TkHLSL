using System.Runtime.CompilerServices;
using Tk.Hlsl.Preprocessing;

namespace Tk.Hlsl.Tests.Golden;

/// <summary>
///     Phase 6 regression suite (see docs/IMPLEMENTATION_PLAN.md §9 Phase 6, §11): runs
///     <see cref="HlslParser.Parse" /> over every fixture in <c>Fixtures/*.compute</c> and compares the
///     <see cref="GoldenSnapshot.Render" /> output against a committed <c>Fixtures/&lt;name&gt;.expected.txt</c>.
/// </summary>
/// <remarks>
///     Set <c>TKHLSL_UPDATE_SNAPSHOTS=1</c> to (re)write every expected file from the current parser
///     output instead of comparing — run it once, then review the diff before committing.
/// </remarks>
public class GoldenCorpusTests
{
    private static readonly string FixturesDirectory = ResolveFixturesDirectory();

    public static IEnumerable<object[]> Fixtures()
    {
        return Directory.EnumerateFiles(FixturesDirectory, "*.compute")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Select(path => new object[] { Path.GetFileName(path) });
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Fixture_MatchesGoldenSnapshot(string fileName)
    {
        var sourcePath = Path.Combine(FixturesDirectory, fileName);
        var source = File.ReadAllText(sourcePath);
        var definedSymbols = ReadDefinedSymbols(source);

        var options = new HlslParseOptions(
            definedSymbols,
            new FixtureIncludeResolver(Path.Combine(FixturesDirectory, "Includes")),
            fileName);

        var result = HlslParser.Parse(source, options);
        var actual = GoldenSnapshot.Render(result);

        var expectedPath = Path.Combine(FixturesDirectory, Path.GetFileNameWithoutExtension(fileName) + ".expected.txt");

        if (Environment.GetEnvironmentVariable("TKHLSL_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllText(expectedPath, actual);
            return;
        }

        Assert.True(File.Exists(expectedPath),
            $"Missing golden file '{expectedPath}'. Run with TKHLSL_UPDATE_SNAPSHOTS=1 to generate it, " +
            "then review the output before committing.");

        var expected = File.ReadAllText(expectedPath).Replace("\r\n", "\n");
        Assert.Equal(expected, actual);
    }

    /// <summary>
    ///     Reads any leading <c>//! define: NAME</c> lines from the fixture, one symbol per line, stopping
    ///     at the first line that doesn't match.
    /// </summary>
    private static string[] ReadDefinedSymbols(string source)
    {
        var symbols = new List<string>();
        using var reader = new StringReader(source);
        while (reader.ReadLine() is { } line)
        {
            const string prefix = "//! define:";
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) break;
            symbols.Add(line[prefix.Length..].Trim());
        }

        return [.. symbols];
    }

    private static string ResolveFixturesDirectory([CallerFilePath] string here = "")
    {
        return Path.Combine(Path.GetDirectoryName(here)!, "..", "Fixtures");
    }
}
