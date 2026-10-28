using System.IO;
using System.Text;
using HexIDE.IDE;

namespace HexIDE.Runtime.Interpreter;

/// <summary>How <c>Input #</c> reads a field, which depends on what it reads into (measured, #1521).</summary>
internal enum InputReading
{
    /// <summary>A Variant, Boolean or Date: the first character decides. A quote begins a quoted field, a <c>#</c> a
    /// <c>#</c> form, a digit, sign, point or <c>&amp;</c> a number, which a blank ends, and anything else text to the
    /// comma, its trailing blanks dropped.</summary>
    ByFirstCharacter,

    /// <summary>A numeric variable: every field is read as a number, which a blank ends, quotes and <c>#</c> included, so
    /// <c>#ERROR 5#</c> is two fields and <c>a b</c> is too.</summary>
    Number,

    /// <summary>A String: a quoted field, or else the text to the comma, blanks and <c>#</c> forms kept as written.</summary>
    Text,
}

internal enum InputFieldKind
{
    /// <summary>Nothing before the comma or line end.</summary>
    Empty,
    Quoted,
    Hash,
    Number,
    Text,
}

/// <summary>A field <c>Input #</c> read, and its text as written: a quoted field without its quotes.</summary>
internal readonly record struct InputField(InputFieldKind Kind, string Text);

/// <summary>The modes of <c>Open</c> that HexIDE runs: the sequential ones (#1145).</summary>
internal enum VbFileMode
{
    Input,
    Output,
    Append,
    Binary,
    Random,
}

/// <summary>
/// What a Binary or Random handle may do, from its <c>Access</c> clause, and what it lets another handle on the same file
/// do, from its <c>Lock</c> clause (#1646).
/// </summary>
[Flags]
internal enum VbAccess
{
    None = 0,
    Read = 1,
    Write = 2,
    ReadWrite = Read | Write,
}

/// <summary>
/// A program's open files, by number: <c>Open</c>, <c>Line Input #</c>, <c>Print #</c>, <c>Close</c>, <c>FreeFile</c> and
/// <c>EOF</c> (#1145), and the fields of <c>Input #</c> (#1521). Measured on vb6.exe, native and p-code agreeing (the oracle's "Sequential files"):
/// <list type="bullet">
/// <item>Text is in the ANSI code page both ways. A character it has no byte for is written as its best fit, Ā as
/// <c>A</c>, or as <c>?</c> where there is none (#1511).</item>
/// <item>A line ends at CR or CR LF, and not at a lone LF, which stays in the line. A trailing line break makes no empty
/// last line. In Input mode Ctrl+Z ends the file.</item>
/// <item>Output truncates, Append adds or creates, and Input of a missing file is 53. A number already open is 55, and so
/// is a second open of one file, unless both are Input and neither has a Lock.</item>
/// <item>A statement on a number that isn't open is 52, and so is opening number 0. Closing one that isn't open, or 0, is
/// no error. <c>EOF</c> of an Output file is True.</item>
/// <item><c>LOF</c>, <c>Loc</c>, <c>Seek</c> and the <c>Input</c> function (#1645): the length in bytes, buffered writes
/// included; the byte position from 1, which <c>Seek #n</c> moves, 63 below 1; <c>Loc</c> from it, in 128-byte blocks; and
/// <c>Input</c> reading characters, line breaks and commas included, 62 short of the count with the variable kept.</item>
/// <item>Binary and Random files (#1646), read and written with <c>Get</c> and <c>Put</c> in the bytes
/// <see cref="VbRecordFormat"/> describes. A Binary file's positions are bytes and a Random file's records of its
/// <c>Len</c>, 128 when there is none; the Seek position is the next one, and <c>Loc</c> the one before it. EOF is True
/// once a read has run past the end, and a Seek makes it False again. Each Binary or Random handle reads and
/// writes the file itself, unbuffered, so another handle on the same file sees a Put at once.</item>
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

    internal sealed class OpenFile(string path, VbFileMode mode, bool locked)
    {
        public string Path { get; } = path;
        public List<(long Start, long End)> Locks { get; } = [];
        public VbFileMode Mode { get; } = mode;
        public bool Locked { get; } = locked;
        public byte[] Input { get; init; } = [];
        public int Position { get; set; }
        // Where a read stopped at a Ctrl+Z, which Seek then answers oddly (see SeekPosition), until a Seek statement.
        public int? CtrlZAt { get; set; }
        public FileStream? Output { get; init; }
        // An Append file's length when it was opened. Seek counts from 1 there, not from the end, until a Seek statement.
        public long AppendOrigin { get; set; }
        // The print column, counted from 0 at the start of a line: where a comma's next zone is measured from.
        public int Column { get; set; }
        public int Width { get; set; }
        // A Binary or Random file's own handle, read and written in place, its position the file's.
        public FileStream? Stream { get; init; }
        // What this handle may do, and what it lets another handle on the file do (#1646).
        public VbAccess Access { get; init; }
        public VbAccess Share { get; init; }
        // A Random file's record length.
        public int RecordLength { get; init; } = 128;
        // A Binary or Random file's EOF: a read ran past the end, until a Seek.
        public bool ShortRead { get; set; }
        // Where Seek and Loc say a Binary or Random file is, which a read past the end puts beyond its own pointer.
        public long Counted { get; set; }
        // A Binary file's bytes, for Line Input, Input # and the Input function, until something writes to the file.
        public byte[]? Snapshot { get; set; }
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

    /// <summary>
    /// Opens a file. For Binary and Random, <paramref name="access"/> is the <c>Access</c> clause, null where there is none,
    /// <paramref name="share"/> what the <c>Lock</c> clause lets another handle do, and <paramref name="recordLength"/> a
    /// Random file's <c>Len</c>.
    /// </summary>
    public void Open(string path, VbFileMode mode, bool locked, int number, VbAccess? access = null,
        VbAccess share = VbAccess.ReadWrite, int recordLength = 128)
    {
        CheckNumber(number);
        if (open.ContainsKey(number))
            throw new VBRunTimeException(VBStandardError.FileAlreadyOpen);
        if (path.Length == 0)
            throw new VBRunTimeException(VBStandardError.PathFileAccessError);
        var full = FullPath(path);
        var record = mode is VbFileMode.Binary or VbFileMode.Random;
        // 55 for a second handle on one file, unless both are Input and neither has a Lock, or both are Binary or Random,
        // which the Lock and Access clauses then decide between (measured, every pair of the five modes, #1646).
        foreach (var other in open.Values)
            if (SamePath(other.Path, full) && !(record && other.Stream is not null)
                && (other.Locked || mode != VbFileMode.Input || other.Mode != VbFileMode.Input))
                throw new VBRunTimeException(VBStandardError.FileAlreadyOpen);
        if (record)
        {
            OpenRecords(full, mode, locked, number, access, share, recordLength);
            return;
        }
        try
        {
            open[number] = mode switch
            {
                VbFileMode.Input => new OpenFile(full, mode, locked) { Input = ReadAll(full) },
                VbFileMode.Append => Appending(full, locked),
                _ => new OpenFile(full, mode, locked) { Output = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.Read) },
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

    // Opened at its end, but not in FileMode.Append, which refuses a seek before the end: VB6's `Seek #n, 1` on an Append
    // file writes over the first byte (measured, #1645).
    private static OpenFile Appending(string full, bool locked)
    {
        var stream = new FileStream(full, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        stream.Seek(0, SeekOrigin.End);
        return new OpenFile(full, VbFileMode.Append, locked) { Output = stream, AppendOrigin = stream.Length };
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
        var (bytes, start, endByte) = TextSource(file);
        if (start >= bytes.Length || bytes[start] == endByte)
        {
            if (file.Mode == VbFileMode.Input && start < bytes.Length)
                file.CtrlZAt ??= start;
            file.ShortRead = true;
            throw new VBRunTimeException(VBStandardError.InputPastEndOfFile);
        }
        var end = start;
        while (end < bytes.Length && bytes[end] != (byte)'\r' && bytes[end] != endByte)
            end++;
        if (file.Mode == VbFileMode.Input && end < bytes.Length && bytes[end] == CtrlZ)
            file.CtrlZAt = end;
        var line = Ansi().GetString(bytes, start, end - start);
        // A Binary file's line that runs to the end, or whose CR is its last byte, leaves EOF True, where one ending CR LF
        // at the end does not (measured, #1646): looking for the line's end, or for an LF after its CR, read past the end.
        var past = end >= bytes.Length;
        if (end < bytes.Length && bytes[end] == (byte)'\r')
        {
            end++;
            if (end < bytes.Length && bytes[end] == (byte)'\n')
                end++;
            else
                past = end >= bytes.Length;
        }
        MoveText(file, end, past);
        return line;
    }

    // The bytes Line Input, Input # and the Input function read, where they are, and the byte that ends the file there: an
    // Input file's Ctrl+Z, and none in a Binary file, where a Ctrl+Z is an ordinary byte (measured, #1646). A Random file,
    // and an Output or Append one, is 54 (measured).
    private (byte[] Bytes, int Position, int EndByte) TextSource(OpenFile file)
    {
        if (file.Mode == VbFileMode.Input)
        {
            CheckLocks(file, 0, long.MaxValue);
            return (file.Input, file.Position, CtrlZ);
        }
        if (file.Mode != VbFileMode.Binary)
            throw new VBRunTimeException(VBStandardError.BadFileMode);
        return (SnapshotOf(file), (int)Math.Min(file.Stream!.Position, int.MaxValue), -1);
    }

    // After a read: where the file now is, and for a Binary file whether the read ran past the end, which EOF answers.
    private static void MoveText(OpenFile file, int position, bool past)
    {
        if (file.Stream is { } stream)
        {
            stream.Position = position;
            file.Counted = position;
        }
        else
            file.Position = position;
        file.ShortRead = past;
    }

    /// <summary>
    /// The next field for <c>Input #</c> (#1521), or null at the end of the file. Blanks before it are skipped. A field ends
    /// at a comma or a CR, which it takes with it, CR LF as one; a lone LF is an ordinary character (measured). After a
    /// quoted field, a <c>#</c> form or a number, blanks are skipped, then a comma or a line end is taken, and anything
    /// else begins the next field: <c>"a" junk</c> is two fields, and so are <c>5 x</c> and <c>1 2</c>. How a field is read
    /// depends on <paramref name="reading"/>, as <see cref="InputReading"/> says.
    /// </summary>
    public InputField? ReadField(int number, InputReading reading)
    {
        var file = Get(number);
        var (bytes, p, endByte) = TextSource(file);
        while (p < bytes.Length && bytes[p] is (byte)' ' or (byte)'\t')
            p++;
        if (p >= bytes.Length || bytes[p] == endByte)
        {
            MoveText(file, p, past: true);
            return null;
        }
        var first = bytes[p];
        InputField field;
        if (first == (byte)',')
        {
            field = new InputField(InputFieldKind.Empty, "");
            p++;
        }
        else if (first == (byte)'\r')
        {
            field = new InputField(InputFieldKind.Empty, "");
            p = AfterLineEnd(bytes, p);
        }
        else if (first == (byte)'"' && reading != InputReading.Number)
        {
            // To the next quote, or to the end of the file, line ends and all (measured): a doubled quote ends the field.
            var end = Scan(bytes, p + 1, endByte, b => b == (byte)'"');
            field = new InputField(InputFieldKind.Quoted, Ansi().GetString(bytes, p + 1, end - p - 1));
            p = AfterField(bytes, end < bytes.Length && bytes[end] == (byte)'"' ? end + 1 : end);
        }
        else if (first == (byte)'#' && reading == InputReading.ByFirstCharacter)
        {
            // To the closing #, or to the comma or line end when there is none: `#,5` is "#" and 5 (measured).
            var end = Scan(bytes, p + 1, endByte, b => b is (byte)'#' or (byte)',' or (byte)'\r');
            if (end < bytes.Length && bytes[end] == (byte)'#')
                end++;
            field = new InputField(InputFieldKind.Hash, Ansi().GetString(bytes, p, end - p));
            p = AfterField(bytes, end);
        }
        else if (reading == InputReading.Number || (reading == InputReading.ByFirstCharacter && IsNumberStart(first)))
        {
            var end = Scan(bytes, p, endByte, b => b is (byte)' ' or (byte)'\t' or (byte)',' or (byte)'\r');
            field = new InputField(InputFieldKind.Number, Ansi().GetString(bytes, p, end - p));
            p = AfterField(bytes, end);
        }
        else
        {
            var end = Scan(bytes, p, endByte, b => b is (byte)',' or (byte)'\r');
            field = new InputField(InputFieldKind.Text, Ansi().GetString(bytes, p, end - p).TrimEnd(' ', '\t'));
            p = AfterField(bytes, end);
        }
        // A Binary file's last field, with no line end after it, read to the end: EOF True. Chosen, not measured; a field
        // ending CR LF at the end leaves it False (measured).
        MoveText(file, p, past: p >= bytes.Length && !(p > 0 && bytes[p - 1] is (byte)'\r' or (byte)'\n' or (byte)','));
        return field;
    }

    private static bool IsNumberStart(byte b) => b is >= (byte)'0' and <= (byte)'9' or (byte)'+' or (byte)'-' or (byte)'.' or (byte)'&';

    // The first position from `start` that `stop` matches, or the end of the file, which `endByte` also marks: an Input
    // file's Ctrl+Z, and nothing in a Binary file's.
    private static int Scan(byte[] bytes, int start, int endByte, Func<byte, bool> stop)
    {
        var p = start;
        while (p < bytes.Length && bytes[p] != endByte && !stop(bytes[p]))
            p++;
        return p;
    }

    private static int AfterField(byte[] bytes, int p)
    {
        while (p < bytes.Length && bytes[p] is (byte)' ' or (byte)'\t')
            p++;
        if (p < bytes.Length && bytes[p] == (byte)',')
            return p + 1;
        return p < bytes.Length && bytes[p] == (byte)'\r' ? AfterLineEnd(bytes, p) : p;
    }

    private static int AfterLineEnd(byte[] bytes, int cr) =>
        cr + 1 < bytes.Length && bytes[cr + 1] == (byte)'\n' ? cr + 2 : cr + 1;

    /// <summary>Whether a read would find nothing: the end of the file, or a Ctrl+Z. An Output file is always True.</summary>
    public bool Eof(int number)
    {
        var file = Get(number);
        if (file.Stream is not null)
            return file.ShortRead;
        return file.Mode != VbFileMode.Input || AtEnd(file);
    }

    // #2521 measured every mode except Input: its probe failed to open that file.
    public long FileAttr(int number, long attribute)
    {
        if (attribute != 1)
            throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
        return Get(number).Mode switch
        {
            VbFileMode.Output => 2L,
            VbFileMode.Append => 8L,
            VbFileMode.Random => 4L,
            VbFileMode.Binary => 32L,
            _ => throw new NotImplementedException("FileAttr for Input mode is not implemented: its VB6 value is unmeasured (#2521)"),
        };
    }

    private static bool AtEnd(OpenFile file) => file.Position >= file.Input.Length || file.Input[file.Position] == CtrlZ;

    /// <summary>
    /// <c>LOF</c>: the file's length in bytes. An Output or Append file's counts what has been printed and not yet written
    /// out, as VB6's does, where <c>FileLen</c> still reads 0 (measured). An Input file's is its length when it was opened.
    /// </summary>
    public long Length(int number)
    {
        var file = Get(number);
        return file.Stream?.Length ?? file.Output?.Length ?? file.Input.Length;
    }

    /// <summary>
    /// The <c>Seek</c> function: the byte the next read or write is at, from 1 (measured, #1645). An Append file counts from 1
    /// at its opening, not from its end: 1 for a 300-byte file, and 201 after 200 bytes more. After a read reaches a Ctrl+Z,
    /// in Line Input or Input, VB6 answers the length plus the Ctrl+Z's position plus 1, so 9 for <c>ab</c>, Ctrl+Z,
    /// <c>cd</c>: measured on six files from 1 byte to 203, with no rule beyond that one fitting them all.
    /// </summary>
    public long SeekPosition(int number)
    {
        var file = Get(number);
        if (file.Stream is not null)
            return file.Mode == VbFileMode.Random ? file.Counted / file.RecordLength + 1 : file.Counted + 1;
        if (file.Output is { } output)
            return output.Position - file.AppendOrigin + 1;
        return file.CtrlZAt is { } z ? file.Input.Length + z + 2 : file.Position + 1L;
    }

    /// <summary>
    /// <c>Loc</c>, in 128-byte blocks from the Seek position (measured, #1645): an Input file's at least 1, 1 at the start and
    /// for an empty file, then 2 once 129 bytes are read; an Output or Append file's the bytes written divided by 128.
    /// </summary>
    public long Loc(int number)
    {
        var file = Get(number);
        var done = SeekPosition(number) - 1;
        // A Binary file's is the last byte read or written, and a Random file's the last record (measured, #1646).
        if (file.Stream is not null)
            return done;
        return file.Mode == VbFileMode.Input ? Math.Max(1, (done + 127) / 128) : done / 128;
    }

    /// <summary>
    /// The <c>Seek</c> statement: the next read or write is at byte <paramref name="position"/>, from 1, and below 1 is 63
    /// (measured, #1645). Past the end an Input file is at its end, EOF True, and an Output file's next Print writes there,
    /// the gap NUL bytes. On an Append file the position is from the file's start, as an Output file's is.
    /// </summary>
    public void Seek(int number, long position)
    {
        var file = Get(number);
        if (position < 1)
            throw new VBRunTimeException(VBStandardError.BadRecordNumber);
        if (file.Stream is { } stream)
        {
            stream.Position = file.Mode == VbFileMode.Random ? (position - 1) * file.RecordLength : position - 1;
            file.Counted = stream.Position;
            file.ShortRead = false;
            return;
        }
        if (file.Output is { } output)
        {
            output.Position = position - 1;
            file.AppendOrigin = 0;
            return;
        }
        file.Position = (int)Math.Min(position - 1, int.MaxValue);
        file.CtrlZAt = null;
    }

    /// <summary>
    /// The <c>Input</c> function: the next <paramref name="count"/> characters, in the ANSI code page, line breaks, commas and
    /// quotes as they are (measured, #1645). A negative count is 5, and 0 reads nothing, at the end too. Fewer characters than
    /// the count before the end or a Ctrl+Z is 62, the variable keeping its value and the file left at its end, or at the
    /// Ctrl+Z. A file open for Output or Append is 54. Which of 5 and 54 a negative count on an Output file gives is not
    /// measured: 5, as an argument is checked first.
    /// </summary>
    public string Read(int number, int count)
    {
        var file = Get(number);
        if (count < 0)
            throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
        var (bytes, p, _) = TextSource(file);
        var decoder = Ansi().GetDecoder();
        var text = new StringBuilder(count);
        var chars = new char[2];
        while (text.Length < count)
        {
            // A Binary file answers what there is, with no error, and EOF True (measured: one character of five, and "" at
            // the end); a Ctrl+Z is an ordinary character there.
            if (file.Mode == VbFileMode.Binary && p >= bytes.Length)
            {
                MoveText(file, p, past: true);
                return text.ToString();
            }
            if (p >= bytes.Length)
            {
                file.Position = Math.Max(file.Position, bytes.Length);
                throw new VBRunTimeException(VBStandardError.InputPastEndOfFile);
            }
            if (file.Mode == VbFileMode.Input && bytes[p] == CtrlZ)
            {
                file.Position = p;
                file.CtrlZAt ??= p;
                throw new VBRunTimeException(VBStandardError.InputPastEndOfFile);
            }
            text.Append(chars, 0, decoder.GetChars(bytes, p++, 1, chars, 0, flush: false));
        }
        MoveText(file, p, past: false);
        return text.ToString();
    }

    public void SetWidth(int number, int width)
    {
        var file = Get(number);
        if (file.Mode == VbFileMode.Random)
            throw new NotImplementedException("Width on a Random file is unmeasured (#2554)");
        if (width is < 0 or > 255)
            throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
        file.Width = width;
    }

    public int PrintWidth(int number) => Writable(number).Width;

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
        {
            file.Output?.Dispose();
            file.Stream?.Dispose();
        }
    }

    /// <summary><c>Close</c> with no number, and the end of the program: every file is closed.</summary>
    public void CloseAll()
    {
        foreach (var file in open.Values)
        {
            file.Output?.Dispose();
            file.Stream?.Dispose();
        }
        open.Clear();
    }

    private OpenFile Writable(int number)
    {
        var file = Get(number);
        // Print # and Write # on a Binary or Random file are 54 too (measured, #1646).
        if (file.Mode is VbFileMode.Input or VbFileMode.Binary or VbFileMode.Random)
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
    internal string FullPath(string path)
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
    internal static Encoding Ansi()
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
