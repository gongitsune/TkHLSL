using Tk.Hlsl.Arena;

namespace Tk.Hlsl.Tests.Arena;

public class UniqueArenaTests
{
    [Fact]
    public void Insert_ReturnsHandleThatRetrievesTheSameItem()
    {
        var arena = new UniqueArena<string>();

        var handle = arena.Insert("first");

        Assert.Equal("first", arena[handle]);
    }

    [Fact]
    public void Insert_EqualValueTwice_ReturnsSameHandleAndDoesNotDuplicate()
    {
        var arena = new UniqueArena<string>();

        var first = arena.Insert("dup");
        var second = arena.Insert("dup");

        Assert.Equal(first, second);
        Assert.Equal(1, arena.Count);
    }

    [Fact]
    public void Insert_DistinctValues_AssignsDistinctHandles()
    {
        var arena = new UniqueArena<string>();

        var a = arena.Insert("a");
        var b = arena.Insert("b");

        Assert.NotEqual(a, b);
        Assert.Equal(2, arena.Count);
    }

    [Fact]
    public void Enumeration_YieldsItemsInFirstInsertionOrder()
    {
        var arena = new UniqueArena<int>();
        arena.Insert(1);
        arena.Insert(2);
        arena.Insert(1);

        Assert.Equal([1, 2], arena.ToList());
    }
}
