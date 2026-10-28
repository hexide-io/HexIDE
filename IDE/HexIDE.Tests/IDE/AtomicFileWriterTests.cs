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
    [Fact]
    public void WriteAllText_RefusedReplacementCleansTemporaryFile()
    {
        var target = FreshPath();
        Directory.CreateDirectory(target);
        var existing = Path.Combine(target, "existing.bin");
        File.WriteAllBytes(existing, [1, 2, 3]);
        try
        {
            var write = () => AtomicFileWriter.WriteAllText(target, "replacement");
            write.Should().Throw<Exception>().Where(e => e is IOException || e is UnauthorizedAccessException);
            Directory.Exists(target).Should().BeTrue();
            File.ReadAllBytes(existing).Should().Equal(1, 2, 3);
            Directory.GetFiles(Dir, Path.GetFileName(target) + "*").Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(target, recursive: true);
            File.Delete(target + ".tmp");
        }
    }

    [Fact]
    public void PartialWriteFailurePreservesTargetAndCleansTemporaryFile()
    {
        var target = FreshPath();
        File.WriteAllBytes(target, [1, 2, 3]);
        try
        {
            var write = () => AtomicFileWriter.Write(target, stream =>
            {
                new FailingStream(stream).Write([4, 5, 6], 0, 3);
            });
            write.Should().Throw<IOException>().WithMessage("Disk full");
            File.ReadAllBytes(target).Should().Equal(1, 2, 3);
            Directory.GetFiles(Dir, Path.GetFileName(target) + "*").Should().Equal(target);
        }
        finally { File.Delete(target); }
    }

    [Fact]
    public void FlushFailurePreservesTargetAndCleansTemporaryFile()
    {
        var target = FreshPath();
        File.WriteAllBytes(target, [1, 2, 3]);
        try
        {
            var write = () => AtomicFileWriter.Write(target, stream => stream.WriteByte(4),
                path => new FlushFailureStream(path));
            write.Should().Throw<IOException>().WithMessage("Flush refused");
            File.ReadAllBytes(target).Should().Equal(1, 2, 3);
            Directory.GetFiles(Dir, Path.GetFileName(target) + "*").Should().Equal(target);
        }
        finally { File.Delete(target); }
    }

    [Fact]
    public void WriteAllText_DoesNotOverwriteAnotherTemporaryFile()
    {
        var target = FreshPath();
        File.WriteAllText(target + ".tmp", "other save");
        try
        {
            AtomicFileWriter.WriteAllText(target, "new save");
            File.ReadAllText(target + ".tmp").Should().Be("other save");
            File.ReadAllText(target).Should().Be("new save");
        }
        finally
        {
            File.Delete(target);
            File.Delete(target + ".tmp");
        }
    }

    private sealed class FlushFailureStream(string path)
        : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)
    {
        public override void Flush(bool flushToDisk)
        {
            flushToDisk.Should().BeTrue();
            throw new IOException("Flush refused");
        }
    }

    private sealed class FailingStream(Stream output) : Stream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            output.Write(buffer, offset, 1);
            throw new IOException("Disk full");
        }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => output.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
