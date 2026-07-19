using System.IO;

namespace HexIDE.IDE;

/// <summary>Writes a file atomically (write to a sibling <c>.tmp</c>, then replace) so a process kill
/// mid-write cannot leave a torn/half-written file. Used by the eager dock-layout save.</summary>
public static class AtomicFileWriter
{
    public static void WriteAllText(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true); // atomic replace on the same volume
    }
}
