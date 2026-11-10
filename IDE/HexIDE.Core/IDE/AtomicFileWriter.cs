using System.Linq;
using System.IO;
using System.Text;

namespace HexIDE.IDE;

/// <summary>Writes and flushes a sibling temporary file before replacing the destination.</summary>
public static class AtomicFileWriter
{
    public static void WriteAllText(string path, string content) =>
        WriteAllBytes(path, new UTF8Encoding(false, true).GetBytes(content));

    public static void WriteAllBytes(string path, byte[] content) =>
        Write(path, stream => stream.Write(content));

    public static void Copy(string source, string path) =>
        Write(path, stream =>
        {
            using var input = File.OpenRead(source);
            input.CopyTo(stream);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(((FileStream)stream).SafeFileHandle, File.GetUnixFileMode(input.SafeFileHandle));
        });

    /// <summary>Retains the previous disk bytes in a unique recovery sibling before saving.</summary>
    public static void WriteRecoverableText(string path, string content) =>
        WriteRecoverableBytes(path, new UTF8Encoding(false, true).GetBytes(content));

    public static void WriteRecoverableBytes(string path, byte[] content)
    {
        if (File.Exists(path) && !HasContent(path, content))
        {
            using var input = File.OpenRead(path);
            var recovery = RecoveryPath(path);
            var created = false;
            try
            {
                using var output = new FileStream(recovery, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                created = true;
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
            catch
            {
                // A partial recovery copy is not usable. The destination has not been touched.
                try { if (created) File.Delete(recovery); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }
        WriteAllBytes(path, content);
        PruneRecoveryCopies(path);
    }

    private static bool HasContent(string path, byte[] content) =>
        new FileInfo(path).Length == content.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(content);

    // Settings and session state are saved constantly, and each copy would otherwise stay in the user's folder for good.
    private static void PruneRecoveryCopies(string path)
    {
        var directory = Path.GetDirectoryName(path);
        var stale = new DirectoryInfo(string.IsNullOrEmpty(directory) ? "." : directory)
            .GetFiles(Path.GetFileName(path) + ".*.recovery")
            .OrderByDescending(file => file.LastWriteTimeUtc).Skip(RecoveryCopiesKept);
        foreach (var file in stale)
            try { file.Delete(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
    }

    /// <summary>Removes the active file by moving it to a recovery sibling, without replacing anything.</summary>
    public static void DeleteRecoverable(string path)
    {
        if (File.Exists(path))
            File.Move(path, RecoveryPath(path));
    }

    private const int RecoveryCopiesKept = 5;

    private static string RecoveryPath(string path) => path + "." + Guid.NewGuid().ToString("N") + ".recovery";

    public static void Write(string path, Action<Stream> write) => Write(path, write, null);

    // Scoped to the caller's execution context so fault injection cannot affect another save.
    internal static readonly AsyncLocal<Func<string, FileStream>?> OpenStream = new();

    internal static void Write(string path, Action<Stream> write, Func<string, FileStream>? open)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = (open ?? OpenStream.Value)?.Invoke(tmp) ?? new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            // A cleanup refusal must not hide the write error the caller reports.
            try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
