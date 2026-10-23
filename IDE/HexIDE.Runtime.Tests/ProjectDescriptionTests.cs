using System;
using System.IO;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A project's description is read from and written to its project file, where and as VB6 writes it (#42). Project
/// Properties set it, and nothing wrote it, so a description typed there was lost at the next save; one read from a
/// file survived only as a line kept unread.
/// </summary>
/// <remarks>
/// Measured with VB6's own Save Project As: <c>Description="He said ""hi"""</c> comes back as it went in, between
/// <c>HelpContextID="0"</c> and <c>CompatibleMode="0"</c>, and a project with no description is saved with no
/// <c>Description</c> line at all. The VB98 Template's <c>Addin.vbp</c> and <c>DHTML Application.vbp</c> carry the
/// line in the same place; its four other projects, which have none, carry no line.
/// </remarks>
public class ProjectDescriptionTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "hexide-description-" + Guid.NewGuid().ToString("N"));

    public ProjectDescriptionTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string error) { }
    }

    private const string Before = "Type=Exe\r\nStartup=\"(None)\"\r\nCommand32=\"\"\r\nName=\"probe\"\r\nHelpContextID=\"0\"\r\n";
    private const string After = "CompatibleMode=\"0\"\r\nMajorVer=1\r\nMinorVer=0\r\nRevisionVer=0\r\n";

    private async Task<ProjectDefinition> Load(string text)
    {
        var path = Path.Combine(dir, "probe.vbp");
        await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);
        return (await new ProjectLoader().LoadAsync(path, new NullSink())).Project;
    }

    private string Save(ProjectDefinition project) => new ProjectSerializer().Serialize(project, Path.Combine(dir, "probe.vbp"));

    [Fact]
    public async Task A_description_is_read_and_written_back_where_it_was()
    {
        var text = Before + "Description=\"Sample AddIn Project\"\r\n" + After;

        var project = await Load(text);

        project.Description.Should().Be("Sample AddIn Project");
        Save(project).Should().Be(text);
    }

    [Fact]
    public async Task A_description_typed_in_is_written_where_VB6_writes_it()
    {
        var project = await Load(Before + After);

        project.Description = "Orders";

        Save(project).Should().Be(Before + "Description=\"Orders\"\r\n" + After);
    }

    [Fact]
    public async Task A_quote_inside_it_is_doubled_and_read_back_as_one()
    {
        var text = Before + "Description=\"He said \"\"hi\"\"\"\r\n" + After;

        var project = await Load(text);

        project.Description.Should().Be("He said \"hi\"");
        Save(project).Should().Be(text, "VB6's Save Project As wrote this line back as it read it");
    }

    [Fact]
    public async Task No_description_writes_no_line()
    {
        var project = await Load(Before + After);

        Save(project).Should().Be(Before + After);
    }

    [Fact]
    public async Task With_no_HelpContextID_a_description_typed_in_follows_Name()
    {
        const string after = "CompatibleMode=\"0\"\r\nMajorVer=1\r\n";
        var project = await Load("Type=Exe\r\nName=\"probe\"\r\n" + after);

        project.Description = "Orders";

        Save(project).Should().Be("Type=Exe\r\nName=\"probe\"\r\nDescription=\"Orders\"\r\n" + after);
    }

    [Fact]
    public async Task A_description_written_somewhere_else_stays_there()
    {
        // Not where VB6 writes it, but where this file has it: an unedited project is saved as it was read.
        var text = Before + "CompatibleMode=\"0\"\r\nDescription=\"Orders\"\r\nMajorVer=1\r\n";

        Save(await Load(text)).Should().Be(text);
    }

    [Fact]
    public async Task A_description_cleared_takes_its_line_with_it()
    {
        var project = await Load(Before + "Description=\"Orders\"\r\n" + After);

        project.Description = "";

        Save(project).Should().Be(Before + After);
    }
}
