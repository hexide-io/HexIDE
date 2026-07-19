using System.IO;
using HexIDE.IDE;

namespace HexIDE.Tests.IDE;

public class AtomicFileWriterTests
{
    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "hexide-atomic-tests");

    private static string FreshPath()
    {
        Directory.CreateDirectory(Dir);
        var p = Path.Combine(Dir, Path.GetRandomFileName() + ".json");
        if (File.Exists(p)) File.Delete(p);
        return p;
    }

    [Fact]
    public void WriteAllText_WritesContent()
    {
        var p = FreshPath();
        AtomicFileWriter.WriteAllText(p, "hello");
        File.ReadAllText(p).Should().Be("hello");
        File.Delete(p);
    }

    [Fact]
    public void WriteAllText_OverwritesExistingFile()
    {
        var p = FreshPath();
        AtomicFileWriter.WriteAllText(p, "first");
        AtomicFileWriter.WriteAllText(p, "second");
        File.ReadAllText(p).Should().Be("second");
        File.Delete(p);
    }

    [Fact]
    public void WriteAllText_LeavesNoTempFileBehind()
    {
        var p = FreshPath();
        AtomicFileWriter.WriteAllText(p, "x");
        File.Exists(p + ".tmp").Should().BeFalse();
        File.Delete(p);
    }

    [Fact]
    public void WriteAllText_CreatesMissingDirectory()
    {
        var root = Path.Combine(Dir, Path.GetRandomFileName());
        var nested = Path.Combine(root, "sub", "layout.json");
        AtomicFileWriter.WriteAllText(nested, "y");
        File.ReadAllText(nested).Should().Be("y");
        Directory.Delete(root, recursive: true);
    }
}
