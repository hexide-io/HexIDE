using System.Text;
using System.Text.Json;
using HexIDE.Desktop.Server;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Infrastructure;

public sealed class ArchiveFileToolsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hexide-1118-" + Guid.NewGuid().ToString("N"));
    private readonly IDocumentDockService dock = Substitute.For<IDocumentDockService>();
    private readonly IProjectManager projects = Substitute.For<IProjectManager>();
    private readonly List<BaseEditorWindowViewModel> tabs = [];
    private readonly ArchiveProjectService archives;
    private readonly IdeContext context;
    private readonly HexIdeTools tools;

    public ArchiveFileToolsTests()
    {
        Directory.CreateDirectory(directory);
        projects.LoadedProjects.Returns(Array.Empty<ProjectDefinition>());
        dock.OpenDocuments.Returns(tabs);
        dock.When(d => d.OpenDocument(Arg.Any<BaseEditorWindowViewModel>(), Arg.Any<bool>()))
            .Do(call => tabs.Add(call.Arg<BaseEditorWindowViewModel>()));
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        archives = new ArchiveProjectService(dock, Substitute.For<IRecentProjectsService>(), localization,
            Substitute.For<IWindowManager>(), new HexIDE.Runtime.Interpreter.ConditionalCompilation.InterpreterConditionalCode());
        context = new IdeContext(projects, dock, Substitute.For<ILspClient>(), null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader(), archives: archives);
        tools = new HexIdeTools(context);
    }

    [AvaloniaFact]
    public async Task Info_lists_archive_paths_entries_and_each_ordinary_projects_own_file()
    {
        var path = Write("one.twinproj", "main.twin");
        await archives.Open(path);
        var ordinary = new ProjectDefinition(VBProjectType.EXE, "VBProject") { AbsolutePath = Path.Combine(directory, "VBProject.vbp") };
        projects.LoadedProjects.Returns([ordinary]);
        var reply = await tools.GetProjectInfoAsync(TestContext.Current.CancellationToken);
        var json = JsonSerializer.SerializeToElement(reply);
        var listed = json.GetProperty("Projects").EnumerateArray().ToArray();
        listed.Should().HaveCount(2);
        listed[0].GetProperty("ProjectFile").GetString().Should().Be(ordinary.AbsolutePath);
        listed[1].GetProperty("Path").GetString().Should().Be(path);
        listed[1].GetProperty("ProjectFile").GetString().Should().Be(path);
        listed[1].GetProperty("Entries").EnumerateArray().Select(e => e.GetString()).Should().Equal("/main.twin");
        reply.Note.Should().Contain("archive").And.NotContain("No project is loaded");
    }

    [AvaloniaFact]
    public async Task An_entry_opens_read_only_and_the_reply_names_its_container_and_entry()
    {
        var path = Write("one.twinproj", "main.twin");
        await archives.Open(path);
        var reply = await tools.OpenFileAsync("/main.twin");
        reply.Success.Should().BeTrue(reply.Error);
        reply.Project.Should().Be(path);
        reply.Document.Should().Be("/main.twin");
        reply.Note.Should().Contain("read-only").And.Contain("language server");
        var tab = tabs.Should().ContainSingle().Which.Should().BeOfType<ArchiveDocumentViewModel>().Which;
        tab.Document.Text.Should().Be("first\nsecond");
        tab.IsReadOnlyDocument.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task Duplicate_entry_paths_refuse_without_opening_and_project_selects_the_archive()
    {
        var one = Write("one.twinproj", "main.twin");
        var two = Write("two.twinproj", "main.twin");
        await archives.Open(one);
        await archives.Open(two);
        var ambiguous = await tools.OpenFileAsync("/main.twin");
        ambiguous.Success.Should().BeFalse();
        ambiguous.Error.Should().Contain(one).And.Contain(two).And.Contain("project");
        tabs.Should().BeEmpty();
        var chosen = await tools.OpenFileAsync("/main.twin", project: two);
        chosen.Success.Should().BeTrue(chosen.Error);
        chosen.Project.Should().Be(two);
        tabs.Should().ContainSingle().Which.Should().BeOfType<ArchiveDocumentViewModel>().Which.Entry.Identity.ContainerPath.Should().Be(two);
    }

    [AvaloniaFact]
    public async Task An_unknown_name_lists_archive_paths_it_can_open()
    {
        var path = Write("one.twinproj", "main.twin");
        await archives.Open(path);
        var reply = await tools.OpenFileAsync("missing");
        reply.Success.Should().BeFalse();
        reply.Error.Should().Contain(path).And.Contain("/main.twin").And.Contain("get_project_info")
            .And.NotContain("No project loaded");
        projects.LoadedProjects.Returns([new ProjectDefinition(VBProjectType.EXE, "Ordinary")]);
        var scoped = await tools.OpenFileAsync("missing", project: path);
        scoped.Success.Should().BeFalse();
        scoped.Error.Should().Contain(path).And.Contain("/main.twin").And.NotContain("No loaded project");
        tabs.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(2, 3, true)]
    [InlineData(3, 1, false)]
    public async Task Entry_caret_is_validated_before_opening(int line, int column, bool valid)
    {
        await archives.Open(Write("one.twinproj", "main.twin"));
        var reply = await tools.OpenFileAsync("/main.twin", line: line, column: column);
        reply.Success.Should().Be(valid, reply.Error);
        if (valid)
        {
            var tab = tabs.Should().ContainSingle().Which.Should().BeOfType<ArchiveDocumentViewModel>().Which;
            tab.CaretOffset.Should().Be(8);
            reply.Note.Should().Contain("line 2, column 3");
        }
        else
        {
            reply.Error.Should().Contain("line 3");
            tabs.Should().BeEmpty();
        }
    }

    [AvaloniaFact]
    public async Task Entry_paths_escape_segment_separators_and_round_trip_through_info()
    {
        await archives.Open(Write("one.twinproj", "a/b%.twin"));
        var info = JsonSerializer.SerializeToElement(await tools.GetProjectInfoAsync(TestContext.Current.CancellationToken));
        var path = info.GetProperty("Projects")[0].GetProperty("Entries")[0].GetString()!;
        path.Should().Be("/a%2Fb%25.twin");
        var reply = await tools.OpenFileAsync(path);
        reply.Success.Should().BeTrue(reply.Error);
        tabs.Should().ContainSingle().Which.Should().BeOfType<ArchiveDocumentViewModel>().Which.Entry.Name.Should().Be("a/b%.twin");
    }

    [AvaloniaTheory]
    [InlineData("one.twinproj")]
    [InlineData("Project")]
    public async Task Archive_project_selector_accepts_the_file_or_stored_name(string selector)
    {
        await archives.Open(Write("one.twinproj", "main.twin"));
        var reply = await tools.OpenFileAsync("/main.twin", project: selector);
        reply.Success.Should().BeTrue(reply.Error);
        tabs.Should().ContainSingle();
    }

    [AvaloniaFact]
    public async Task Unsupported_preview_can_be_inspected_but_not_given_a_caret()
    {
        await archives.Open(Write("one.twinproj", "form.tbform"));
        var refused = await tools.OpenFileAsync("/form.tbform", line: 1);
        refused.Success.Should().BeFalse();
        refused.Error.Should().Contain("UnsupportedType");
        tabs.Should().BeEmpty();
        var opened = await tools.OpenFileAsync("/form.tbform");
        opened.Success.Should().BeTrue(opened.Error);
        tabs.Should().ContainSingle().Which.Should().BeOfType<ArchiveDocumentViewModel>().Which.Notice.Should().Contain("UnsupportedType");
    }

    private string Write(string file, string entry)
    {
        var path = Path.Combine(directory, file);
        using var writer = new BinaryWriter(File.Create(path), Encoding.UTF8);
        writer.Write(0xEA0BA51Cu);
        Header("Project");
        writer.Write(1u);
        Header(entry);
        var bytes = Encoding.UTF8.GetBytes("first\nsecond");
        writer.Write((uint)bytes.Length);
        writer.Write(bytes);
        writer.Write(0u);
        return path;

        void Header(string name)
        {
            writer.Write((short)1);
            var bytes = Encoding.UTF8.GetBytes(name);
            writer.Write((uint)bytes.Length);
            writer.Write(bytes);
            writer.Write(1ul);
            writer.Write(0u);
            writer.Write((byte)0);
        }
    }

    public void Dispose()
    {
        foreach (var tab in tabs) tab.Dispose();
        context.Dispose();
        Directory.Delete(directory, recursive: true);
    }
}
