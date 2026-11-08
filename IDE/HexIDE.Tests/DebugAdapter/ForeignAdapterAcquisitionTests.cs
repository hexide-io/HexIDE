using System.Security.Cryptography;

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

}
