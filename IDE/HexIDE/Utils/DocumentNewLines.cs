using AvaloniaEdit.Document;

namespace HexIDE.Utils;

internal static class DocumentNewLines
{
    // Follow the buffer, never the host. A buffer with no line break yet starts as a VB6 file (#530).
    public static string Of(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n"
        : text.Contains('\n') ? "\n"
        : text.Contains('\r') ? "\r" : "\r\n";

    public static string Normalize(string incoming, string current) =>
        TextUtilities.NormalizeNewLines(incoming, Of(current));
}
