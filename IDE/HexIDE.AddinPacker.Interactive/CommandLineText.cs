namespace HexIDE.AddinPacker.Interactive;

/// <summary>
/// Writes an argument list as one command line you can paste back into a shell. hexide-pack itself never parses
/// this text: it hands the packer the argument list. The line is for your next run, or a script.
/// </summary>
public static class CommandLineText
{
    public static string Format(IEnumerable<string> arguments, bool windows) =>
        string.Join(' ', new[] { "hexide-pack" }.Concat(arguments.Select(argument => Quote(argument, windows))));

    /// <summary>
    /// POSIX shells get single quotes, which keep every character as typed. On Windows, double quotes work in both
    /// PowerShell and cmd, unless the value holds a character PowerShell would expand there (<c>$</c> or a
    /// backtick) or a double quote, and then it gets PowerShell's single quotes.
    /// </summary>
    public static string Quote(string value, bool windows)
    {
        if (value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || "._,:/=@+-".Contains(c)
                || (windows && c == '\\')))
            return value;
        if (!windows)
            return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
        return value.IndexOfAny(['"', '$', '`']) >= 0
            ? "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'"
            : "\"" + value + "\"";
    }
}
