using ConsoleForge.Styling;

namespace ConsoleForge.Tests.Styling;

/// <summary>Unit tests for <see cref="Style"/>.</summary>
public class StyleTests
{
    // ── Default ───────────────────────────────────────────────────────────────

    [Fact]
    public void Default_IsValueEqualToDefault()
    {
        Assert.Equal(Style.Default, default(Style));
    }

    [Fact]
    public void Default_Render_ReturnsTextUnchanged()
    {
        var text = "hello";
        Assert.Equal(text, Style.Default.Render(text));
    }

    // ── Equality ──────────────────────────────────────────────────────────────

    [Fact]
    public void Equality_SameProperties_AreEqual()
    {
        var a = Style.Default.Foreground(Color.Red).Bold(true);
        var b = Style.Default.Foreground(Color.Red).Bold(true);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentForeground_NotEqual()
    {
        var a = Style.Default.Foreground(Color.Red);
        var b = Style.Default.Foreground(Color.Blue);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_Operator_Works()
    {
        var a = Style.Default.Bold(true);
        var b = Style.Default.Bold(true);
        Assert.True(a == b);
        Assert.False(a != b);
    }

    // ── Foreground / Background ───────────────────────────────────────────────

    [Fact]
    public void Foreground_ProducesAnsiWithEscapeCode()
    {
        var style = Style.Default.Foreground(Color.Red);
        var rendered = style.Render("x", ColorProfile.Ansi);
        Assert.Contains("\x1b[", rendered);
        Assert.Contains("x", rendered);
    }

    [Fact]
    public void Background_ProducesAnsiWithEscapeCode()
    {
        var style = Style.Default.Background(Color.Blue);
        var rendered = style.Render("x", ColorProfile.Ansi);
        Assert.Contains("\x1b[", rendered);
    }

    [Fact]
    public void NoColor_Profile_ReturnsTextWithoutEscapes()
    {
        var style = Style.Default.Foreground(Color.Red).Bold(true);
        var rendered = style.Render("hello", ColorProfile.NoColor);
        // In NoColor mode, no ANSI color codes (bold/dim still allowed but colors stripped)
        Assert.DoesNotContain("\x1b[3", rendered); // no color sequences (38m etc.)
        Assert.Contains("hello", rendered);
    }

    // ── Bold ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Bold_True_Render_Contains_BoldCode()
    {
        var style = Style.Default.Bold(true);
        var rendered = style.Render("text", ColorProfile.TrueColor);
        Assert.Contains("\x1b[1m", rendered);
    }

    [Fact]
    public void Bold_False_AfterTrue_NoBoldCode()
    {
        var style = Style.Default.Bold(true).Bold(false);
        var rendered = style.Render("text", ColorProfile.TrueColor);
        Assert.DoesNotContain("\x1b[1m", rendered);
    }

    // ── Italic ────────────────────────────────────────────────────────────────

    [Fact]
    public void Italic_True_Render_Contains_ItalicCode()
    {
        var style = Style.Default.Italic(true);
        var rendered = style.Render("text", ColorProfile.TrueColor);
        Assert.Contains("\x1b[3m", rendered);
    }

    // ── Underline ─────────────────────────────────────────────────────────────

    [Fact]
    public void Underline_True_Render_Contains_UnderlineCode()
    {
        var style = Style.Default.Underline(true);
        var rendered = style.Render("text", ColorProfile.TrueColor);
        Assert.Contains("\x1b[4m", rendered);
    }

    // ── Reverse ───────────────────────────────────────────────────────────────

    [Fact]
    public void Reverse_True_Render_Contains_ReverseCode()
    {
        var style = Style.Default.Reverse(true);
        var rendered = style.Render("text", ColorProfile.TrueColor);
        Assert.Contains("\x1b[7m", rendered);
    }

    // ── Faint / Blink / Strikethrough ────────────────────────────────────────

    [Fact]
    public void Faint_True_Render_Contains_FaintCode()
    {
        var style = Style.Default.Faint(true);
        var rendered = style.Render("x", ColorProfile.TrueColor);
        Assert.Contains("\x1b[2m", rendered);
    }

    [Fact]
    public void Strikethrough_True_Render_Contains_StrikeCode()
    {
        var style = Style.Default.Strikethrough(true);
        var rendered = style.Render("x", ColorProfile.TrueColor);
        Assert.Contains("\x1b[9m", rendered);
    }

    // ── Inherit ───────────────────────────────────────────────────────────────

    [Fact]
    public void Inherit_DefaultChild_CopiesAllParentProperties()
    {
        // Only bold — no fg — so SGR is a simple ESC[1m not a combined sequence
        var parent = Style.Default.Bold(true);
        var child  = Style.Default.Inherit(parent);
        var rendered = child.Render("x", ColorProfile.TrueColor);
        Assert.Contains("\x1b[1m", rendered);
    }

    [Fact]
    public void Inherit_ChildHasFg_ChildFgWins()
    {
        var parent = Style.Default.Foreground(Color.Green);
        var child  = Style.Default.Foreground(Color.Red);

        var merged = child.Inherit(parent);

        // child's red foreground must survive
        var fromChild  = child.Render("x", ColorProfile.Ansi);
        var fromMerged = merged.Render("x", ColorProfile.Ansi);
        Assert.Equal(fromChild, fromMerged);
    }

    [Fact]
    public void Inherit_DefaultParent_ReturnsChildUnchanged()
    {
        var child = Style.Default.Bold(true).Italic(true);
        var merged = child.Inherit(Style.Default);
        Assert.Equal(child, merged);
    }

    [Fact]
    public void Inherit_MarginAndPadding_NotInherited()
    {
        // Margins/padding are local, not inherited
        var parent = Style.Default.Padding(2).Margin(3);
        var child  = Style.Default;
        var merged = child.Inherit(parent);

        // merged == child (padding/margin not copied)
        Assert.Equal(child, merged);
    }

    // ── Unset ─────────────────────────────────────────────────────────────────

    [Fact]
    public void UnsetForeground_ClearsColor()
    {
        var style = Style.Default.Foreground(Color.Red).UnsetForeground();
        Assert.Equal(Style.Default, style);
    }

    [Fact]
    public void UnsetBold_ClearsBold()
    {
        var style = Style.Default.Bold(true).UnsetBold();
        var rendered = style.Render("x", ColorProfile.TrueColor);
        Assert.DoesNotContain("\x1b[1m", rendered);
    }

    // ── RenderChar hot path ───────────────────────────────────────────────────

    [Fact]
    public void RenderChar_Default_ReturnsSingleCharString()
    {
        var result = Style.Default.RenderChar('A');
        Assert.Equal("A", result);
    }

    [Fact]
    public void RenderChar_Styled_ContainsChar()
    {
        var style = Style.Default.Bold(true);
        var result = style.RenderChar('Z', ColorProfile.TrueColor);
        Assert.Contains("Z", result);
        Assert.Contains("\x1b[", result);
    }

    [Fact]
    public void RenderChar_CalledTwice_SameResult()
    {
        // Verifies cache consistency
        var style = Style.Default.Foreground(Color.Cyan);
        var r1 = style.RenderChar('x', ColorProfile.TrueColor);
        var r2 = style.RenderChar('x', ColorProfile.TrueColor);
        Assert.Equal(r1, r2);
    }

    // ── Alignment ─────────────────────────────────────────────────────────────

    [Fact]
    public void Align_Center_PadsTextEvenly()
    {
        var style = Style.Default.Width(10).Align(HorizontalAlign.Center);
        var rendered = style.Render("ab");
        Assert.Equal(10, rendered.Length);
        Assert.StartsWith("    ", rendered); // 4 spaces on left for "ab" in 10
    }

    [Fact]
    public void Align_Right_PadsLeft()
    {
        var style = Style.Default.Width(10).Align(HorizontalAlign.Right);
        var rendered = style.Render("ab");
        Assert.Equal(10, rendered.Length);
        Assert.EndsWith("ab", rendered);
    }

    [Fact]
    public void Align_Left_PadsRight()
    {
        var style = Style.Default.Width(10).Align(HorizontalAlign.Left);
        var rendered = style.Render("ab");
        Assert.Equal(10, rendered.Length);
        Assert.StartsWith("ab", rendered);
    }

    // ── Reset sequence ────────────────────────────────────────────────────────

    [Fact]
    public void Render_StyledText_EndsWithResetSequence()
    {
        var style = Style.Default.Bold(true);
        var rendered = style.Render("hello", ColorProfile.TrueColor);
        Assert.EndsWith("\x1b[0m", rendered);
    }
}
