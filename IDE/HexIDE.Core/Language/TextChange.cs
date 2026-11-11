namespace HexIDE.Language;

/// <summary>A replacement of <see cref="Length"/> characters at <see cref="Offset"/> with <see cref="Text"/>.</summary>
public readonly record struct TextChange(int Offset, int Length, string Text);

