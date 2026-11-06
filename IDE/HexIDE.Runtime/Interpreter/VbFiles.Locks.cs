namespace HexIDE.Runtime.Interpreter;

internal sealed partial class VbFiles
{
    // In-process byte ranges: #2553 measured two numbers in one program, not another process.
    // Keeping them on the open handle also releases them on Close and CloseAll.
    public void Lock(int number, long? first, long? last, bool unlock)
    {
        var file = Get(number);
        var start = first ?? 1;
        var end = last ?? first ?? long.MaxValue;
        if (start < 1 || end < start)
            throw new VBRunTimeException(VBStandardError.BadRecordNumber);
        var range = (Start: 0L, End: long.MaxValue);
        if (file.Mode is VbFileMode.Binary or VbFileMode.Random && (first is not null || last is not null))
        {
            var size = file.Mode == VbFileMode.Random ? file.RecordLength : 1;
            range = ((start - 1) * size, end * size - 1);
        }
        if (unlock)
        {
            if (!file.Locks.Remove(range))
                throw new VBRunTimeException(VBStandardError.PermissionDenied);
            return;
        }
        foreach (var other in open.Values)
            if (SamePath(file.Path, other.Path))
                foreach (var held in other.Locks)
                    if (range.Start <= held.End && held.Start <= range.End)
                        throw new VBRunTimeException(VBStandardError.PermissionDenied);
        file.Locks.Add(range);
    }

    private void CheckLocks(OpenFile file, long start, long end)
    {
        foreach (var other in open.Values)
            if (other != file && SamePath(file.Path, other.Path))
                foreach (var held in other.Locks)
                    if (start <= held.End && held.Start <= end)
                        throw new VBRunTimeException(VBStandardError.PermissionDenied);
    }
}
