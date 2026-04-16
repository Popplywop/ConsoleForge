using ConsoleForge.Styling;

namespace ConsoleForge.Tests.Styling;

/// <summary>Unit tests for <see cref="ThemeExtensions"/>.</summary>
public class ThemeExtensionsTests
{
    [Fact]
    public void Bg_DarkTheme_ReturnsNonNull()
    {
        Assert.NotNull(Theme.Dark.Bg());
    }

    [Fact]
    public void Bg_DefaultTheme_ReturnsNull()
    {
        Assert.Null(Theme.Default.Bg());
    }

    [Fact]
    public void Fg_DarkTheme_ReturnsNonNull()
    {
        Assert.NotNull(Theme.Dark.Fg());
    }

    [Fact]
    public void Accent_DarkTheme_ReturnsNonNull()
    {
        Assert.NotNull(Theme.Dark.Accent());
    }

    [Fact]
    public void FocusColor_DarkTheme_ReturnsNonNull()
    {
        Assert.NotNull(Theme.Dark.FocusColor());
    }

    [Fact]
    public void AccentStyle_DarkTheme_HasForeground()
    {
        var s = Theme.Dark.AccentStyle();
        Assert.NotNull(s.Fg);
    }

    [Fact]
    public void MutedStyle_DarkTheme_HasForeground()
    {
        var s = Theme.Dark.MutedStyle();
        Assert.NotNull(s.Fg);
    }

    [Fact]
    public void Success_DarkTheme_HasForeground()
    {
        Assert.NotNull(Theme.Dark.Success().Fg);
    }

    [Fact]
    public void Warning_DarkTheme_HasForeground()
    {
        Assert.NotNull(Theme.Dark.Warning().Fg);
    }

    [Fact]
    public void Error_DarkTheme_HasForeground()
    {
        Assert.NotNull(Theme.Dark.Error().Fg);
    }

    [Fact]
    public void GetStyle_ExistingKey_ReturnsNamedStyle()
    {
        var s = Theme.Dracula.GetStyle("accent");
        Assert.NotNull(s.Fg);
    }

    [Fact]
    public void GetStyle_MissingKey_ReturnsFallback()
    {
        var fb = Style.Default.Bold(true);
        var s  = Theme.Dark.GetStyle("nonexistent", fb);
        Assert.Equal(fb, s);
    }

    [Fact]
    public void BgStyle_DarkTheme_HasBackground()
    {
        var s = Theme.Dark.BgStyle();
        Assert.NotNull(s.Bg);
    }

    [Fact]
    public void BgStyle_DefaultTheme_IsDefault()
    {
        Assert.Equal(Style.Default, Theme.Default.BgStyle());
    }

    [Fact]
    public void BaseColorStyle_DarkTheme_HasBothColors()
    {
        var s = Theme.Dark.BaseColorStyle();
        Assert.NotNull(s.Fg);
        Assert.NotNull(s.Bg);
    }

    [Fact]
    public void SecondaryStyle_DarkTheme_HasForeground()
    {
        Assert.NotNull(Theme.Dark.SecondaryStyle().Fg);
    }

    // ── All built-in themes have the expected Named slots ────────────────

    [Theory]
    [InlineData("accent")]
    [InlineData("secondary")]
    [InlineData("success")]
    [InlineData("warning")]
    [InlineData("error")]
    [InlineData("muted")]
    public void BuiltInThemes_HaveAllNamedSlots(string key)
    {
        foreach (var theme in Theme.BuiltIn)
        {
            Assert.True(theme.Named.ContainsKey(key),
                $"Theme '{theme.Name}' is missing Named[\"{key}\"]");
        }
    }

    // ── Style.Fg / Style.Bg / Style.BorderColor ──────────────────────────

    [Fact]
    public void Style_Fg_ReturnsSetColor()
    {
        var s = Style.Default.Foreground(Color.Red);
        Assert.Same(Color.Red, s.Fg);
    }

    [Fact]
    public void Style_Fg_ReturnsNullWhenUnset()
    {
        Assert.Null(Style.Default.Fg);
    }

    [Fact]
    public void Style_Bg_ReturnsSetColor()
    {
        var s = Style.Default.Background(Color.Blue);
        Assert.Same(Color.Blue, s.Bg);
    }

    [Fact]
    public void Style_BorderColor_ReturnsSetColor()
    {
        var s = Style.Default.BorderForeground(Color.Green);
        Assert.Same(Color.Green, s.BorderColor);
    }

    [Fact]
    public void Style_BorderColor_ReturnsNullWhenUnset()
    {
        Assert.Null(Style.Default.BorderColor);
    }
}
