using ConsoleForge.Core;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Widgets;

/// <summary>Unit tests for <see cref="Checkbox"/>.</summary>
public class CheckboxTests
{
    // ── Constructor ───────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultState_IsUnchecked()
    {
        var cb = new Checkbox();
        Assert.False(cb.IsChecked);
    }

    [Fact]
    public void Constructor_SetsLabel()
    {
        var cb = new Checkbox("Enable feature");
        Assert.Equal("Enable feature", cb.Label);
    }

    [Fact]
    public void Constructor_SetsCheckedState()
    {
        var cb = new Checkbox(isChecked: true);
        Assert.True(cb.IsChecked);
    }

    // ── Toggle ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ConsoleKey.Spacebar)]
    [InlineData(ConsoleKey.Enter)]
    public void Update_SpaceOrEnter_Unchecked_DispatchesToggledTrue(ConsoleKey key)
    {
        var cb = new Checkbox("test", isChecked: false);
        var (next, _) = cb.Update(new KeyMsg(key, null));
        var result = (Checkbox)next;

        Assert.True(result.IsChecked);
    }

    [Theory]
    [InlineData(ConsoleKey.Spacebar)]
    [InlineData(ConsoleKey.Enter)]
    public void Update_SpaceOrEnter_Checked_DispatchesToggledFalse(ConsoleKey key)
    {
        var cb = new Checkbox("test", isChecked: true);
        var (next, _) = cb.Update(new KeyMsg(key, null));
        var result = (Checkbox)next;

        Assert.False(result.IsChecked);
    }

    [Fact]
    public void Update_OtherKey_DispatchesNothing()
    {
        var cb = new Checkbox("test");
        var (next, _) = cb.Update(new KeyMsg(ConsoleKey.Tab, null));
        Assert.Same(cb, next);
    }

    // ── Render ────────────────────────────────────────────────────────────────

    [Fact]
    public void Render_Unchecked_ShowsEmptyBrackets()
    {
        var cb = new Checkbox("my option", isChecked: false);
        var descriptor = ViewDescriptor.From(cb, width: 30, height: 1);
        var plain = TestHelpers.StripAnsi(descriptor.Content);
        Assert.Contains("[ ]", plain);
        Assert.Contains("my option", plain);
    }

    [Fact]
    public void Render_Checked_ShowsCheckmark()
    {
        var cb = new Checkbox("my option", isChecked: true);
        var descriptor = ViewDescriptor.From(cb, width: 30, height: 1);
        var plain = TestHelpers.StripAnsi(descriptor.Content);
        Assert.Contains("[✓]", plain);
        Assert.Contains("my option", plain);
    }

    [Fact]
    public void Render_CustomChars_Used()
    {
        var cb = new Checkbox("opt", isChecked: true, checkedChar: 'X', uncheckedChar: '-');
        var descriptor = ViewDescriptor.From(cb, width: 20, height: 1);
        var plain = TestHelpers.StripAnsi(descriptor.Content);
        Assert.Contains("[X]", plain);
    }

    [Fact]
    public void Render_Truncates_WhenWidthSmall()
    {
        var cb = new Checkbox("long label that exceeds width");
        var descriptor = ViewDescriptor.From(cb, width: 6, height: 1);
        // Must not throw; output ≤ 6 chars
        var plain = TestHelpers.StripAnsi(descriptor.Content);
        Assert.True(plain.TrimEnd().Length <= 6);
    }

    [Fact]
    public void Render_ZeroWidth_DoesNotThrow()
    {
        var cb = new Checkbox("test");
        var ex = Record.Exception(() => ViewDescriptor.From(cb, width: 0, height: 1));
        Assert.Null(ex);
    }
}