using System.IO;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Projects;

public sealed class ProjectTextReadTests : IDisposable
{
    private readonly string directory = Path.Join(Path.GetTempPath(), "hexide-project-text-" + Guid.NewGuid().ToString("N"));
    private string ProjectPath => Path.Join(directory, "Test.vbp");
    private readonly IDeserializeErrorSink problems = Substitute.For<IDeserializeErrorSink>();

    public ProjectTextReadTests()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(ProjectPath, "Type=Exe\r\nName=\"Disk\"\r\n");
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public async Task Proposed_text_is_read_without_replacing_disk_bytes()
    {
        var original = File.ReadAllBytes(ProjectPath);
        const string text = "Type=Exe\r\nName=\"Edited\"\r\nTitle=\"Typed title\"\n";
        var encoding = new Vb6TextEncoding(1252);

        var read = await new ProjectLoader().LoadTextAsync(ProjectPath, text, encoding, problems);

        read.Project.Name.Should().Be("Edited");
        read.Project.AbsolutePath.Should().Be(ProjectPath);
        read.Project.TextEncoding.Should().Be(encoding);
        read.ProjectFileBytes.Should().Equal(Vb6TextFile.Encode(text, encoding));
        read.Serialized.UnknownPreSectionLines.Should().Contain(line => line.RawLine == "Title=\"Typed title\"");
        File.ReadAllBytes(ProjectPath).Should().Equal(original);
    }

    [Fact]
    public async Task Proposed_members_resolve_beside_the_real_project_path()
    {
        Directory.CreateDirectory(Path.Join(directory, "src"));
        var memberPath = Path.Join(directory, "src", "Code.bas");
        File.WriteAllText(memberPath, "Attribute VB_Name = \"Code\"\r\nSub Main()\r\nEnd Sub\r\n");
        var observed = new List<string>();
        var loader = new ProjectLoader { FileRead = (path, _) => observed.Add(path) };

        var read = await loader.LoadTextAsync(ProjectPath,
            "Type=Exe\r\nModule=Code; src\\Code.bas\r\nName=\"Edited\"\r\n", new(1252), problems);

        read.Project.Modules.Should().ContainSingle().Which.AbsolutePath.Should().Be(memberPath);
        observed.Should().ContainSingle().Which.Should().Be(memberPath);
    }

    [Fact]
    public async Task Proposed_missing_member_reports_the_existing_readers_error_without_writing()
    {
        var original = File.ReadAllBytes(ProjectPath);

        await new ProjectLoader().LoadTextAsync(ProjectPath,
            "Type=Exe\r\nModule=Missing; Missing.bas\r\n", new(1252), problems);

        problems.Received().LogError("Str.ProjectLoad.MemberNotFound", Arg.Any<string>(), Arg.Any<object[]>());
        File.ReadAllBytes(ProjectPath).Should().Equal(original);
    }

    [Fact]
    public async Task Unencodable_text_is_refused_before_reading_members()
    {
        var original = File.ReadAllBytes(ProjectPath);
        var observed = new List<string>();
        var loader = new ProjectLoader { FileRead = (path, _) => observed.Add(path) };

        var read = () => loader.LoadTextAsync(ProjectPath,
            "Type=Exe\r\nName=\"漢字\"\r\n", new(1252), problems);

        await read.Should().ThrowAsync<SourceEncodingRefusal>();
        observed.Should().BeEmpty();
        File.ReadAllBytes(ProjectPath).Should().Equal(original);
    }
}
