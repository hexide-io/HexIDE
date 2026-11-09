using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace HexIDE.Tests.LspClient;

public class ForeignDownloadRetryTests
{
    [Theory]
    [InlineData("504", 2, true)]
    [InlineData("always504", 3, false)]
    [InlineData("404", 1, false)]
    [InlineData("timeout", 2, true)]
    [InlineData("connection", 2, true)]
    [InlineData("body", 2, true)]
    [InlineData("checksum", 1, false)]
    public void Acquisition_retries_only_transient_fetches(string failure, int attempts, bool available)
    {
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("stub" + (OperatingSystem.IsWindows() ? ".exe" : "")).Open()))
            writer.Write("stub server");
        var bytes = archive.ToArray();
        var digest = failure == "checksum" ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(bytes));
        var key = "retry-test-" + Guid.NewGuid().ToString("N");
        var source = new ForeignServerSource(key, "1", "stub", "https://unused.invalid/{0}/{1}",
            DigestProvenance.ComputedHere, ForeignServerAcquisition.Markdown.Assets
                .Select(asset => new ForeignAsset(asset.Rid, "stub.zip", digest)).ToArray());
        using var handler = new StubHandler(failure, bytes);
        using var http = new HttpClient(handler);
        var directory = Path.Combine(ForeignServerAcquisition.CacheRoot(), key);
        try
        {
            var result = ForeignServerAcquisition.Acquire(source, http);
            (result is not null).Should().Be(available);
            handler.Attempts.Should().Be(attempts);
            if (failure == "checksum")
            {
                source.ChecksumMismatch.Should().Contain("Refusing to run it");
                Action guard = () => RequiredServersTests.CheckServer(source, () => result, required: false);
                guard.Should().Throw<Exception>().WithMessage("*Checksum mismatch*");
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private sealed class DroppedBody : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            stream.WriteByte(99);
            throw new HttpIOException(HttpRequestError.ResponseEnded, "stub dropped response body");
        }

        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    internal sealed class StubHandler(string failure, byte[] bytes) : HttpMessageHandler
    {
        internal int Attempts { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            if (failure == "always504" || (failure == "504" && Attempts == 1))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.GatewayTimeout));
            if (failure == "404") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (Attempts == 1 && failure == "timeout") throw new TaskCanceledException("stub timeout");
            if (Attempts == 1 && failure == "connection")
                throw new HttpRequestException(HttpRequestError.ConnectionError, "stub dropped connection");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = failure == "body" && Attempts == 1 ? new DroppedBody() : new ByteArrayContent(bytes),
            });
        }
    }
}
