using System.Text;
using HexIDE.IDE;

namespace HexIDE.Tests.IDE;

public sealed class AtomicFileWriterRecoveryCopiesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "hexide-recovery-" + Guid.NewGuid().ToString("N"));

    public AtomicFileWriterRecoveryCopiesTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Saving_the_same_content_leaves_no_copy()
    {
        var path = Path.Combine(_directory, "settings.json");
        AtomicFileWriter.WriteRecoverableText(path, "same");
        AtomicFileWriter.WriteRecoverableText(path, "same");
        Directory.GetFiles(_directory, "*.recovery").Should().BeEmpty();
    }

    [Fact]
    public void Only_the_newest_copies_are_kept()
    {
        var path = Path.Combine(_directory, "settings.json");
        AtomicFileWriter.WriteRecoverableText(path, "version 0");
        for (var save = 1; save <= 8; save++)
        {
            foreach (var copy in Directory.GetFiles(_directory, "*.recovery"))
                File.SetLastWriteTimeUtc(copy, File.GetLastWriteTimeUtc(copy).AddMinutes(-1));
            AtomicFileWriter.WriteRecoverableText(path, "version " + save);
        }
        Directory.GetFiles(_directory, "*.recovery").Select(copy => File.ReadAllText(copy, Encoding.UTF8))
            .Should().BeEquivalentTo("version 3", "version 4", "version 5", "version 6", "version 7");
    }
}
