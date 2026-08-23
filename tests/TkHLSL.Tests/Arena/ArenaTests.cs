using TkHLSL.Arena;

namespace TkHLSL.Tests.Arena;

public class ArenaTests
{
    [Fact]
    public void Add_ReturnsHandleThatRetrievesTheSameItem()
    {
        var arena = new Arena<string>();

        var handle = arena.Add("first");

        Assert.Equal("first", arena[handle]);
    }

    [Fact]
    public void Add_MultipleItems_AssignsSequentialHandles()
    {
        var arena = new Arena<string>();

        var first = arena.Add("a");
        var second = arena.Add("b");
        var third = arena.Add("c");

        Assert.Equal("a", arena[first]);
        Assert.Equal("b", arena[second]);
        Assert.Equal("c", arena[third]);
        Assert.Equal(0, first.Index);
        Assert.Equal(1, second.Index);
        Assert.Equal(2, third.Index);
    }

    [Fact]
    public void Count_ReflectsNumberOfAddedItems()
    {
        var arena = new Arena<int>();

        arena.Add(1);
        arena.Add(2);

        Assert.Equal(2, arena.Count);
    }

    [Fact]
    public void Enumeration_YieldsItemsInInsertionOrder()
    {
        var arena = new Arena<int>();
        arena.Add(1);
        arena.Add(2);
        arena.Add(3);

        Assert.Equal([1, 2, 3], arena.ToList());
    }
}
