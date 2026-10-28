namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// Binary and Random files (#1646): opening them, and the bytes of <c>Get</c> and <c>Put</c>, which
/// <see cref="VbRecordFormat"/> encodes. Measured on vb6.exe, native and p-code agreeing (the oracle's "Random and Binary
/// files").
/// </summary>
internal sealed partial class VbFiles
{
    // Opens a Binary or Random file, creating it when it is missing, with Access Read too (measured). Two such handles on one
    // file are let through as Windows lets two handles through: each must allow what the other does, a Lock clause allowing
    // what it doesn't name, so Lock Read allows writing. With no Access clause VB6 asks for reading and writing, then for
    // writing alone, then for reading alone, and takes the first allowed: beside Lock Read a plain Open can only write, and
    // a Get on it is 75 (measured, all twenty pairs of the four Lock clauses, Access Read and Access Write). Nothing allowed is
    // 70. These locks hold only within the program, as the remarks say.
    private void OpenRecords(string full, VbFileMode mode, bool locked, int number, VbAccess? access, VbAccess share, int recordLength)
    {
        VBStandardError? refused = null;
        foreach (var candidate in access is { } asked ? [asked] : new[] { VbAccess.ReadWrite, VbAccess.Write, VbAccess.Read })
        {
            if (!Shares(full, candidate, share))
            {
                refused ??= VBStandardError.PermissionDenied;
                continue;
            }
            FileStream stream;
            try
            {
                // Unbuffered, so another handle on the file reads a Put at once (measured: Put on #2, Get on #1 reads it).
                stream = new FileStream(full, FileMode.OpenOrCreate, ToFileAccess(candidate), FileShare.ReadWrite, bufferSize: 0);
            }
            catch (UnauthorizedAccessException)
            {
                // A read-only file, say. Not measured: 75, as Put on a handle that can't write is.
                refused = VBStandardError.PathFileAccessError;
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                throw new VBRunTimeException(VBStandardError.PathNotFound);
            }
            catch (IOException)
            {
                throw new VBRunTimeException(VBStandardError.PermissionDenied);
            }
            open[number] = new OpenFile(full, mode, locked)
            {
                Stream = stream,
                Access = candidate,
                Share = share,
                RecordLength = recordLength,
            };
            return;
        }
        throw new VBRunTimeException(refused ?? VBStandardError.PermissionDenied);
    }

    private bool Shares(string full, VbAccess access, VbAccess share)
    {
        foreach (var other in open.Values)
            if (other.Stream is not null && SamePath(other.Path, full)
                && ((access & ~other.Share) != 0 || (other.Access & ~share) != 0))
                return false;
        return true;
    }

    private static FileAccess ToFileAccess(VbAccess access) => access switch
    {
        VbAccess.Read => FileAccess.Read,
        VbAccess.Write => FileAccess.Write,
        _ => FileAccess.ReadWrite,
    };

    // A Binary file's bytes, for the reads that scan text, kept until something writes to the file.
    private byte[] SnapshotOf(OpenFile file)
    {
        if ((file.Access & VbAccess.Read) == 0)
            throw new VBRunTimeException(VBStandardError.PathFileAccessError);
        if (file.Snapshot is { } kept)
            return kept;
        var stream = file.Stream!;
        var position = stream.Position;
        var bytes = new byte[stream.Length];
        stream.Position = 0;
        stream.ReadExactly(bytes);
        stream.Position = position;
        file.Snapshot = bytes;
        return bytes;
    }

    private void Wrote(string path)
    {
        foreach (var other in open.Values)
            if (SamePath(other.Path, path))
                other.Snapshot = null;
    }

    // A Binary or Random file for Get or Put: 54 for any other mode, and 75 for a handle that may not read or write
    // (measured). Then where the statement starts: at <paramref name="at"/>, a byte or a record from 1, 63 below 1 (measured),
    // or where the file is. Where it is has two answers after a read that ran past the end, and they are kept apart as VB6
    // keeps them (measured, #1646): the file's own pointer stopped at the end, where the next Get or Put with no position
    // reads or writes, while Seek and Loc count on as if the whole value had been there. So `Get #1, 2, l` on two bytes
    // answers Seek 6, and the next `Put #1, , b` writes byte 3, after which Seek is 7. A Seek statement brings them together.
    private (OpenFile File, long Start, long Counted) RecordAt(int number, long? at, VbAccess need)
    {
        var file = Get(number);
        if (file.Stream is not { } stream)
            throw new VBRunTimeException(VBStandardError.BadFileMode);
        if ((file.Access & need) == 0)
            throw new VBRunTimeException(VBStandardError.PathFileAccessError);
        if (at is not { } position)
            return (file, stream.Position, file.Counted);
        if (position < 1)
            throw new VBRunTimeException(VBStandardError.BadRecordNumber);
        var start = file.Mode == VbFileMode.Random ? (position - 1) * file.RecordLength : position - 1;
        return (file, start, start);
    }

    /// <summary>Whether a file is Binary or Random, as Get and Put need it: 52 for a number not open, and 54 for any other mode (measured).</summary>
    public VbFileMode RecordMode(int number)
    {
        var file = Get(number);
        return file.Stream is not null ? file.Mode : throw new VBRunTimeException(VBStandardError.BadFileMode);
    }

    /// <summary>
    /// <c>Put</c>'s bytes, at <paramref name="at"/> or where the file is. A Random record longer than the file's <c>Len</c>
    /// is 59, and nothing is written (measured). After it the file is at the next byte, or the next record. EOF is left as
    /// it was: a Put after a read past the end leaves it True (measured). Where <paramref name="move"/> is false, as for a
    /// Variant holding an object, the file stays where it was (measured: 09 00 written, then 13, and the next Put writes
    /// over it).
    /// </summary>
    public void Put(int number, long? at, byte[] bytes, bool move = true)
    {
        var (file, start, counted) = RecordAt(number, at, VbAccess.Write);
        if (file.Mode == VbFileMode.Random && bytes.Length > file.RecordLength)
            throw new VBRunTimeException(VBStandardError.BadRecordLength);
        CheckLocks(file, start, start + bytes.Length - 1);
        var stream = file.Stream!;
        var before = stream.Position;
        stream.Position = start;
        stream.Write(bytes);
        var step = file.Mode == VbFileMode.Random ? file.RecordLength : bytes.Length;
        if (move)
        {
            stream.Position = start + step;
            file.Counted = counted + step;
        }
        else
            stream.Position = before;
        Wrote(file.Path);
    }

    /// <summary>
    /// The bytes one <c>Get</c> reads. A Binary file's past its end are zeros, and reading them sets EOF; a Random file's
    /// come from the whole record, zeros past the end, EOF set when the record runs past it, and a value longer than the
    /// record is 59 (measured). Nothing moves until <see cref="EndGet"/>, which a failed Get never reaches: VB6 leaves the
    /// file where it was (measured for 13, 458 and 59).
    /// </summary>
    public RecordRead BeginGet(int number, long? at)
    {
        var (file, start, counted) = RecordAt(number, at, VbAccess.Read);
        return new RecordRead(this, file, start, counted);
    }

    /// <summary>Moves the file past what <paramref name="read"/> took, or past its record, and sets EOF.</summary>
    public void EndGet(RecordRead read) => read.Commit();

    internal sealed class RecordRead : IRecordSource
    {
        private readonly OpenFile file;
        private readonly VbFiles files;
        private readonly long start;
        private readonly long counted;
        private readonly byte[]? record;
        private int taken;
        // Bytes that were there, which is where the file's own pointer stops.
        private int got;
        private bool past;

        internal RecordRead(VbFiles files, OpenFile file, long start, long counted)
        {
            this.file = file;
            this.files = files;
            this.start = start;
            this.counted = counted;
            if (file.Mode == VbFileMode.Random)
                record = Bytes(start, file.RecordLength, out past);
        }

        public byte[] Take(int count)
        {
            if (count <= 0)
                return [];
            byte[] bytes;
            if (record is not null)
            {
                if (taken + count > record.Length)
                    throw new RecordOverflow();
                bytes = record[taken..(taken + count)];
            }
            else
            {
                bytes = Bytes(start + taken, count, out var fewer);
                past |= fewer;
            }
            taken += count;
            return bytes;
        }

        // `count` bytes from `from`, zeros past the end, `fewer` when there were fewer. The file stays where it was.
        private byte[] Bytes(long from, int count, out bool fewer)
        {
            files.CheckLocks(file, from, from + count - 1);
            var stream = file.Stream!;
            var bytes = new byte[count];
            var read = 0;
            if (from < stream.Length)
            {
                var kept = stream.Position;
                stream.Position = from;
                while (read < count && stream.Read(bytes, read, count - read) is var n and > 0)
                    read += n;
                stream.Position = kept;
            }
            got += read;
            fewer = read < count;
            return bytes;
        }

        internal void Commit()
        {
            file.Stream!.Position = start + got;
            file.Counted = counted + (record?.Length ?? taken);
            file.ShortRead = past;
        }
    }
}
