using System.Text;
using ConsoleForge.Layout;

namespace ConsoleForge.Tests.Rendering;

/// <summary>
/// A screen model that applies ANSI output the way a real terminal does:
/// the cursor advances by the *display width* of each glyph, so a 2-column
/// glyph occupies its own cell plus a continuation cell.
/// <para>
/// <see cref="Testing.VirtualTerminal"/> advances one column per char, which
/// hides column-drift bugs. This simulator exists to catch them.
/// </para>
/// </summary>
internal sealed class TerminalSim
{
    private const char Esc = '\x1b';
    private const char Bel = '\a';

    /// <summary>Marker stored in the right half of a 2-column glyph.</summary>
    private const string Continuation = "\uFFFF";

    private readonly string[,] _screen;
    private int _row;
    private int _col;

    public int Width  { get; }
    public int Height { get; }

    public TerminalSim(int width, int height)
    {
        Width  = width;
        Height = height;
        _screen = new string[height, width];
        Clear();
    }

    public void Apply(string ansi)
    {
        int i = 0;
        while (i < ansi.Length)
        {
            char ch = ansi[i];

            if (ch == Esc)
            {
                i = SkipEscape(ansi, i);
                continue;
            }

            if (ch == '\n') { _row++; _col = 0; i++; continue; }
            if (ch == '\r') { _col = 0; i++; continue; }

            // Decode one rune (may be a surrogate pair).
            Rune rune;
            int len;
            if (Rune.TryCreate(ch, out rune)) len = 1;
            else if (i + 1 < ansi.Length && Rune.TryCreate(ch, ansi[i + 1], out rune)) len = 2;
            else { i++; continue; }

            int w = TextUtils.RuneDisplayWidth(rune);
            if (w <= 0) { i += len; continue; }

            if (_row >= 0 && _row < Height && _col >= 0 && _col < Width)
            {
                _screen[_row, _col] = rune.ToString();
                if (w == 2 && _col + 1 < Width)
                    _screen[_row, _col + 1] = Continuation;
            }

            _col += w;
            i += len;
        }
    }

    /// <summary>Skip one escape sequence, honouring the erase commands we emit.</summary>
    private int SkipEscape(string ansi, int i)
    {
        if (i + 1 < ansi.Length && ansi[i + 1] == '[')
        {
            int j = i + 2;
            while (j < ansi.Length && (ansi[j] < 0x40 || ansi[j] > 0x7E)) j++;
            if (j >= ansi.Length) return ansi.Length;

            string param = ansi[(i + 2)..j];
            switch (ansi[j])
            {
                case 'H':
                {
                    var parts = param.Split(';');
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0], out int r) &&
                        int.TryParse(parts[1], out int c))
                    { _row = r - 1; _col = c - 1; }
                    else { _row = 0; _col = 0; }
                    break;
                }
                case 'K':
                    EraseLine(param);
                    break;
                case 'J':
                    if (param is "2" or "") Clear();
                    break;
            }
            return j + 1;
        }

        // Non-CSI (OSC / DCS / APC): run to the string terminator.
        int k = i + 1;
        if (k < ansi.Length && (ansi[k] is ']' or 'P' or '_' or '^'))
        {
            while (k < ansi.Length)
            {
                if (ansi[k] == Bel) return k + 1;
                if (ansi[k] == Esc && k + 1 < ansi.Length && ansi[k + 1] == '\\') return k + 2;
                k++;
            }
            return ansi.Length;
        }
        while (k < ansi.Length && ansi[k] < '@') k++;
        return k + 1;
    }

    private void EraseLine(string param)
    {
        if (_row < 0 || _row >= Height) return;
        int from = param is "2" ? 0 : Math.Max(0, _col);
        for (int c = from; c < Width; c++) _screen[_row, c] = " ";
    }

    public void Clear()
    {
        for (var r = 0; r < Height; r++)
            for (var c = 0; c < Width; c++)
                _screen[r, c] = " ";
    }

    /// <summary>Screen rows, with wide-glyph continuation cells collapsed away.</summary>
    public string[] Lines
    {
        get
        {
            var lines = new string[Height];
            var sb = new StringBuilder();
            for (var r = 0; r < Height; r++)
            {
                sb.Clear();
                for (var c = 0; c < Width; c++)
                {
                    var cell = _screen[r, c];
                    if (cell == Continuation) continue;
                    sb.Append(cell);
                }
                lines[r] = sb.ToString();
            }
            return lines;
        }
    }

    public string Screen => string.Join("\n", Lines);
}
