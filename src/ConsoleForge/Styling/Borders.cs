namespace ConsoleForge.Styling;

/// <summary>Pre-defined border character sets.</summary>
public static class Borders
{
    /// <summary>Box-drawing single line: ┌─┐│└┘├┤┬┴┼</summary>
    public static readonly BorderSpec Normal = new()
    {
        Top = "─", Bottom = "─", Left = "│", Right = "│",
        TopLeft = "┌", TopRight = "┐", BottomLeft = "└", BottomRight = "┘",
        MiddleLeft = "├", MiddleRight = "┤", Middle = "┼",
        MiddleTop = "┬", MiddleBottom = "┴"
    };

    /// <summary>Rounded corners: ╭─╮│╰╯├┤┬┴┼</summary>
    public static readonly BorderSpec Rounded = new()
    {
        Top = "─", Bottom = "─", Left = "│", Right = "│",
        TopLeft = "╭", TopRight = "╮", BottomLeft = "╰", BottomRight = "╯",
        MiddleLeft = "├", MiddleRight = "┤", Middle = "┼",
        MiddleTop = "┬", MiddleBottom = "┴"
    };

    /// <summary>Heavy/thick lines: ┏━┓┃┗┛┣┫┳┻╋</summary>
    public static readonly BorderSpec Thick = new()
    {
        Top = "━", Bottom = "━", Left = "┃", Right = "┃",
        TopLeft = "┏", TopRight = "┓", BottomLeft = "┗", BottomRight = "┛",
        MiddleLeft = "┣", MiddleRight = "┫", Middle = "╋",
        MiddleTop = "┳", MiddleBottom = "┻"
    };

    /// <summary>Double lines: ╔═╗║╚╝╠╣╦╩╬</summary>
    public static readonly BorderSpec Double = new()
    {
        Top = "═", Bottom = "═", Left = "║", Right = "║",
        TopLeft = "╔", TopRight = "╗", BottomLeft = "╚", BottomRight = "╝",
        MiddleLeft = "╠", MiddleRight = "╣", Middle = "╬",
        MiddleTop = "╦", MiddleBottom = "╩"
    };

    /// <summary>ASCII safe fallback: +-+|+-+</summary>
    public static readonly BorderSpec ASCII = new()
    {
        Top = "-", Bottom = "-", Left = "|", Right = "|",
        TopLeft = "+", TopRight = "+", BottomLeft = "+", BottomRight = "+",
        MiddleLeft = "+", MiddleRight = "+", Middle = "+",
        MiddleTop = "+", MiddleBottom = "+"
    };

    /// <summary>Invisible border (all spaces).</summary>
    public static readonly BorderSpec Hidden = new()
    {
        Top = " ", Bottom = " ", Left = " ", Right = " ",
        TopLeft = " ", TopRight = " ", BottomLeft = " ", BottomRight = " ",
        MiddleLeft = " ", MiddleRight = " ", Middle = " ",
        MiddleTop = " ", MiddleBottom = " "
    };
}
