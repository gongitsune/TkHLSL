using TkHLSL.Text;

namespace TkHLSL.Tests.Text;

public class SourceTextTests
{
    [Fact]
    public void FromRoot_HasSingleSegmentSpanningWholeText()
    {
        var source = SourceText.FromRoot("int x;");

        var segment = Assert.Single(source.Segments);
        Assert.Equal(0, segment.Start);
        Assert.Equal("int x;".Length, segment.Length);
        Assert.Same("int x;", source.Text);
    }

    [Fact]
    public void TryGetLocation_OffsetAtSegmentStart_ResolvesToThatSegment()
    {
        var source = new SourceText("root\nincluded\n", [
            new SourceSegment("", 0, 4), // "root"
            new SourceSegment("Common.cginc", 5, 8), // "included"
        ]);

        Assert.True(source.TryGetLocation(5, out var segment, out var offsetInFile));
        Assert.Equal("Common.cginc", segment.Path);
        Assert.Equal(0, offsetInFile);
    }

    [Fact]
    public void TryGetLocation_OffsetAtLastSegmentEnd_ResolvesToPrecedingCharacter()
    {
        var source = new SourceText("root\nincluded\n", [
            new SourceSegment("", 0, 4),
            new SourceSegment("Common.cginc", 5, 8),
        ]);

        Assert.True(source.TryGetLocation(12, out var segment, out var offsetInFile));
        Assert.Equal("Common.cginc", segment.Path);
        Assert.Equal(7, offsetInFile);
    }

    [Fact]
    public void TryGetLocation_OffsetPastEnd_ReturnsFalse()
    {
        var source = SourceText.FromRoot("int x;");

        Assert.False(source.TryGetLocation(source.Text.Length, out _, out _));
    }

    [Fact]
    public void TryGetLocation_OffsetOnSeparatorBetweenSegments_ReturnsFalse()
    {
        var source = new SourceText("root\nincluded\n", [
            new SourceSegment("", 0, 4), // ends at 4, separator '\n' is at index 4
            new SourceSegment("Common.cginc", 5, 8),
        ]);

        Assert.False(source.TryGetLocation(4, out _, out _));
    }

    [Fact]
    public void Slice_ReturnsExpectedSubstring()
    {
        var source = SourceText.FromRoot("int x;");

        Assert.Equal("x", source.Slice(new TextSpan(4, 1)).ToString());
    }
}
