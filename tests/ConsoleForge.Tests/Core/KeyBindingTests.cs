using ConsoleForge.Core;

namespace ConsoleForge.Tests.Core;

public class KeyBindingTests
{
    private sealed record QuitMsg : IMsg;
    private sealed record SaveMsg : IMsg;

    private static readonly KeyBinding Quit =
        new("q", "quit", KeyPattern.OfChar('q'), KeyPattern.WithCtrl(ConsoleKey.C));

    private static readonly KeyBinding Save = new("ctrl+s", "save", KeyPattern.WithCtrl(ConsoleKey.S));

    [Fact]
    public void Matches_AnyOfItsPatterns()
    {
        Assert.True(Quit.Matches(new KeyMsg(ConsoleKey.Q, 'q')));
        Assert.True(Quit.Matches(new KeyMsg(ConsoleKey.C, '\x03', Ctrl: true)));
        Assert.False(Quit.Matches(new KeyMsg(ConsoleKey.W, 'w')));
    }

    [Fact]
    public void Disabled_MatchesNothing()
    {
        Assert.False((Quit with { Enabled = false }).Matches(new KeyMsg(ConsoleKey.Q, 'q')));
    }

    [Fact]
    public void KeyMap_HandlesEveryPattern_AndListsTheBinding()
    {
        var map = new KeyMap().On(Quit, () => new QuitMsg());

        Assert.IsType<QuitMsg>(map.Handle(new KeyMsg(ConsoleKey.Q, 'q')));
        Assert.IsType<QuitMsg>(map.Handle(new KeyMsg(ConsoleKey.C, '\x03', Ctrl: true)));
        Assert.Equal<KeyBinding>([Quit], map.Bindings);
    }

    [Fact]
    public void KeyMap_SkipsDisabledBinding_ForInputAndHelp()
    {
        // The same map built for a state where there is nothing to save.
        var map = new KeyMap()
            .On(Quit, () => new QuitMsg())
            .On(Save with { Enabled = false }, () => new SaveMsg());

        Assert.Null(map.Handle(new KeyMsg(ConsoleKey.S, '\x13', Ctrl: true)));
        Assert.Equal<KeyBinding>([Quit], map.Bindings);
    }

    [Fact]
    public void KeyMap_BindingsLeaveOutPlainKeys()
    {
        // A bare key carries no help text, so it has nothing to show.
        var map = new KeyMap()
            .On(ConsoleKey.Escape, () => new QuitMsg())
            .On(Save, _ => new SaveMsg());

        Assert.Equal<KeyBinding>([Save], map.Bindings);
        Assert.Equal(2, map.KeyBindingCount);
    }

    [Fact]
    public void Merge_KeepsBindingsInPriorityOrder()
    {
        var merged = new KeyMap().On(Save, () => new SaveMsg()).Merge(new KeyMap().On(Quit, () => new QuitMsg()));
        Assert.Equal<KeyBinding>([Save, Quit], merged.Bindings);
    }
}
