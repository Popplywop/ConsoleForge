namespace ConsoleForge.Layout;


/// <summary>
/// Cursor state for a rendered frame.
/// </summary>
/// <param name="Visible">Whether the hardware cursor should be shown.</param>
/// <param name="Col">Zero-based column of the cursor.</param>
/// <param name="Row">Zero-based row of the cursor.</param>
public readonly record struct CursorDescriptor(bool Visible = true, int Col = 0, int Row = 0);