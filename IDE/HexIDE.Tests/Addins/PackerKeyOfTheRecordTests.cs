using System.Diagnostics;
using HexIDE.AddinPacker;
using HexIDE.Addins;

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
}
