using System.Security.Cryptography;

namespace HexIDE.Tests.LspClient;

public class ForeignServerChecksumTests
{
    private static ForeignServerSource Source() => ForeignServerAcquisition.Markdown with { Key = "checksum-test" };

    [Fact]
    public void MismatchReturnsNullRecordsBothDigestsAndDeletesTheDownload()
    {
        var source = Source();
        var asset = new ForeignAsset("test", "server.zip", new string('0', 64));
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            var actual = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 }));

            ForeignServerAcquisition.VerifyDownload(source, asset, path).Should().BeNull();

            source.ChecksumMismatch.Should().Contain(asset.FileName).And.Contain(asset.Sha256).And.Contain(actual);
            File.Exists(path).Should().BeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MismatchFailsTheOrdinaryGuardEvenWhenServersAreNotRequired()
    {
        var source = Source();
        source.ChecksumMismatch = "Checksum mismatch: expected pinned, got different.";
        Action check = () => RequiredServersTests.CheckServer(source, () => null, required: false);

        check.Should().Throw<Exception>().WithMessage("*expected pinned, got different*");
    }

    [Fact]
    public void MismatchSkipExplainsWhyTheDownloadWasRefused()
    {
        var source = Source();
        source.ChecksumMismatch = "Checksum mismatch: expected pinned, got different.";
        var server = new ForeignServer(source, "UNUSED", "unused", "", "test", []);

        ForeignServerFactAttribute.SkipReason("test", server).Should()
            .Contain("the download did not match its pinned checksum");
    }
}
