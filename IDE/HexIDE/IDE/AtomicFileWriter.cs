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
