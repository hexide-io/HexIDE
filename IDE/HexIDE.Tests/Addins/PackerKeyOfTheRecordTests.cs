using System.Diagnostics;
using HexIDE.AddinPacker;
using HexIDE.Addins;
using HexIDE.Tests.Infrastructure;

namespace HexIDE.Tests.Addins;

/// <summary>
/// <c>pack</c> signs a package with the trust folder's <c>firstparty.key</c> and copies in the folder's publisher record.
/// HexIDE checks the package against the key that record names, so a key the record doesn't name makes a package every
/// HexIDE refuses. A key left over from an earlier chain did that with no error, and so would a release whose signing
/// secret and committed records came from different chains (#4109). <c>pack</c> now refuses such a key before it reads
/// the output or writes anything.
/// </summary>
public sealed class PackerKeyOfTheRecordTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hexide-packkey-" + Guid.NewGuid().ToString("N"));
    private string Trust => Path.Combine(_root, "trust");
    private string Other => Path.Combine(_root, "other");
    private string Source => Path.Combine(_root, "src");
    private string Output => Path.Combine(_root, "out");

    public PackerKeyOfTheRecordTests()
    {
        Directory.CreateDirectory(Source);
        File.WriteAllBytes(Path.Combine(Source, "probe.dll"), [1, 2, 3]);
        PackageSigner.GenerateDevTrust(Trust);
        PackageSigner.GenerateDevTrust(Other);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private void Pack() => PackageSigner.Pack(new PackRequest
    {
        SrcDir = Source, OutDir = Output, Id = "com.test.probe", Title = "Probe", Version = "1.0.0", Entry = "probe.dll",
        TrustDir = Trust,
    });

    private void UseTheOtherChainsKey() =>
        File.Copy(Path.Combine(Other, "firstparty.key"), Path.Combine(Trust, "firstparty.key"), overwrite: true);

    private static string RecordKeyFingerprint(string trust) => PackageCrypto.KeyFingerprint(
        System.Text.Json.JsonSerializer.Deserialize(File.ReadAllBytes(Path.Combine(trust, "publisher.json")),
            AddinJsonContext.Default.PublisherRecord)!.PublicKey);

    [Fact]
    public void A_key_the_publisher_record_does_not_name_is_refused_before_anything_is_written()
    {
        UseTheOtherChainsKey();

        var refusal = FluentActions.Invoking(Pack).Should().Throw<InvalidOperationException>().Which.Message;

        refusal.Should().Contain(Path.Combine(Trust, "firstparty.key")).And.Contain("Nothing was packed")
            .And.Contain(RecordKeyFingerprint(Other), "the key's public half is the other chain's publisher key")
            .And.Contain(RecordKeyFingerprint(Trust), "the record names its own")
            .And.Contain("remove it").And.Contain("get the current key");
        Directory.Exists(Output).Should().BeFalse();
        Directory.GetFileSystemEntries(_root).Select(Path.GetFileName).Should().BeEquivalentTo(["trust", "other", "src"],
            "no staging or previous folder is left beside the output");
    }

    [Fact]
    public void A_refused_key_leaves_an_earlier_package_as_it_was()
    {
        Pack();
        var before = Directory.GetFiles(Output, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(Output, path), File.ReadAllBytes);
        UseTheOtherChainsKey();

        FluentActions.Invoking(Pack).Should().Throw<InvalidOperationException>();

        var after = Directory.GetFiles(Output, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(Output, path), File.ReadAllBytes);
        after.Keys.Should().BeEquivalentTo(before.Keys);
        foreach (var (name, bytes) in before)
            after[name].Should().Equal(bytes, name);
    }

    [Fact]
    public void A_key_file_that_is_not_a_key_is_refused_without_echoing_it()
    {
        File.WriteAllText(Path.Combine(Trust, "firstparty.key"), "not a key SECRETMARKER");

        var refusal = FluentActions.Invoking(Pack).Should().Throw<InvalidOperationException>().Which.Message;

        refusal.Should().Contain("isn't a private key this tool can read").And.Contain("Nothing was packed")
            .And.NotContain("SECRETMARKER");
        Directory.Exists(Output).Should().BeFalse();
    }

    // What the Desktop build's pack targets and publish.yml see: they run the packer, and a nonzero exit fails the build.
    [Fact]
    public async Task The_packer_exits_1_and_says_what_to_do()
    {
        UseTheOtherChainsKey();
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[]
                 {
                     typeof(PackageSigner).Assembly.Location, "pack", "--src", Source, "--out", Output, "--id", "com.test.probe",
                     "--title", "Probe", "--version", "1.0.0", "--entry", "probe.dll", "--trust", Trust,
                 })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        process.ExitCode.Should().Be(1, await stdout);
        (await stderr).Should().StartWith("error: ").And.Contain("Nothing was packed").And.Contain("remove it");
        Directory.Exists(Output).Should().BeFalse();
    }

    // #5318: pack --key reads the publisher key from wherever it is kept, so a trust folder of public files is enough.
    private string MoveTheKeyOutOfTheTrustFolder()
    {
        var key = Path.Combine(_root, "private", "acme.key");
        Directory.CreateDirectory(Path.GetDirectoryName(key)!);
        File.Move(Path.Combine(Trust, "firstparty.key"), key);
        return key;
    }

    private void PackWith(string key, params string[] include) => PackageSigner.Pack(new PackRequest
    {
        SrcDir = Source, OutDir = Output, Id = "com.test.probe", Title = "Probe", Version = "1.0.0", Entry = "probe.dll",
        TrustDir = Trust, KeyFile = key, Include = [.. include],
    });

    private bool SignedByTheRecordsKey()
    {
        using var named = PackageCrypto.ImportPublicKey(System.Text.Json.JsonSerializer.Deserialize(
            File.ReadAllBytes(Path.Combine(Trust, "publisher.json")), AddinJsonContext.Default.PublisherRecord)!.PublicKey);
        return PackageCrypto.Verify(named, File.ReadAllBytes(Path.Combine(Output, "addin.json")),
            File.ReadAllText(Path.Combine(Output, "addin.sig")));
    }

    [Fact]
    public void Key_names_a_key_outside_the_trust_folder_and_it_signs_the_package()
    {
        var key = MoveTheKeyOutOfTheTrustFolder();

        PackWith(key);

        SignedByTheRecordsKey().Should().BeTrue();
        File.Exists(Path.Combine(Trust, "firstparty.key")).Should().BeFalse("the trust folder held public files only");
        Directory.GetFiles(Output).Select(Path.GetFileName).Should().NotContain(name => name!.EndsWith(".key"));
    }

    [Fact]
    public void Key_wins_over_a_leftover_firstparty_key_in_the_trust_folder()
    {
        var key = MoveTheKeyOutOfTheTrustFolder();
        File.Copy(Path.Combine(Other, "firstparty.key"), Path.Combine(Trust, "firstparty.key"));

        PackWith(key);

        SignedByTheRecordsKey().Should().BeTrue("--key is the key read, not the trust folder's");
    }

    [Fact]
    public void A_key_given_with_key_that_the_record_does_not_name_is_refused_before_anything_is_written()
    {
        MoveTheKeyOutOfTheTrustFolder();
        var wrong = Path.Combine(Other, "firstparty.key");

        var refusal = FluentActions.Invoking(() => PackWith(wrong)).Should().Throw<InvalidOperationException>().Which.Message;

        refusal.Should().Contain(wrong).And.Contain("Nothing was packed")
            .And.Contain(RecordKeyFingerprint(Other)).And.Contain(RecordKeyFingerprint(Trust));
        Directory.Exists(Output).Should().BeFalse();
    }

    [Fact]
    public void With_no_key_anywhere_it_says_where_it_looked_and_what_to_do()
    {
        MoveTheKeyOutOfTheTrustFolder();

        var refusal = FluentActions.Invoking(Pack).Should().Throw<InvalidOperationException>().Which.Message;

        refusal.Should().Contain($"There's no key at '{Path.Combine(Trust, "firstparty.key")}'")
            .And.Contain("--key").And.Contain("Nothing was packed");
        Directory.Exists(Output).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_key_named_as_a_file_to_bundle_is_refused_so_it_is_never_published(bool differentCase)
    {
        var key = Path.Combine(Source, "acme.key");
        File.Move(Path.Combine(Trust, "firstparty.key"), key);

        var refusal = FluentActions.Invoking(() => PackWith(key, differentCase ? "ACME.KEY" : "acme.key"))
            .Should().Throw<InvalidOperationException>().Which.Message;

        refusal.Should().Contain("would be published inside the package").And.Contain("Nothing was packed");
        Directory.Exists(Output).Should().BeFalse();
    }

    // A path match misses the likeliest mistake: a copy of the key in the source folder, named with --include. File.Copy
    // follows links too, so a symlink or hard link to the key has its own path as well. The content is what's checked.
    [Theory]
    [InlineData("copy")]
    [InlineData("embedded-utf8")]
    [InlineData("embedded-utf16")]
    public void A_bundled_file_holding_the_key_is_refused_whatever_its_name(string shape)
    {
        var key = MoveTheKeyOutOfTheTrustFolder();
        var text = File.ReadAllText(key).Trim();
        var copy = Path.Combine(Source, "settings.bin");
        switch (shape)
        {
            case "copy": File.Copy(key, copy); break;
            case "embedded-utf8": File.WriteAllText(copy, "prefix " + text + " suffix"); break;
            default: File.WriteAllText(copy, "prefix " + text + " suffix", System.Text.Encoding.Unicode); break;
        }

        var refusal = FluentActions.Invoking(() => PackWith(key, "settings.bin"))
            .Should().Throw<InvalidOperationException>().Which.Message;

        refusal.Should().Contain("'settings.bin' is, or holds, your private key").And.Contain("Nothing was packed")
            .And.NotContain(text, "the refusal never echoes the key");
        Directory.Exists(Output).Should().BeFalse();
    }

    public static bool CanCreateSymbolicLinks => SymbolicLinks.RunTests;

    [Fact(Skip = SymbolicLinks.SkipReason, SkipUnless = nameof(CanCreateSymbolicLinks))]
    public void A_bundled_symlink_to_the_key_is_refused()
    {
        var key = MoveTheKeyOutOfTheTrustFolder();
        File.CreateSymbolicLink(Path.Combine(Source, "linked.bin"), key);

        FluentActions.Invoking(() => PackWith(key, "linked.bin")).Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("'linked.bin' is, or holds, your private key");
        Directory.Exists(Output).Should().BeFalse();
    }

    [Fact]
    public void A_bundled_file_that_does_not_hold_the_key_still_packs()
    {
        var key = MoveTheKeyOutOfTheTrustFolder();
        File.WriteAllText(Path.Combine(Source, "settings.bin"), "nothing secret");

        PackWith(key, "settings.bin");

        SignedByTheRecordsKey().Should().BeTrue();
        File.Exists(Path.Combine(Output, "settings.bin")).Should().BeTrue();
    }

    [Fact]
    public async Task The_packer_reads_key_from_its_command_line()
    {
        var key = MoveTheKeyOutOfTheTrustFolder();
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[]
                 {
                     typeof(PackageSigner).Assembly.Location, "pack", "--src", Source, "--out", Output, "--id", "com.test.probe",
                     "--title", "Probe", "--version", "1.0.0", "--entry", "probe.dll", "--trust", Trust, "--key", key,
                 })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        process.ExitCode.Should().Be(0, await stderr);
        (await stdout).Should().Contain("Packed 'com.test.probe'");
        SignedByTheRecordsKey().Should().BeTrue();
    }
}
