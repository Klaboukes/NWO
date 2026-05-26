using Godot;
using NWO.Core;
using Xunit;

namespace NWO.Tests;

public class EndTurnQueueTests
{
    // Minimal stub IEndTurnItem for testing prune behavior.
    private class StubItem : IEndTurnItem
    {
        public bool     NeedsAttention { get; set; } = true;
        public string   PromptText     => "stub";
        public Vector2I FocusPosition  => Vector2I.Zero;
    }

    [Fact]
    public void PeekValid_EmptyQueue_ReturnsNull()
    {
        var q = new EndTurnQueue();
        Assert.Null(q.PeekValid());
    }

    [Fact]
    public void PeekValid_AllValid_ReturnsFirst()
    {
        var q = new EndTurnQueue();
        var a = new StubItem();
        var b = new StubItem();
        q.Add(a); q.Add(b);
        Assert.Same(a, q.PeekValid());
        Assert.Equal(2, q.Count);
    }

    [Fact]
    public void PeekValid_PrunesInvalidFromHead()
    {
        var q = new EndTurnQueue();
        var skipped = new StubItem { NeedsAttention = false };
        var valid   = new StubItem();
        q.Add(skipped); q.Add(valid);
        Assert.Same(valid, q.PeekValid());
        Assert.Equal(1, q.Count); // skipped item removed
    }

    [Fact]
    public void PeekValid_AllInvalid_ReturnsNull()
    {
        var q = new EndTurnQueue();
        q.Add(new StubItem { NeedsAttention = false });
        q.Add(new StubItem { NeedsAttention = false });
        Assert.Null(q.PeekValid());
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void Pop_RemovesHead()
    {
        var q = new EndTurnQueue();
        var a = new StubItem();
        var b = new StubItem();
        q.Add(a); q.Add(b);
        q.Pop();
        Assert.Same(b, q.PeekValid());
    }

    [Fact]
    public void Pop_OnEmpty_NoOps()
    {
        var q = new EndTurnQueue();
        q.Pop();
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void Clear_EmptiesQueue()
    {
        var q = new EndTurnQueue();
        q.Add(new StubItem());
        q.Add(new StubItem());
        q.Clear();
        Assert.Equal(0, q.Count);
    }
}
