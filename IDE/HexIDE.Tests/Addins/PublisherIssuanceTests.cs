using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HexIDE.AddinPacker;
using HexIDE.Addins;

namespace HexIDE.Tests.Addins;

public sealed class PublisherIssuanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hexide-issuer-" + Guid.NewGuid().ToString("N"));
    private string Trust => Path.Combine(_root, "trust");
    private string Output => Path.Combine(_root, "issued");

    public PublisherIssuanceTests()
    {
        PackageSigner.GenerateDevTrust(Trust);
        using var publisher = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(Path.Combine(_root, "publisher.pub"), PackageCrypto.ExportPublicKey(publisher));
        File.WriteAllText(Path.Combine(_root, "publisher.key"), PackageCrypto.ExportPrivateKey(publisher));
    }

    public void Dispose() => Directory.Delete(_root, true);

    private (int ExitCode, string Message) Issue(string id = "org.example.publisher", string name = "Example Publisher",
        string? intermediateKey = null, string? rootKey = null, string? publicKey = null, string? namespaces = null)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { typeof(PackageSigner).Assembly.Location, "sign-publisher",
                     "--root-public-key", rootKey ?? Path.Combine(Trust, "hexide-root.pub"),
                     "--intermediate-key", intermediateKey ?? Path.Combine(Trust, "intermediate.key"),
                     "--intermediate", Trust, "--publisher-id", id, "--display-name", name,
                     "--public-key", publicKey ?? Path.Combine(_root, "publisher.pub"), "--out", Output })
            start.ArgumentList.Add(arg);
        if (namespaces is not null)
        {
            start.ArgumentList.Add("--namespaces");
            start.ArgumentList.Add(namespaces);
        }
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout + stderr);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Issued_publisher_verifies_package_and_rejects_tampering(bool tamper)
    {
        var result = Issue();
        result.ExitCode.Should().Be(0, result.Message);
        result.Message.Should().Contain("Verified");
        foreach (var name in new[] { "publisher.json", "publisher.sig" })
            File.Copy(Path.Combine(Output, name), Path.Combine(Trust, name), true);
        File.Copy(Path.Combine(_root, "publisher.key"), Path.Combine(Trust, "firstparty.key"), true);
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "entry.dll"), "payload");
        var package = Path.Combine(_root, "package");
        PackageSigner.Pack(new PackRequest { SrcDir = src, OutDir = package, TrustDir = Trust,
            Id = "example", Title = "Example", Version = "1.0", Entry = "entry.dll" });
        if (tamper) File.AppendAllText(Path.Combine(package, "publisher.json"), " ");
        PackageVerification.Verify(package, File.ReadAllText(Path.Combine(Trust, "hexide-root.pub")))
            .Verdict.Should().Be(tamper ? PackageVerdict.Untrusted : PackageVerdict.Verified);
    }

    // #4258 part D: the namespaces a publisher is issued come only from its signed record.
    private PackageVerificationResult PackAndVerify(Action<string>? alterPackage = null)
    {
        foreach (var name in new[] { "publisher.json", "publisher.sig" })
            File.Copy(Path.Combine(Output, name), Path.Combine(Trust, name), true);
        File.Copy(Path.Combine(_root, "publisher.key"), Path.Combine(Trust, "firstparty.key"), true);
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "entry.dll"), "payload");
        var package = Path.Combine(_root, "package");
        PackageSigner.Pack(new PackRequest { SrcDir = src, OutDir = package, TrustDir = Trust,
            Id = "example", Title = "Example", Version = "1.0", Entry = "entry.dll" });
        alterPackage?.Invoke(package);
        return PackageVerification.Verify(package, File.ReadAllText(Path.Combine(Trust, "hexide-root.pub")));
    }

    [Fact]
    public void Issued_namespaces_reach_the_verified_result()
    {
        var issued = Issue(namespaces: "rdcore., rdcore.extra.");
        issued.ExitCode.Should().Be(0, issued.Message);
        var result = PackAndVerify();
        result.Verdict.Should().Be(PackageVerdict.Verified);
        result.PublisherNamespaces.Should().Equal("rdcore.", "rdcore.extra.");
    }

    [Fact]
    public void A_record_without_namespaces_verifies_with_none()
    {
        Issue().ExitCode.Should().Be(0);
        File.ReadAllText(Path.Combine(Output, "publisher.json")).Should().NotContain("namespaces",
            "a record without the field stays byte-for-byte as before");
        var result = PackAndVerify();
        result.Verdict.Should().Be(PackageVerdict.Verified);
        result.PublisherNamespaces.Should().BeNull();
    }

    [Theory]
    [InlineData("hexide.")]
    [InlineData("HexIDE.tools.")]
    [InlineData("rdcore")]
    [InlineData("rdcore/")]
    [InlineData(".rdcore.")]
    public void A_namespace_that_cannot_be_issued_is_refused(string ns)
    {
        var issued = Issue(namespaces: ns);
        issued.ExitCode.Should().NotBe(0);
        issued.Message.Should().Contain($"'{ns}' is not a namespace that can be issued");
        File.Exists(Path.Combine(Output, "publisher.json")).Should().BeFalse();
    }

    [Fact]
    public void Namespaces_added_to_a_signed_record_make_it_untrusted()
    {
        Issue(namespaces: "rdcore.").ExitCode.Should().Be(0);
        var result = PackAndVerify(package =>
        {
            var path = Path.Combine(package, "publisher.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"rdcore.\"", "\"rdcore.\", \"other.\""));
        });
        result.Verdict.Should().Be(PackageVerdict.Untrusted);
        result.PublisherNamespaces.Should().BeNull();
    }

    [Fact]
    public void A_signed_record_claiming_hexide_grants_only_its_other_namespaces()
    {
        // The issuer refuses it, so sign one by hand with the intermediate's key, as a mis-issuing intermediate could.
        using var publisher = PackageCrypto.ImportPublicKey(File.ReadAllText(Path.Combine(_root, "publisher.pub")).Trim());
        var record = new PublisherRecord
        {
            PublisherId = "org.example.publisher",
            DisplayName = "Example Publisher",
            PublicKey = PackageCrypto.ExportPublicKey(publisher),
            IntermediateId = JsonSerializer.Deserialize(File.ReadAllBytes(Path.Combine(Trust, "intermediate.json")),
                AddinJsonContext.Default.IntermediateRecord)!.IntermediateId,
            Namespaces = ["hexide.", "hexide.chat.", "rdcore.", "not a namespace"],
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, AddinJsonContext.Default.PublisherRecord);
        using var intermediate = PackageCrypto.ImportPrivateKey(File.ReadAllText(Path.Combine(Trust, "intermediate.key")).Trim());
        Directory.CreateDirectory(Output);
        File.WriteAllBytes(Path.Combine(Output, "publisher.json"), bytes);
        File.WriteAllText(Path.Combine(Output, "publisher.sig"), PackageCrypto.Sign(intermediate, bytes));
        var result = PackAndVerify();
        result.Verdict.Should().Be(PackageVerdict.Verified);
        result.PublisherNamespaces.Should().Equal("rdcore.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("org..example")]
    [InlineData("org/example")]
    [InlineData("org\\example")]
    [InlineData("C:example")]
    [InlineData("Org.example")]
    public void Invalid_id_is_refused(string id)
    {
        var result = Issue(id: id);
        result.ExitCode.Should().Be(1);
        result.Message.Should().Contain("publisher id");
        Directory.Exists(Output).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" Example")]
    [InlineData("Example\nPublisher")]
    public void Invalid_name_is_refused(string name)
    {
        var result = Issue(name: name);
        result.ExitCode.Should().Be(1);
        result.Message.Should().Contain("display name");
        Directory.Exists(Output).Should().BeFalse();
    }

    // Invisible format characters would let one publisher's name read as another's in the trust inspector:
    // a right-to-left override, a zero-width space, a byte-order mark, a line separator.
    [Theory]
    [InlineData(0x202E)]
    [InlineData(0x200B)]
    [InlineData(0xFEFF)]
    [InlineData(0x2028)]
    public void Name_with_an_invisible_format_character_is_refused(int code)
    {
        var result = Issue(name: "Example" + (char)code + "Publisher");
        result.ExitCode.Should().Be(1);
        result.Message.Should().Contain("display name");
        Directory.Exists(Output).Should().BeFalse();
    }

    [Fact]
    public void Overlong_id_and_name_are_refused()
    {
        Issue(id: new string('a', 65)).Message.Should().Contain("publisher id");
        Issue(name: new string('a', 129)).Message.Should().Contain("display name");
        Directory.Exists(Output).Should().BeFalse();
    }

    [Fact]
    public void Wrong_curve_is_refused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        File.WriteAllText(Path.Combine(_root, "publisher.pub"), PackageCrypto.ExportPublicKey(key));
        var result = Issue();
        result.ExitCode.Should().Be(1);
        result.Message.Should().Contain("public key");
        Directory.Exists(Output).Should().BeFalse();
    }

    [Fact]
    public void Mismatched_intermediate_is_refused()
    {
        var result = Issue(intermediateKey: Path.Combine(Trust, "firstparty.key"));
        result.ExitCode.Should().Be(1);
        result.Message.Should().Contain("does not match");
        Directory.Exists(Output).Should().BeFalse();
    }

    [Fact]
    public void Wrong_root_is_refused_before_writing()
    {
        var result = Issue(rootKey: Path.Combine(_root, "publisher.pub"));
        result.ExitCode.Should().Be(1);
        result.Message.Should().Contain("intermediate signature");
        Directory.Exists(Output).Should().BeFalse();
    }

    [Fact]
    public void Invalid_public_key_is_refused_without_echoing_input()
    {
        File.WriteAllText(Path.Combine(_root, "publisher.pub"), "secret-marker");
        var result = Issue();
        result.ExitCode.Should().Be(1);
        result.Message.Should().Contain("public key").And.NotContain("secret-marker");
        Directory.Exists(Output).Should().BeFalse();
    }

    [Theory]
    [InlineData("publisher.json")]
    [InlineData("publisher.sig")]
    public void Existing_output_is_preserved(string name)
    {
        Directory.CreateDirectory(Output);
        var existing = Path.Combine(Output, name);
        File.WriteAllText(existing, "user data");
        var result = Issue();
        result.ExitCode.Should().Be(1);
        result.Message.Should().Contain("error:");
        File.ReadAllText(existing).Should().Be("user data");
        Directory.GetFiles(Output).Should().Equal(existing);
    }
}
