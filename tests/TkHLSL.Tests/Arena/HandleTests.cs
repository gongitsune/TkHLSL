using TkHLSL.Arena;

namespace TkHLSL.Tests.Arena;

public class HandleTests
{
    [Fact]
    public void Handles_WithSameIndex_AreEqual()
    {
        var arena = new Arena<string>();
        var handle = arena.Add("value");

        var arenaTwo = new Arena<string>();
        var otherHandle = arenaTwo.Add("value");

        Assert.Equal(handle, otherHandle);
        Assert.True(handle == otherHandle);
    }

    [Fact]
    public void Handles_WithDifferentIndex_AreNotEqual()
    {
        var arena = new Arena<string>();
        var first = arena.Add("a");
        var second = arena.Add("b");

        Assert.NotEqual(first, second);
        Assert.True(first != second);
    }

    [Fact]
    public void GetHashCode_MatchesForEqualHandles()
    {
        var arena = new Arena<string>();
        var handle = arena.Add("a");

        var arenaTwo = new Arena<string>();
        var otherHandle = arenaTwo.Add("a");

        Assert.Equal(handle.GetHashCode(), otherHandle.GetHashCode());
    }
}
