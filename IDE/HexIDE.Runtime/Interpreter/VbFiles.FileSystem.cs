using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HexIDE.Runtime.Interpreter;

// Filesystem semantics measured with compiled VB6 EXEs (#1561); see the fidelity oracle.
internal sealed partial class VbFiles
{
    private string[]? listing;
    private int listingIndex;

    public string CurDir(string drive = "")
    {
        var current = FullPath(".");
        if (drive.Length == 0) return current;
        var letter = char.ToUpperInvariant(drive[0]);
        if (letter is < 'A' or > 'Z') Fail(VBStandardError.InvalidProcedureCall);
        if (!OperatingSystem.IsWindows() || !Directory.Exists($"{letter}:\\")) Fail(VBStandardError.DeviceUnavailable);
        return char.ToUpperInvariant(current[0]) == letter ? current : Path.GetFullPath($"{letter}:.");
    }

    public void ChangeDirectory(string path)
    {
        if (path.Length == 0) Fail(VBStandardError.PathNotFound);
        var full = FullPath(path);
        if (!Directory.Exists(full)) Fail(VBStandardError.PathNotFound);
        CurrentDirectory = full;
    }

    private static void Fail(VBStandardError error) => throw new VBRunTimeException(error);
    private static void NoWildcards(string path)
    {
        if (path.IndexOfAny(['*', '?']) >= 0) Fail(VBStandardError.BadFileNameOrNumber);
    }

    private string Existing(string path)
    {
        NoWildcards(path);
        if (path.Length == 0) Fail(VBStandardError.FileNotFound);
        var full = FullPath(path);
        if (!File.Exists(full) && !Directory.Exists(full)) Fail(VBStandardError.FileNotFound);
        return full;
    }

    private void CheckOpen(string path, bool inputAllowed)
    {
        if (open.Values.Any(f => SamePath(f.Path, path) && (!inputAllowed || f.Mode != VbFileMode.Input)))
            Fail(VBStandardError.FileAlreadyOpen);
    }

    private static void Access(Action action)
    {
        try { action(); }
        catch (UnauthorizedAccessException) { Fail(VBStandardError.PathFileAccessError); }
        catch (IOException) { Fail(VBStandardError.PathFileAccessError); }
    }

    public void MakeDirectory(string path)
    {
        if (path.Length == 0) Fail(VBStandardError.PathNotFound);
        var full = Path.TrimEndingDirectorySeparator(FullPath(path));
        if (File.Exists(full) || Directory.Exists(full)) Fail(VBStandardError.PathFileAccessError);
        if (!Directory.Exists(Path.GetDirectoryName(full))) Fail(VBStandardError.PathNotFound);
        Access(() => Directory.CreateDirectory(full));
    }

    public void RemoveDirectory(string path)
    {
        if (path.Length == 0 || path.IndexOfAny(['*', '?']) >= 0) Fail(VBStandardError.PathNotFound);
        var full = FullPath(path);
        if (!Directory.Exists(full)) Fail(VBStandardError.PathNotFound);
        Access(() => Directory.Delete(full));
    }

    public int FileLength(string path)
    {
        var full = Existing(path);
        return Directory.Exists(full) ? 0 : checked((int)new FileInfo(full).Length);
    }

    public DateTime FileDateTime(string path)
    {
        var date = File.GetLastWriteTime(Existing(path));
        return new DateTime(date.Ticks - date.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Unspecified);
    }

    public int GetAttributes(string path) => (int)File.GetAttributes(Existing(path)) & 55;

    public void SetAttributes(string path, int attributes)
    {
        if ((attributes & ~39) != 0) Fail(VBStandardError.InvalidProcedureCall);
        var full = Existing(path);
        CheckOpen(full, true);
        Access(() => File.SetAttributes(full, (FileAttributes)attributes | (File.GetAttributes(full) & FileAttributes.Directory)));
    }

    public void Copy(string source, string destination)
    {
        var from = Existing(source);
        NoWildcards(destination);
        var to = FullPath(destination);
        CheckOpen(from, true);
        CheckOpen(to, true);
        if (SamePath(from, to)) Fail(VBStandardError.PermissionDenied);
        if (Directory.Exists(from) || Directory.Exists(to)) Fail(VBStandardError.PathFileAccessError);
        if (!Directory.Exists(Path.GetDirectoryName(to))) Fail(VBStandardError.PathNotFound);
        if (File.Exists(to) && (File.GetAttributes(to) & (FileAttributes.Hidden | FileAttributes.ReadOnly)) != 0)
            Fail(VBStandardError.PathFileAccessError);
        Access(() =>
        {
            // File.Copy propagates attributes. Write bytes instead: VB6 creates an Archive copy.
            File.WriteAllBytes(to, File.ReadAllBytes(from));
            File.SetAttributes(to, FileAttributes.Archive);
        });
    }

    public void Rename(string source, string destination)
    {
        var from = Existing(source);
        NoWildcards(destination);
        var to = FullPath(destination);
        CheckOpen(from, false);
        if (string.Equals(from, to, StringComparison.Ordinal)) return;
        if (!Directory.Exists(Path.GetDirectoryName(to))) Fail(VBStandardError.FileNotFound);
        if (!SamePath(from, to) && (File.Exists(to) || Directory.Exists(to))) Fail(VBStandardError.FileAlreadyExists);
        Access(() => { if (Directory.Exists(from)) Directory.Move(from, to); else File.Move(from, to); });
    }

    private static bool Matches(string name, string pattern) => Regex.IsMatch(name,
        "\\A" + Regex.Escape(pattern == "*.*" ? "*" : pattern).Replace("\\*", ".*").Replace("\\?", ".") + "\\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private string[] Entries(string pattern, int attributes)
    {
        var full = FullPath(pattern.Length == 0 ? "." + Path.DirectorySeparatorChar : pattern);
        var trailing = Path.EndsInDirectorySeparator(full);
        var parent = trailing ? full : Path.GetDirectoryName(full)!;
        var mask = trailing ? "*" : Path.GetFileName(full);
        if (!Directory.Exists(parent)) return [];
        var names = Directory.EnumerateFileSystemEntries(parent)
            .Where(p => Matches(Path.GetFileName(p), mask))
            .Where(p =>
            {
                var flags = (int)File.GetAttributes(p);
                return (flags & 2) <= (attributes & 2) && (flags & 4) <= (attributes & 4)
                    && ((flags & 16) == 0 || (attributes & 16) != 0);
            }).Select(Path.GetFileName).Cast<string>().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        if ((attributes & 16) != 0)
        {
            if (Matches("..", mask)) names.Insert(0, "..");
            if (Matches(".", mask)) names.Insert(0, ".");
        }
        return names.ToArray();
    }

    public void Kill(string pattern)
    {
        if (pattern.Length == 0) Fail(VBStandardError.FileNotFound);
        var full = FullPath(pattern);
        var names = Entries(pattern, 0);
        if (names.Length == 0) Fail(VBStandardError.FileNotFound);
        foreach (var name in names)
        {
            var path = Path.Combine(Path.GetDirectoryName(full)!, name);
            CheckOpen(path, false);
            if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) Fail(VBStandardError.PathFileAccessError);
            Access(() => File.Delete(path));
        }
    }

    public string Dir(string? pattern, int attributes)
    {
        if (attributes < 0 || (attributes & ~63) != 0) Fail(VBStandardError.InvalidProcedureCall);
        if (pattern is not null)
        {
            listing = attributes == 8 ? [] : Entries(pattern, attributes);
            listingIndex = 0;
        }
        else if (listing is null) Fail(VBStandardError.InvalidProcedureCall);
        if (listingIndex < listing!.Length) return listing[listingIndex++];
        listing = null;
        return "";
    }
}
