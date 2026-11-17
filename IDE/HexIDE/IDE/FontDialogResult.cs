using Avalonia.Media;

namespace HexIDE.IDE;

public record FontDialogResult(FontFamily Family, FontStyle Style, FontWeight Weight, double Size,
    bool? Underline = null, bool? Strikethrough = null, int? Charset = null);
