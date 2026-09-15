using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Core;

/// <summary>Unit tests for <see cref="FocusManager"/>.</summary>
public class FocusManagerTests
{
    // ── CollectFocusable ──────────────────────────────────────────────────────

    [Fact]
    public void CollectFocusable_NonFocusableRoot_ReturnsEmpty()
    {
        var widget = new TextBlock("hello");
        var result = FocusManager.CollectFocusable(widget);
        Assert.Empty(result);
    }

    [Fact]
    public void CollectFocusable_SingleFocusable_ReturnsSingle()
    {
        var input = new TextInput("text");
        var result = FocusManager.CollectFocusable(input);
        Assert.Single(result);
        Assert.Same(input, result[0]);
    }

    [Fact]
    public void CollectFocusable_ContainerWithFocusableChildren_ReturnsAll()
    {
        var a = new TextInput("a");
        var b = new TextInput("b");
        var c = new TextInput("c");
        var root = new Container(Axis.Vertical, [a, b, c]);

        var result = FocusManager.CollectFocusable(root);

        Assert.Equal(3, result.Count);
        Assert.Same(a, result[0]);
        Assert.Same(b, result[1]);
        Assert.Same(c, result[2]);
    }

    [Fact]
    public void CollectFocusable_MixedWidgets_SkipsNonFocusable()
    {
        var label = new TextBlock("label");
        var input = new TextInput("value");
        var list = new List(["a", "b"]);
        var root = new Container(Axis.Vertical, [label, input, list]);

        var result = FocusManager.CollectFocusable(root);

        Assert.Equal(2, result.Count);
        Assert.Same(input, result[0]);
        Assert.Same(list, result[1]);
    }

    [Fact]
    public void CollectFocusable_NestedContainers_DepthFirst()
    {
        var a = new TextInput("a");
        var b = new TextInput("b");
        var c = new TextInput("c");

        var inner = new Container(Axis.Vertical, [b, c]);
        var root = new Container(Axis.Horizontal, [a, inner]);

        var result = FocusManager.CollectFocusable(root);

        // depth-first: a, then inner's children b, c
        Assert.Equal(3, result.Count);
        Assert.Same(a, result[0]);
        Assert.Same(b, result[1]);
        Assert.Same(c, result[2]);
    }

    [Fact]
    public void CollectFocusable_BorderBoxWithFocusableBody_IncludesBody()
    {
        var input = new TextInput("inside");
        var box = new BorderBox(body: input);

        var result = FocusManager.CollectFocusable(box);

        Assert.Single(result);
        Assert.Same(input, result[0]);
    }

    [Fact]
    public void CollectFocusable_BorderBoxWithContainer_IncludesAllChildren()
    {
        var a = new TextInput("a");
        var b = new TextInput("b");
        var box = new BorderBox(body: new Container(Axis.Vertical, [a, b]));

        var result = FocusManager.CollectFocusable(box);

        Assert.Equal(2, result.Count);
        Assert.Same(a, result[0]);
        Assert.Same(b, result[1]);
    }

    // ── GetNext ───────────────────────────────────────────────────────────────

    [Fact]
    public void GetNext_EmptyList_ReturnsNull()
    {
        var result = FocusManager.GetNext(null, []);
        Assert.Null(result);
    }

    [Fact]
    public void GetNext_NullCurrent_ReturnsFirst()
    {
        var result = FocusManager.GetNext(null, ["a", "b"]);
        Assert.Equal("a", result);
    }

    [Fact]
    public void GetNext_FromFirst_ReturnsSecond()
    {
        var result = FocusManager.GetNext("a", ["a", "b"]);
        Assert.Equal("b", result);
    }

    [Fact]
    public void GetNext_FromLast_WrapsToFirst()
    {
        var result = FocusManager.GetNext("b", ["a", "b"]);
        Assert.Equal("a", result);
    }

    [Fact]
    public void GetNext_UnknownCurrent_ReturnsFirst()
    {
        // The not-found branch: IndexOf misses, Step falls back to the first key.
        // A broken IndexOf sends every lookup down this path, so it has to be
        // distinguishable from the found case.
        var result = FocusManager.GetNext("zzz", ["a", "b"]);
        Assert.Equal("a", result);
    }

    // ── GetPrev ───────────────────────────────────────────────────────────────

    [Fact]
    public void GetPrev_EmptyList_ReturnsNull()
    {
        var result = FocusManager.GetPrev(null, []);
        Assert.Null(result);
    }

    [Fact]
    public void GetPrev_NullCurrent_ReturnsLast()
    {
        var result = FocusManager.GetPrev(null, ["a", "b"]);
        Assert.Equal("b", result);
    }

    [Fact]
    public void GetPrev_FromFirst_WrapsToLast()
    {
        var result = FocusManager.GetPrev("a", ["a", "b"]);
        Assert.Equal("b", result);
    }

    [Fact]
    public void GetPrev_FromLast_ReturnsPrev()
    {
        var result = FocusManager.GetPrev("b", ["a", "b"]);
        Assert.Equal("a", result);
    }

    [Fact]
    public void GetPrev_UnknownCurrent_ReturnsLast()
    {
        var result = FocusManager.GetPrev("zzz", ["a", "b"]);
        Assert.Equal("b", result);
    }

    // ── Single-element list ───────────────────────────────────────────────────

    [Fact]
    public void GetNext_SingleItem_ReturnsSelf()
    {
        var result = FocusManager.GetNext("a", ["a"]);
        Assert.Equal("a", result);
    }

    [Fact]
    public void GetPrev_SingleItem_ReturnsSelf()
    {
        var result = FocusManager.GetPrev("a", ["a"]);
        Assert.Equal("a", result);
    }

    // ── CollectFocusKeys ──────────────────────────────────────────────────────

    [Fact]
    public void CollectFocusKeys_SkipWidgetsWithoutAKey()
    {
        var label = new TextBlock("label");
        var keyed = new TextInput("value") { FocusKey = "input" };
        var unkeyed = new List(["a", "b"]);          // focusable, deliberately no key
        var root = new Container(Axis.Vertical, [label, keyed, unkeyed]);

        Assert.Equal<string>(["input"], FocusManager.CollectFocusKeys(root));
    }

    [Fact]
    public void CollectFocusKeys_ReturnsKeysInDepthFirstOrder()
    {
        var inner = new Container(Axis.Vertical, [
            new TextInput("b") { FocusKey = "b" },
            new TextInput("c") { FocusKey = "c" }]);
        var root = new Container(Axis.Vertical, [
            new TextInput("a") { FocusKey = "a" }, inner]);

        Assert.Equal<string>(["a", "b", "c"], FocusManager.CollectFocusKeys(root));
    }
}