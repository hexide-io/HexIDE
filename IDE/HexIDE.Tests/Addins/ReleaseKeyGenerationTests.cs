using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HexIDE.AddinPacker;
using HexIDE.Addins;

namespace HexIDE.Tests.Addins;

public sealed class ReleaseKeyGenerationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hexide-release-" + Guid.NewGuid().ToString("N"));
    private string RootKeys => Path.Combine(_root, "root");
    private string IntermediateKeys => Path.Combine(_root, "intermediate");
    private string PublisherKeys => Path.Combine(_root, "publisher");
    private string Public => Path.Combine(_root, "public");

    public ReleaseKeyGenerationTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task Private_keys_are_separated_from_each_other_and_public_records()
    {
        var result = await Generate();
        result.Code.Should().Be(0, result.Error);
        Directory.GetFiles(RootKeys).Select(Path.GetFileName).Should().Equal("root.key");
        Directory.GetFiles(IntermediateKeys).Select(Path.GetFileName).Should().Equal("intermediate.key");
        Directory.GetFiles(PublisherKeys).Select(Path.GetFileName).Should().Equal("firstparty.key");
        Directory.GetFiles(Public).Select(Path.GetFileName).Should().BeEquivalentTo(new[]
        {
            "hexide-root.pub", "hexide-firstparty.pub", "intermediate.json", "intermediate.sig", "publisher.json", "publisher.sig",
        });
        var publicKeys = new[] { File.ReadAllText(Path.Combine(Public, "hexide-root.pub")),
            ReadRecord("intermediate.json").GetProperty("publicKey").GetString()!,
            File.ReadAllText(Path.Combine(Public, "hexide-firstparty.pub")) };
        foreach (var key in publicKeys)
            result.Output.Should().Contain(PackageCrypto.KeyFingerprint(key));
        var privateFiles = new[] { Path.Combine(RootKeys, "root.key"), Path.Combine(IntermediateKeys, "intermediate.key"),
            Path.Combine(PublisherKeys, "firstparty.key") };
        for (var i = 0; i < privateFiles.Length; i++)
        {
            var material = File.ReadAllText(privateFiles[i]);
            (result.Output + result.Error).Contains(material, StringComparison.Ordinal).Should().BeFalse("private keys must not be logged");
            using var key = PackageCrypto.ImportPrivateKey(material);
            PackageCrypto.ExportPublicKey(key).Should().Be(publicKeys[i]);
        }
    }

    [Fact]
    public async Task Records_use_requested_ids_and_names_without_development_labels()
    {
        var result = await Generate();
        result.Code.Should().Be(0, result.Error);
        var intermediate = ReadRecord("intermediate.json");
        intermediate.GetProperty("intermediateId").GetString().Should().Be("test.marketplace");
        intermediate.GetProperty("displayName").GetString().Should().Be("Test Marketplace");
        var publisher = ReadRecord("publisher.json");
        publisher.GetProperty("publisherId").GetString().Should().Be("test.publisher");
        publisher.GetProperty("intermediateId").GetString().Should().Be("test.marketplace");
        publisher.GetProperty("displayName").GetString().Should().Be("Test Publisher");
        // Binary key/signature encodings can coincidentally contain these three letters.
        (intermediate.GetRawText() + publisher.GetRawText()).Should().NotContain("(DEV)");
        result.Output.Should().NotContain("DEV");
    }

    [Theory]
    [InlineData("root/root.key")]
    [InlineData("intermediate/intermediate.key")]
    [InlineData("publisher/firstparty.key")]
    [InlineData("public/hexide-root.pub")]
    [InlineData("public/hexide-firstparty.pub")]
    [InlineData("public/intermediate.json")]
    [InlineData("public/intermediate.sig")]
    [InlineData("public/publisher.json")]
    [InlineData("public/publisher.sig")]
    public async Task Existing_files_survive_without_a_partial_chain(string relative)
    {
        var existing = Path.GetFullPath(Path.Combine(_root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, "keep");
        var result = await Generate();
        result.Error.Should().Contain("error:");
        result.Code.Should().Be(1);
        File.ReadAllText(existing).Should().Be("keep");
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Should().Equal(existing);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("Uppercase")]
    [InlineData("a..b")]
    [InlineData("a--b")]
    [InlineData("../bad")]
    [InlineData("bad\\id")]
    [InlineData("bad\nid")]
    [InlineData("bad.")]
    [InlineData(".bad")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Invalid_ids_are_refused_before_writing(string id)
    {
        foreach (var option in new[] { "--intermediate-id", "--firstparty-id" })
        {
            var result = await Generate(option, id);
            result.Code.Should().Be(1);
            result.Error.Should().Contain("invalid", "an unknown command would not exercise validation");
            Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" untrimmed")]
    [InlineData("untrimmed ")]
    [InlineData("bad\nname")]
    [InlineData("bad\u007fname")]
    [InlineData("bad\u202Ename")]
    [InlineData("bad\u200Bname")]
    [InlineData("bad\uFEFFname")]
    [InlineData("bad\u2028name")]
    public async Task Invalid_names_are_refused_before_writing(string name)
    {
        foreach (var option in new[] { "--intermediate-name", "--firstparty-name" })
        {
            var result = await Generate(option, name);
            result.Code.Should().Be(1);
            result.Error.Should().Contain("invalid");
            Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(128, 0)]
    [InlineData(129, 1)]
    public async Task Display_name_length_is_capped(int length, int expectedCode)
    {
        foreach (var option in new[] { "--intermediate-name", "--firstparty-name" })
        {
            var result = await Generate(option, new string('a', length));
            result.Code.Should().Be(expectedCode);
            if (expectedCode == 1) result.Error.Should().Contain("invalid");
            else
                foreach (var directory in Directory.GetDirectories(_root)) Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("a-b.c")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Valid_id_boundaries_are_accepted(string id)
    {
        var result = await Generate("--firstparty-id", id);
        result.Code.Should().Be(0, result.Error);
        ReadRecord("publisher.json").GetProperty("publisherId").GetString().Should().Be(id);
    }

    [Theory]
    [InlineData("--root-out", "intermediate")]
    [InlineData("--public-out", "root")]
    [InlineData("--public-out", "root/public")]
    public async Task Overlapping_output_folders_are_refused(string option, string relative)
    {
        var result = await Generate(option, Path.Combine(_root, relative));
        result.Code.Should().Be(1);
        result.Error.Should().Contain("overlap");
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    public static bool CanCreateSymbolicLinks => !OperatingSystem.IsWindows();

    [Fact(Skip = "Creating symbolic links on Windows requires developer mode or elevation", SkipUnless = nameof(CanCreateSymbolicLinks))]
    public async Task Linked_output_folders_cannot_alias_private_and_public_outputs()
    {
        Directory.CreateDirectory(RootKeys);
        Directory.CreateSymbolicLink(Public, RootKeys);
        var result = await Generate();
        result.Code.Should().Be(1);
        result.Error.Should().Contain("overlap");
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact(Skip = "Creating symbolic links on Windows requires developer mode or elevation", SkipUnless = nameof(CanCreateSymbolicLinks))]
    public async Task Distinct_outputs_through_a_linked_parent_are_accepted()
    {
        var alias = Path.Combine(_root, "alias");
        Directory.CreateSymbolicLink(alias, _root);
        var result = await Generate("--public-out", Path.Combine(alias, "public"));
        result.Code.Should().Be(0, result.Error);
        File.Exists(Path.Combine(Public, "hexide-root.pub")).Should().BeTrue();
        Directory.Delete(alias);
    }

    [Fact]
    public async Task Generated_chain_verifies_a_package()
    {
        var result = await Generate();
        result.Code.Should().Be(0, result.Error);
        // pack --trust consumes the public records alongside the publisher key only.
        foreach (var record in Directory.GetFiles(Public, "*.json").Concat(Directory.GetFiles(Public, "*.sig")))
            File.Copy(record, Path.Combine(PublisherKeys, Path.GetFileName(record)));
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllBytes(Path.Combine(src, "test.dll"), [1, 2, 3]);
        var package = Path.Combine(_root, "package");
        PackageSigner.Pack(new PackRequest
        {
            SrcDir = src, OutDir = package, TrustDir = PublisherKeys, Id = "test.addin",
            Title = "Test", Version = "1.0", Entry = "test.dll",
        });
        var verified = PackageVerification.Verify(package, File.ReadAllText(Path.Combine(Public, "hexide-root.pub")),
            File.ReadAllText(Path.Combine(Public, "hexide-firstparty.pub")));
        verified.IsVerified.Should().BeTrue(verified.Error ?? "");
        verified.IsFirstParty.Should().BeTrue();
        verified.PublisherId.Should().Be("test.publisher");
        verified.PublisherDisplayName.Should().Be("Test Publisher");
    }

    [Fact]
    public async Task Release_chain_issues_and_packs_first_and_third_party_packages()
    {
        var generated = await Generate();
        generated.Code.Should().Be(0, generated.Error);
        var thirdParty = Path.Combine(_root, "thirdparty");
        Directory.CreateDirectory(thirdParty);
        using var publisher = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publisherPublic = Path.Combine(thirdParty, "publisher.pub");
        File.WriteAllText(publisherPublic, PackageCrypto.ExportPublicKey(publisher));
        File.WriteAllText(Path.Combine(thirdParty, "firstparty.key"), PackageCrypto.ExportPrivateKey(publisher));
        var issued = await RunCommand("sign-publisher",
            "--root-public-key", Path.Combine(Public, "hexide-root.pub"),
            "--intermediate-key", Path.Combine(IntermediateKeys, "intermediate.key"),
            "--intermediate", Public, "--publisher-id", "test.thirdparty",
            "--display-name", "Third Party", "--public-key", publisherPublic, "--out", thirdParty);
        issued.Code.Should().Be(0, issued.Error);

        foreach (var record in new[] { "intermediate.json", "intermediate.sig", "publisher.json", "publisher.sig" })
            File.Copy(Path.Combine(Public, record), Path.Combine(PublisherKeys, record));
        foreach (var record in new[] { "intermediate.json", "intermediate.sig" })
            File.Copy(Path.Combine(Public, record), Path.Combine(thirdParty, record));

        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllBytes(Path.Combine(src, "test.dll"), [1, 2, 3]);
        var rootPublic = File.ReadAllText(Path.Combine(Public, "hexide-root.pub"));
        var firstPartyPublic = File.ReadAllText(Path.Combine(Public, "hexide-firstparty.pub"));
        TrustAnchors.RootPublicKey.Should().NotBeNullOrWhiteSpace();
        foreach (var (trust, id, firstParty) in new[]
                 { (PublisherKeys, "test.publisher", true), (thirdParty, "test.thirdparty", false) })
        {
            var package = Path.Combine(_root, id);
            var packed = await RunCommand("pack", "--src", src, "--out", package, "--id", id,
                "--title", "Test", "--version", "1.0", "--entry", "test.dll", "--trust", trust);
            packed.Code.Should().Be(0, packed.Error);
            var verified = PackageVerification.Verify(package, rootPublic, firstPartyPublic);
            verified.IsVerified.Should().BeTrue(verified.Error ?? "");
            verified.IsFirstParty.Should().Be(firstParty);
            verified.PublisherId.Should().Be(id);
            PackageVerification.Verify(package, TrustAnchors.RootPublicKey, TrustAnchors.FirstPartyPublicKey)
                .Verdict.Should().Be(PackageVerdict.Untrusted);
        }
    }

    private JsonElement ReadRecord(string name) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(Path.Combine(Public, name)));

    private async Task<(int Code, string Output, string Error)> Generate(string? option = null, string? value = null)
    {
        var arguments = new List<string>
        {
            "genkeys-release", "--root-out", RootKeys, "--intermediate-out", IntermediateKeys,
            "--firstparty-out", PublisherKeys, "--public-out", Public,
            "--intermediate-id", "test.marketplace", "--intermediate-name", "Test Marketplace",
            "--firstparty-id", "test.publisher", "--firstparty-name", "Test Publisher",
        };
        if (option is not null) arguments[arguments.IndexOf(option) + 1] = value!;
        return await RunCommand(arguments.ToArray());
    }

    private static async Task<(int Code, string Output, string Error)> RunCommand(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(PackageSigner).Assembly.Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, await stdout, await stderr);
    }
}
