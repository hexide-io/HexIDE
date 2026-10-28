using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Net.Sockets;
using HexIDE.IDE.Profiles;

namespace HexIDE.Tests.Profiles;

public class ProfileReaderSafetyTests
{
    public static bool IsLinux => OperatingSystem.IsLinux();

    private const int Limit = 1024 * 1024;

    [Theory]
    [InlineData("con")]
    [InlineData("nul")]
    [InlineData("aux")]
    [InlineData("prn")]
    [InlineData("com1")]
    [InlineData("com2")]
    [InlineData("com3")]
    [InlineData("com4")]
    [InlineData("com5")]
    [InlineData("com6")]
    [InlineData("com7")]
    [InlineData("com8")]
    [InlineData("com9")]
    [InlineData("lpt1")]
    [InlineData("lpt2")]
    [InlineData("lpt3")]
    [InlineData("lpt4")]
    [InlineData("lpt5")]
    [InlineData("lpt6")]
    [InlineData("lpt7")]
    [InlineData("lpt8")]
    [InlineData("lpt9")]
    public void DeviceNamesAreNotBundleIdsOnAnyPlatform(string id) =>
        ProfileCatalog.IsBundleId(id).Should().BeFalse();

    [Theory]
    [InlineData("profile.json", "$")]
    [InlineData("theme.json", "theme")]
    [InlineData("settings.json", "settingsDefaults")]
    public void OversizedFileHasAttributedTooLargeError(string name, string field)
    {
        WithDirectory(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "profile.json"),
                """{"schemaVersion":1,"id":"example","theme":"theme.json","settingsDefaults":"settings.json"}""");
            File.WriteAllText(Path.Combine(directory, "theme.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{}");
            File.WriteAllBytes(Path.Combine(directory, name), new byte[Limit + 1]);
            var error = ((Action)(() => ProfileBundleLoader.Load(directory)))
                .Should().Throw<InvalidDataException>().Which;
            error.Data["code"].Should().Be("TooLarge");
            error.Data["file"].Should().Be(Path.Combine(directory, name));
            error.Data["field"].Should().Be(field);
        });
    }

    [Fact]
    public void GrowingFileIsBoundedDuringRead()
    {
        WithDirectory(directory =>
        {
            var path = Path.Combine(directory, "growing.json");
            File.WriteAllBytes(path, new byte[8192]);
            using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            using var stream = new GrowingStream(file);
            ((Action)(() => ProfileFileReader.ReadBytes(stream))).Should()
                .Throw<InvalidDataException>().WithMessage("TooLarge");
            stream.BytesRead.Should().Be(Limit + 1);
        });
    }

    [Fact]
    public void ExactlyOneMiBIsAllowed()
    {
        using var stream = new MemoryStream(new byte[Limit]);
        ProfileFileReader.ReadBytes(stream).Length.Should().Be(Limit);
    }

    [Fact(Skip = "Linux special-file checks.", SkipUnless = nameof(IsLinux))]
    public void LinuxFifoIsRefusedWithoutBlocking()
    {
        WithDirectory(directory =>
        {
            var start = new ProcessStartInfo("mkfifo") { UseShellExecute = false };
            start.ArgumentList.Add(Path.Combine(directory, "fifo"));
            using var process = Process.Start(start)!;
            process.WaitForExit();
            process.ExitCode.Should().Be(0);
            ((Action)(() => ProfileFileReader.Read(directory, "fifo"))).Should()
                .Throw<InvalidDataException>().WithMessage("UnsafePath");
        });
    }

    [Fact(Skip = "Linux special-file checks.", SkipUnless = nameof(IsLinux))]
    public void LinuxDeviceIsRefused()
    {
        ((Action)(() => ProfileFileReader.Read("/dev", "null"))).Should()
            .Throw<InvalidDataException>().WithMessage("UnsafePath");
    }

    [Fact(Skip = "Linux special-file checks.", SkipUnless = nameof(IsLinux))]
    public void LinuxSocketIsRefused()
    {
        WithDirectory(directory =>
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Bind(new UnixDomainSocketEndPoint(Path.Combine(directory, "socket")));
            ((Action)(() => ProfileFileReader.Read(directory, "socket"))).Should()
                .Throw<InvalidDataException>().WithMessage("UnsafePath");
        });
    }

    [WindowsOnlyFact]
    public void WindowsPipeHandleIsRefused()
    {
        using var pipe = new AnonymousPipeServerStream(PipeDirection.Out);
        using var handle = new SafeFileHandle(pipe.SafePipeHandle.DangerousGetHandle(), ownsHandle: false);
        ((Action)(() => ProfileFileReader.ValidateHandle(handle))).Should()
            .Throw<InvalidDataException>().WithMessage("UnsafePath");
    }

    [Theory(Skip = "Linux handle fallback.", SkipUnless = nameof(IsLinux))]
    [InlineData(false)]
    [InlineData(true)]
    public void LinuxFallbackReadsRegularFile(bool unsupportedArchitecture)
    {
        WithDirectory(directory =>
        {
            var path = Path.Combine(directory, "regular.json");
            File.WriteAllText(path, "{}");
            using var handle = File.OpenHandle(path);
            Action validate = () => ProfileFileReader.ValidateHandle(handle,
                (_, _) => throw (unsupportedArchitecture
                    ? new InvalidOperationException("Unsupported stat layout must not be used.")
                    : new EntryPointNotFoundException()),
                unsupportedArchitecture ? Architecture.Arm : Architecture.X64);
            validate.Should().NotThrow();
            handle.IsClosed.Should().BeFalse();
            using var stream = new FileStream(handle, FileAccess.Read);
            ProfileFileReader.ReadBytes(stream).Should().Equal((byte)'{', (byte)'}');
        });
    }

    [Theory(Skip = "Linux handle fallback.", SkipUnless = nameof(IsLinux))]
    [InlineData(false)]
    [InlineData(true)]
    public void LinuxFallbackRefusesNonSeekableHandle(bool unsupportedArchitecture)
    {
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In);
        using var handle = new SafeFileHandle(pipe.SafePipeHandle.DangerousGetHandle(), ownsHandle: false);
        Action validate = () => ProfileFileReader.ValidateHandle(handle,
            (_, _) => throw (unsupportedArchitecture
                ? new InvalidOperationException("Unsupported stat layout must not be used.")
                : new EntryPointNotFoundException()),
            unsupportedArchitecture ? Architecture.Arm : Architecture.X64);
        validate.Should().Throw<InvalidDataException>().WithMessage("UnsafePath");
        handle.IsClosed.Should().BeFalse();
    }

    private sealed class GrowingStream(FileStream file) : Stream
    {
        public int BytesRead { get; private set; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = file.Read(buffer, offset, count);
            if (BytesRead == 0)
                file.SetLength(Limit * 2L);
            BytesRead += read;
            return read;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => file.Length;
        public override long Position { get => file.Position; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static void WithDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-reader-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, true); }
    }
}
