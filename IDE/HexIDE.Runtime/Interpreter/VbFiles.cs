using System.IO;
using System.Text;
using HexIDE.IDE;

namespace HexIDE.Runtime.Interpreter;

/// <summary>The modes of <c>Open</c> that HexIDE runs: the sequential ones (#1145).</summary>
internal enum VbFileMode
{
    Input,
    Output,
    Append,
}

/// <summary>
/// A program's open files, by number: <c>Open</c>, <c>Line Input #</c>, <c>Print #</c>, <c>Close</c>, <c>FreeFile</c> and
/// <c>EOF</c> (#1145). Measured on vb6.exe, native and p-code agreeing (the oracle's "Sequential files"):
/// <list type="bullet">
/// <item>Text is in the ANSI code page both ways. A character it has no byte for is written as its best fit, Ā as
/// <c>A</c>, or as <c>?</c> where there is none (#1511).</item>
/// <item>A line ends at CR or CR LF, and not at a lone LF, which stays in the line. A trailing line break makes no empty
/// last line. In Input mode Ctrl+Z ends the file.</item>
/// <item>Output truncates, Append adds or creates, and Input of a missing file is 53. A number already open is 55, and so
/// is a second open of one file, unless both are Input and neither has a Lock.</item>
/// <item>A statement on a number that isn't open is 52, and so is opening number 0. Closing one that isn't open, or 0, is
/// no error. <c>EOF</c> of an Output file is True.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>An Input file is read whole when it is opened. VB6 reads it as it goes, so a file changed by another program
/// while it is open reads differently here; nothing measured depends on it. Locks hold only within the program: another
/// process is not kept out.</para>
/// <para>A relative path is resolved against the project's folder until the program changes directory with
/// <c>ChDir</c>, and against that directory after it. Chosen, not measured: VB6 resolves against <c>CurDir</c> (measured),
/// which is the project's folder when the project was opened from Explorer, the usual case. The IDE's own working
/// directory, wherever HexIDE happened to be started, is never used, since a file written there is one the developer
/// never chose.</para>
/// </remarks>
internal sealed partial class VbFiles
{
    private const byte CtrlZ = 0x1A;

    private sealed class OpenFile(string path, VbFileMode mode, bool locked)
    {
        public string Path { get; } = path;
        public VbFileMode Mode { get; } = mode;
        public bool Locked { get; } = locked;
        public byte[] Input { get; init; } = [];
        public int Position { get; set; }
        public FileStream? Output { get; init; }
        // The print column, counted from 0 at the start of a line: where a comma's next zone is measured from.
        public int Column { get; set; }
    }

    private readonly Dictionary<int, OpenFile> open = new();

    /// <summary>The project's folder, read when a relative path is opened: App.Path, empty for an unsaved project.</summary>
    public Func<string> ProjectFolder { get; set; } = () => "";

    /// <summary>The directory the program last changed to with ChDir, or null while it has not.</summary>
    public string? CurrentDirectory { get; set; }

    /// <summary>
    /// The lowest number not open: from 1 to 255 for range 0, and from 256 to 511 for any other (measured: 1, then 256 for
    /// both 1 and 2). It stays the same until a file is opened on it.
    /// </summary>
    public int FreeFile(int range)
    {
        var (first, last) = range == 0 ? (1, 255) : (256, 511);
        for (var number = first; number <= last; number++)
            if (!open.ContainsKey(number))
                return number;
        throw new VBRunTimeException(VBStandardError.TooManyFiles);
    }

    public void Open(string path, VbFileMode mode, bool locked, int number)
    {
        CheckNumber(number);
        if (open.ContainsKey(number))
            throw new VBRunTimeException(VBStandardError.FileAlreadyOpen);
        if (path.Length == 0)
            throw new VBRunTimeException(VBStandardError.PathFileAccessError);
        var full = FullPath(path);
        foreach (var other in open.Values)
            if (SamePath(other.Path, full) && (other.Locked || mode != VbFileMode.Input || other.Mode != VbFileMode.Input))
                throw new VBRunTimeException(VBStandardError.FileAlreadyOpen);
        try
        {
            open[number] = mode switch
            {
                VbFileMode.Input => new OpenFile(full, mode, locked) { Input = ReadAll(full) },
                _ => new OpenFile(full, mode, locked)
                {
                    Output = new FileStream(full, mode == VbFileMode.Append ? FileMode.Append : FileMode.Create,
                        FileAccess.Write, FileShare.Read),
                },
            };
        }
        catch (FileNotFoundException)
        {
            throw new VBRunTimeException(VBStandardError.FileNotFound);
        }
        catch (DirectoryNotFoundException)
        {
            throw new VBRunTimeException(VBStandardError.PathNotFound);
        }
        catch (UnauthorizedAccessException)
        {
            throw new VBRunTimeException(VBStandardError.PathFileAccessError);
        }
        catch (IOException)
        {
            // Held by another program. Not measured: 70 is what VB6 is documented to raise for a sharing violation.
            throw new VBRunTimeException(VBStandardError.PermissionDenied);
        }
    }

    private static byte[] ReadAll(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>
    /// The next line, without its ending. 62 at the end of the file, where the caller's variable keeps what it had.
    /// </summary>
    public string LineInput(int number)
    {
        var file = Get(number);
        if (file.Mode != VbFileMode.Input)
            throw new VBRunTimeException(VBStandardError.BadFileMode);
        if (AtEnd(file))
            throw new VBRunTimeException(VBStandardError.InputPastEndOfFile);
        var bytes = file.Input;
        var start = file.Position;
        var end = start;
        while (end < bytes.Length && bytes[end] != (byte)'\r' && bytes[end] != CtrlZ)
            end++;
        var line = Ansi().GetString(bytes, start, end - start);
        if (end < bytes.Length && bytes[end] == (byte)'\r')
        {
            end++;
            if (end < bytes.Length && bytes[end] == (byte)'\n')
                end++;
        }
        file.Position = end;
        return line;
    }

    /// <summary>Whether a read would find nothing: the end of the file, or a Ctrl+Z. An Output file is always True.</summary>
    public bool Eof(int number)
    {
        var file = Get(number);
        return file.Mode != VbFileMode.Input || AtEnd(file);
    }

    private static bool AtEnd(OpenFile file) => file.Position >= file.Input.Length || file.Input[file.Position] == CtrlZ;

    /// <summary>The column the next character of <c>Print #</c> lands in, from 0.</summary>
    public int Column(int number) => Writable(number).Column;

    /// <summary>Writes <paramref name="text"/> as it stands, in the ANSI code page, and moves the print column past it.</summary>
    public void Write(int number, string text)
    {
        var file = Writable(number);
        var bytes = Ansi().GetBytes(text);
        file.Output!.Write(bytes);
        var lastBreak = text.LastIndexOfAny(['\r', '\n']);
        file.Column = lastBreak < 0 ? file.Column + text.Length : text.Length - lastBreak - 1;
    }

    /// <summary>Closes a file. One that isn't open is no error, and nor is 0, but a negative number is 52 (measured).</summary>
    public void Close(int number)
    {
        if (number < 0)
            throw new VBRunTimeException(VBStandardError.BadFileNameOrNumber);
        if (open.Remove(number, out var file))
            file.Output?.Dispose();
    }

    /// <summary><c>Close</c> with no number, and the end of the program: every file is closed.</summary>
    public void CloseAll()
    {
        foreach (var file in open.Values)
            file.Output?.Dispose();
        open.Clear();
    }

    private OpenFile Writable(int number)
    {
        var file = Get(number);
        if (file.Mode == VbFileMode.Input)
            throw new VBRunTimeException(VBStandardError.BadFileMode);
        return file;
    }

    private OpenFile Get(int number)
    {
        CheckNumber(number);
        return open.TryGetValue(number, out var file) ? file : throw new VBRunTimeException(VBStandardError.BadFileNameOrNumber);
    }

    // Number 0 is 52 (measured), and so, by the same rule, is a negative one. 512 is taken, so no upper bound is drawn.
    private static void CheckNumber(int number)
    {
        if (number < 1)
            throw new VBRunTimeException(VBStandardError.BadFileNameOrNumber);
    }

    // A program's paths are Windows paths. Elsewhere a backslash is taken as the separator it means. A relative path is
    // resolved as the remarks say.
    private string FullPath(string path)
    {
        if (!OperatingSystem.IsWindows())
            path = path.Replace('\\', System.IO.Path.DirectorySeparatorChar);
        if (System.IO.Path.IsPathFullyQualified(path))
            return System.IO.Path.GetFullPath(path);
        var baseDirectory = CurrentDirectory ?? ProjectFolder();
        // Rooted on no drive, as \x or /x is on Windows, a path is the current drive's root and needs no folder to resolve it.
        if (baseDirectory.Length == 0 && System.IO.Path.IsPathRooted(path))
            return System.IO.Path.GetFullPath(path);
        if (baseDirectory.Length == 0)
            throw new NotImplementedException(
                $"Opening \"{path}\", a relative path, needs a saved project: an unsaved one has no folder to resolve it against");
        return System.IO.Path.GetFullPath(path, baseDirectory);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static Encoding? ansi;
    private static int ansiCodePage;

    // The ANSI code page, writing a character it has no byte for as Windows does: its best fit, Ā as "A" and Ł as "L", and
    // '?' only where there is none, as for Ж (measured, #1511). The code page's own encoding does that; a plain '?'
    // replacement, which this was, wrote every one of them as '?'.
    private static Encoding Ansi()
    {
        var codePage = Vb6TextEncoding.Ansi.CodePage;
        if (ansi is null || ansiCodePage != codePage)
        {
            var table = (Encoding)(CodePagesEncodingProvider.Instance.GetEncoding(codePage) ?? Encoding.GetEncoding(codePage)).Clone();
            table.DecoderFallback = DecoderFallback.ReplacementFallback;
            ansi = table;
            ansiCodePage = codePage;
        }
        return ansi;
    }
}
