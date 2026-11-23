using System.Security.Cryptography;
using System.Net.Http;
using HexIDE.Tests.LspClient;

namespace HexIDE.Tests.DebugAdapter;

public class ForeignAdapterAcquisitionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Verification_accepts_only_the_pinned_bytes(bool matching)
    {
        var directory = Path.Combine(ForeignAdapterAcquisition.RepositoryRoot(), "artifacts", "foreign-dap", "verification");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "downloaded adapter archive");
        try
        {
            var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
            Action verify = () => ForeignAdapterAcquisition.Verify(file, matching ? digest : new string('0', 64));
            if (matching) verify.Should().NotThrow();
            else verify.Should().Throw<InvalidDataException>().WithMessage("*Checksum mismatch*expected*got*Refusing to run it*");
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData("504", 2, true)]
    [InlineData("always504", 3, false)]
    [InlineData("404", 1, false)]
    [InlineData("timeout", 2, true)]
    [InlineData("connection", 2, true)]
    [InlineData("body", 2, true)]
    [InlineData("checksum", 1, false)]
    public void Download_retries_transient_failures_but_never_a_checksum_mismatch(string failure, int attempts, bool available)
    {
        byte[] bytes = [1, 2, 3];
        var digest = failure == "checksum" ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(bytes));
        using var handler = new ForeignDownloadRetryTests.StubHandler(failure, bytes);
        using var http = new HttpClient(handler);
        var archive = Path.Combine(ForeignAdapterAcquisition.RepositoryRoot(), "artifacts", "foreign-dap", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        try
        {
            Action fetch = () => ForeignAdapterAcquisition.DownloadVerified(http, "https://unused.invalid/adapter", digest, archive);
            if (available) fetch.Should().NotThrow();
            else if (failure == "checksum") fetch.Should().Throw<InvalidDataException>().WithMessage("*Checksum mismatch*Refusing to run it*");
            else fetch.Should().Throw<HttpRequestException>();
            handler.Attempts.Should().Be(attempts);
            File.Exists(archive).Should().Be(available);
            File.Exists(archive + ".partial").Should().BeFalse();
            if (available) File.ReadAllBytes(archive).Should().Equal(bytes);
        }
        finally { File.Delete(archive); File.Delete(archive + ".partial"); }
    }

    private static string[] PythonCandidates() =>
        (OperatingSystem.IsWindows() ? new[] { "python.exe", "python3.exe" } : ["python3", "python"])
        .SelectMany(name => new[] { "first", "second" }.Select(directory => Path.Combine(Path.GetFullPath(directory), name))).ToArray();

    [Fact]
    public void Python_search_continues_after_a_timeout()
    {
        var candidates = PythonCandidates();
        var tried = new List<string>();
        var result = ForeignAdapterAcquisition.FindPython(
            [Path.GetFullPath("first"), Path.GetFullPath("second")], _ => true, path =>
            {
                tried.Add(path);
                return path == candidates[0] ? (false, 0, "") : (true, 0, "Python 3.14.0");
            });
        result.Path.Should().Be(candidates[1]);
        result.Unavailable.Should().BeNull();
        tried.Should().Equal(candidates.Take(2));
    }

    [Fact]
    public void Python_search_reports_every_rejection_in_search_order()
    {
        var candidates = PythonCandidates();
        var result = ForeignAdapterAcquisition.FindPython(
            [Path.GetFullPath("first"), Path.GetFullPath("second")], _ => true, path =>
            {
                if (path == candidates[0]) return (false, 0, "");
                if (path == candidates[1]) return (true, 9009, "");
                if (path == candidates[2]) return (true, 0, "Python 2.7.18\r\nignored");
                throw new Win32Exception("probe refused");
            });
        result.Path.Should().BeNull();
        result.Unavailable.Should().Be("debugpy requires Python 3 on PATH (CI installs 3.14). Tried: " +
            $"{candidates[0]}: no exit within 5 s; {candidates[1]}: exit code 9009; " +
            $"{candidates[2]}: printed \"Python 2.7.18\"; {candidates[3]}: failed to start: Win32Exception: probe refused");
    }

    [Fact]
    public void Python_search_distinguishes_no_candidate_files()
    {
        var result = ForeignAdapterAcquisition.FindPython([Path.GetFullPath("absent")], _ => false,
            _ => throw new InvalidOperationException("No file should be probed."));
        result.Path.Should().BeNull();
        result.Unavailable.Should().Be("debugpy requires Python 3 on PATH (CI installs 3.14). No " +
            (OperatingSystem.IsWindows() ? "python.exe or python3.exe" : "python3 or python") + " on PATH.");
    }

    [Theory]
    [InlineData(199)]
    [InlineData(201)]
    public void Python_search_caps_printed_text(int length)
    {
        var candidate = PythonCandidates()[0];
        var result = ForeignAdapterAcquisition.FindPython(["first"], path => Path.GetFullPath(path) == candidate,
            _ => (true, 0, new string('x', length) + "\nignored"));
        result.Unavailable.Should().EndWith($"{candidate}: printed \"{new string('x', Math.Min(length, 200))}\"");
    }

}
