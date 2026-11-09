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

}
