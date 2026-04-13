namespace ConsoleForge.Layout;

/// <summary>An absolute terminal region (col, row, width, height).</summary>
public readonly record struct Region(int Col, int Row, int Width, int Height);
