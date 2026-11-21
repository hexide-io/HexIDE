using System.Diagnostics;
using System.IO;
using HexIDE.IDE;

namespace HexIDE.Tests.IDE;

public sealed class ProjectLocalStateTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hexide-project-state-" + Guid.NewGuid().ToString("N"));
    private string Project => Path.Combine(directory, "sample.vbp");
    private static DistributionIdentity Brand => new("rdcore", "RDCore") { ProjectFolder = ".rdcore", UserSidecar = ".user.rdproj" };
    public static bool OnWindows => OperatingSystem.IsWindows();

    public ProjectLocalStateTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public void Missing_members_and_stock_keep_the_stock_names()
    {
        var identity = DistributionDescriptor.Parse("""{"schemaVersion":1,"id":"rdcore","name":"RDCore"}"""u8.ToArray(), "brand.json", _ => []);
        identity.ProjectFolder.Should().Be(".hexide");
        identity.UserSidecar.Should().Be(".user.hexproj");
        DistributionIdentity.Stock.ProjectFolder.Should().Be(".hexide");
        DistributionIdentity.Stock.UserSidecar.Should().Be(".user.hexproj");
    }

    [Theory]
    [InlineData(true, ".rdcore", false)]
    [InlineData(true, ".hexide", false)]
    [InlineData(true, ".other", true)]
    [InlineData(false, ".user.rdproj", false)]
    [InlineData(false, ".user.hexproj", false)]
    [InlineData(false, ".user.other", true)]
    public void Own_stock_and_marked_foreign_names_are_discovered(bool folder, string name, bool marked)
    {
        var path = Candidate(folder, name, marked);
        ProjectLocalState.Find(Project, folder, Brand).Should().Equal(path);
        ProjectLocalState.Resolve(Project, folder, identity: Brand).Should().Be(path);
    }

    [Theory]
    [InlineData(".other", "")]
    [InlineData(".other", "{}")]
    [InlineData(".other", "{\"kind\":\"other\",\"version\":1}")]
    [InlineData(".other", "{\"kind\":\"hexide.project-state\",\"version\":2}")]
    [InlineData(".git", "{\"kind\":\"hexide.project-state\",\"version\":1}")]
    [InlineData(".a..b", "{\"kind\":\"hexide.project-state\",\"version\":1}")]
    public void Unmarked_wrong_or_invalid_folders_are_ignored(string name, string json)
    {
        var path = Candidate(true, name, false);
        if (json.Length > 0) File.WriteAllText(Path.Combine(path, ProjectLocalState.FolderMarker), json);
        ProjectLocalState.Find(Project, true, Brand).Should().BeEmpty();
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("{}")]
    [InlineData("{\"kind\":\"other\"}")]
    public void Unmarked_or_invalid_sidecars_are_ignored(string json)
    {
        File.WriteAllText(Path.Combine(directory, "sample.user.other"), json);
        ProjectLocalState.Find(Project, false, Brand).Should().BeEmpty();
    }

    [Theory]
    [InlineData(4 * 1024 * 1024, true)]
    [InlineData(4 * 1024 * 1024 + 1, false)]
    public void Foreign_sidecar_reads_are_bounded_to_four_MiB(int length, bool accepted)
    {
        var path = Candidate(false, ".user.other", true);
        using (var file = File.OpenWrite(path))
        {
            file.Position = file.Length;
            var padding = new byte[length - file.Length];
            Array.Fill(padding, (byte)' ');
            file.Write(padding);
        }
        ProjectLocalState.Find(Project, false, Brand).Should().HaveCount(accepted ? 1 : 0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void None_selects_own_and_two_require_a_choice(bool folder)
    {
        var own = ProjectLocalState.OwnPath(Project, folder, Brand);
        ProjectLocalState.Resolve(Project, folder, identity: Brand).Should().Be(own);
        var stock = Candidate(folder, folder ? ".hexide" : ".user.hexproj", false);
        var other = Candidate(folder, folder ? ".other" : ".user.other", true);
        var resolve = () => ProjectLocalState.Resolve(Project, folder, identity: Brand);
        resolve.Should().Throw<ProjectLocalStateConflict>().Which.Candidates.Should().BeEquivalentTo(stock, other);
        ProjectLocalState.Resolve(Project, folder, other, Brand).Should().Be(other);
        File.Exists(own).Should().BeFalse();
        Directory.Exists(own).Should().BeFalse();
    }

    [Fact]
    public void Two_marked_saves_and_recovery_copies_produce_exactly_one_candidate()
    {
        var path = Path.Combine(directory, "sample.user.other");
        AtomicFileWriter.WriteRecoverableText(path, """{"kind":"hexide.user-sidecar","version":3}""");
        AtomicFileWriter.WriteRecoverableText(path, """{"kind":"hexide.user-sidecar","version":4}""");
        Directory.GetFiles(directory, "*.recovery").Should().NotBeEmpty();
        ProjectLocalState.Find(Project, false, Brand).Should().Equal(path);
    }

    [Theory(Skip = "Windows symbolic links may require privileges; Linux runs both cases.", SkipWhen = nameof(OnWindows))]
    [InlineData(true, ".hexide")]
    [InlineData(true, ".other")]
    [InlineData(false, ".user.hexproj")]
    [InlineData(false, ".user.other")]
    public void Links_are_ignored_and_direct_writes_are_refused(bool folder, string name)
    {
        var target = Candidate(folder, folder ? "target" : ".target", true);
        var link = Path.Combine(directory, folder ? name : "sample" + name);
        if (folder) Directory.CreateSymbolicLink(link, target);
        else File.CreateSymbolicLink(link, target);
        try
        {
            ProjectLocalState.Find(Project, folder, Brand).Should().BeEmpty();
            var write = () => ProjectLocalState.EnsureRegularPath(link);
            write.Should().Throw<IOException>();
            if (folder)
            {
                var marker = File.ReadAllText(Path.Combine(target, ProjectLocalState.FolderMarker));
                var writeMarker = () => ProjectLocalState.WriteFolderMarker(link);
                writeMarker.Should().Throw<IOException>();
                File.ReadAllText(Path.Combine(target, ProjectLocalState.FolderMarker)).Should().Be(marker);
            }
        }
        finally { if (folder) Directory.Delete(link); else File.Delete(link); }
    }

    [Fact(Skip = "Directory junctions require Windows; symbolic links are covered on Linux.", SkipUnless = nameof(OnWindows))]
    public void Directory_junction_is_ignored_and_never_written_through()
    {
        var target = Candidate(true, "target", true);
        var link = Path.Combine(directory, ".hexide");
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            ArgumentList = { "/d", "/c", "mklink", "/J", link, target },
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        })!;
        process.WaitForExit();
        process.ExitCode.Should().Be(0, process.StandardError.ReadToEnd());
        try
        {
            ProjectLocalState.Find(Project, true, Brand).Should().BeEmpty();
            var write = () => ProjectLocalState.WriteFolderMarker(link);
            write.Should().Throw<IOException>();
        }
        finally { Directory.Delete(link); }
    }

    private string Candidate(bool folder, string name, bool marked)
    {
        var path = Path.Combine(directory, folder ? name : "sample" + name);
        if (folder)
        {
            Directory.CreateDirectory(path);
            if (marked) File.WriteAllText(Path.Combine(path, ProjectLocalState.FolderMarker), """{"kind":"hexide.project-state","version":1}""");
        }
        else File.WriteAllText(path, marked ? """{"kind":"hexide.user-sidecar"}""" : "{}");
        return path;
    }
}
