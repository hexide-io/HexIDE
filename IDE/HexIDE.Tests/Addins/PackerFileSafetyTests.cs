using HexIDE.AddinPacker;

namespace HexIDE.Tests.Addins;

public sealed class PackerFileSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hexide-packer-safety-" + Guid.NewGuid().ToString("N"));

    public PackerFileSafetyTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData("intermediate.json")]
    [InlineData("intermediate.sig")]
    [InlineData("publisher.json")]
    [InlineData("publisher.sig")]
    [InlineData("hexide-root.pub")]
    [InlineData("hexide-firstparty.pub")]
    [InlineData("firstparty.key")]
    [InlineData("root.key")]
    [InlineData("intermediate.key")]
    public void Generating_trust_preserves_existing_output(string name)
    {
        var output = Path.Combine(_root, "output");
        Directory.CreateDirectory(output);
        var existing = Path.Combine(output, name);
        File.WriteAllText(existing, "user data");

        var generate = () => PackageSigner.GenerateDevTrust(output);

        generate.Should().Throw<IOException>();
        File.ReadAllText(existing).Should().Be("user data");
        Directory.GetFiles(output).Should().Equal(existing);
    }

    [Theory]
    [InlineData("revocations.json")]
    [InlineData("revocations.sig")]
    public void Signing_revocations_preserves_existing_output(string name)
    {
        var trust = Path.Combine(_root, "trust");
        PackageSigner.GenerateDevTrust(trust);
        var input = Path.Combine(_root, "input.json");
        File.WriteAllText(input, "{}");
        var output = Path.Combine(_root, "output");
        Directory.CreateDirectory(output);
        var existing = Path.Combine(output, name);
        File.WriteAllText(existing, "user data");

        var sign = () => PackageSigner.SignRevocations(input, Path.Combine(trust, "root.key"), output);

        sign.Should().Throw<IOException>();
        File.ReadAllText(existing).Should().Be("user data");
        Directory.GetFiles(output).Should().Equal(existing);
    }
}
