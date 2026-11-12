using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Desktop.Server;
using HexIDE.Tests.Infrastructure;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using HexIDE.Language;
using HexIDE.Lsp;
using HexIDE.Recovery;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.Projects;

/// <summary>
/// What the IDE does with a project's files through a format that isn't VB6's (#3564). None of the files here is anything a
/// VB6 reader could take, and the stand-in's renders are nothing a VB6 writer would produce, so each assertion can only pass
/// by way of the interface.
/// </summary>
public class ProjectFileFormatSeamTests : CompositionTestBase, IDisposable
{
    private readonly string _dir = Path.Join(Path.GetTempPath(), "hexide-format-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> _loaded = [];
    private readonly IProjectManager _projects = Substitute.For<IProjectManager>();
    private readonly IWindowManager _windows = Substitute.For<IWindowManager>();
    private readonly FileBaselineStore _baselines = new();
    private readonly StandInFormat _format = new();
    private readonly string _vbp;
    private readonly string _frm;
    private readonly string _bas;

    public ProjectFileFormatSeamTests()
    {
        Directory.CreateDirectory(_dir);
        _vbp = Path.Join(_dir, "Test.vbp");
        _frm = Path.Join(_dir, "Form1.frm");
        _bas = Path.Join(_dir, "Module1.bas");
        File.WriteAllText(_vbp, "not a project file");
        File.WriteAllText(_frm, "not a form");
        File.WriteAllText(_bas, "not a module");
        _projects.LoadedProjects.Returns(_ => _loaded);
        _projects.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
            .Do(call => _loaded.Add(call.Arg<ProjectDefinition>()));
        _windows.ShowDialog(Arg.Any<IDialog>()).Returns(call =>
        {
            if (call.Arg<IDialog>() is SaveChangesViewModel prompt) prompt.Yes();
            return Task.FromResult(true);
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private ProjectService Service(IReadOnlyRegionWriter? writer = null)
    {
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns("{0}");
        return new ProjectService(writer ?? new HexIDE.Runtime.Serialization.Vb6ReadOnlyRegionWriter(), () => throw new InvalidOperationException("no new-project dialog here"), _windows,
            Substitute.For<IEventBus>(), _projects, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), sidecar, _baselines, localization,
            Substitute.For<IHeaderRefresher>(), format: _format, new ComponentRegistry());
    }

    private async Task<ProjectService> Opened(string? path = null)
    {
        var service = Service();
        await service.OpenProject(path ?? _vbp);
        return service;
    }

    private List<string> Messages() => _windows.ReceivedCalls()
        .Where(call => call.GetMethodInfo().Name == nameof(IWindowManager.MessageBox))
        .Select(call => (string)call.GetArguments()[0]!)
        .ToList();

    [Fact]
    public async Task A_project_is_what_the_format_read()
    {
        _format.Skipped = ["Doc1.dob"];

        await Opened();

        var project = _loaded.Single();
        project.Forms.Single().Name.Should().Be("Form1");
        project.Modules.Single().Name.Should().Be("Module1");
        Messages().Should().Contain(message => message.Contains("Doc1.dob"));
        _baselines.TryGet(_frm).Should().NotBeNull("what the read saw is what a later change is compared with");
        _baselines.TryGet(_vbp).Should().NotBeNull();
    }

    [Fact]
    public async Task A_group_is_what_the_format_read()
    {
        var vbg = Path.Join(_dir, "Group.vbg");
        File.WriteAllText(vbg, "not a group");
        _format.Group = new("Test.vbp", ["Test.vbp"], ["a line nothing models"]);

        await Opened(vbg);

        _loaded.Should().ContainSingle();
        _projects.CurrentGroup!.UnknownLines.Should().Equal("a line nothing models");
    }

    [Fact]
    public async Task A_form_reloads_from_its_text_and_companion_through_the_format()
    {
        var service = await Opened();
        var form = _loaded.Single().Forms.Single();
        File.WriteAllText(_frm, "changed elsewhere");
        File.WriteAllBytes(Path.ChangeExtension(_frm, ".standin"), [7, 8]);

        (await service.ReloadFormFromDisk(form)).Should().BeTrue();

        form.Code.Should().Be("code of changed elsewhere");
        _format.FormsRead.Single().Companion.Should().Equal(7, 8);
    }

    [Fact]
    public async Task A_module_reloads_through_the_format()
    {
        var service = await Opened();
        var module = _loaded.Single().Modules.Single();
        File.WriteAllText(_bas, "changed elsewhere");

        (await service.ReloadModuleFromDisk(module)).Should().BeTrue();

        module.OriginalHeader.Should().Be("header of changed elsewhere");
        module.Code.Should().Be("body of changed elsewhere");
    }

    [Fact]
    public async Task A_designer_module_reloads_with_the_companion_the_format_names()
    {
        var service = await Opened();
        var project = _loaded.Single();
        var path = Path.Join(_dir, "Ctl1.ctl");
        File.WriteAllText(path, "changed elsewhere");
        File.WriteAllBytes(Path.ChangeExtension(path, ".standin"), [3]);
        var module = new ModuleDefinition(project, "Ctl1", ModuleKind.UserControl) { AbsolutePath = path };
        project.AddModule(module);

        (await service.ReloadModuleFromDisk(module)).Should().BeTrue();

        _format.DesignerPartsRead.Single().Companion.Should().Equal(3);
        module.Code.Should().Be("code of changed elsewhere");
    }

    [Fact]
    public async Task An_existing_form_is_added_through_the_format()
    {
        var service = await Opened();
        var project = _loaded.Single();
        var path = Path.Join(_dir, "Form2.frm");
        File.WriteAllText(path, "another");
        _format.FormName = "Form2";

        await service.AddExistingForm(project, path);

        project.Forms.Should().Contain(form => form.Name == "Form2" && form.Code == "code of another");
    }

    [Fact]
    public async Task An_existing_module_is_added_through_the_format()
    {
        var service = await Opened();
        var project = _loaded.Single();
        var path = Path.Join(_dir, "Module2.bas");
        File.WriteAllText(path, "another");

        await service.AddExistingModule(project, path, ModuleKind.StandardModule);

        project.Modules.Should().Contain(module => module.Name == "Module2" && module.Code == "body of another");
    }

    [Fact]
    public async Task Recovered_work_goes_back_through_the_format()
    {
        var service = await Opened();
        var project = _loaded.Single();

        service.ApplyRecovered(project.Forms.Single(), new UnsavedFile("Form1.frm", "recovered", "Form1.standin", [5])).Should().BeTrue();
        service.ApplyRecovered(project.Modules.Single(), new UnsavedFile("Module1.bas", "recovered")).Should().BeTrue();

        project.Forms.Single().Code.Should().Be("code of recovered");
        _format.FormsRead.Single().Companion.Should().Equal(5);
        project.Modules.Single().Code.Should().Be("body of recovered");
    }

    [Fact]
    public async Task A_recovered_form_with_no_place_in_the_project_is_added_through_the_format()
    {
        var service = await Opened();
        var project = _loaded.Single();
        _format.FormName = "Form2";

        var added = service.AddRecovered(project,
            new UnsavedDocument("Form2", "Form", null, Vb6TextEncoding.Ansi, new UnsavedFile("Form2.frm", "recovered")));

        added.Should().BeOfType<FormDefinition>().Which.Code.Should().Be("code of recovered");
    }

    [Fact]
    public async Task An_unsaved_project_comes_back_through_the_format()
    {
        var service = Service();
        var renderedAt = Path.Join(Path.GetTempPath(), "hexide-format-restore-" + Guid.NewGuid().ToString("N"), "Test.vbp");

        var project = await service.RestoreUnsavedProject(new UnsavedProject("Test", null, null, Vb6TextEncoding.Ansi,
            new UnsavedFile("Test.vbp", "not a project file"), [], renderedAt));

        project!.Forms.Single().Name.Should().Be("Form1");
        project.AbsolutePath.Should().BeNull();
    }

    [Fact]
    public void A_raw_property_shows_as_binary_when_the_format_says_it_cites_the_companion()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Tool.Properties.Binary").Returns("(Binary)");
        _format.Citation = "P = cites it";

        new RawPropertyViewModel(new("P", ["P = cites it"]), localization, _format).Value.Should().Be("(Binary)");
        new RawPropertyViewModel(new("P", ["P = plain"]), localization, _format).Value.Should().Be("plain");
    }

    [Fact]
    public async Task A_document_is_edited_exactly_when_the_format_renders_it_differently()
    {
        var service = await Opened();
        var project = _loaded.Single();

        service.HasUnsavedChanges(project.Forms.Single()).Should().BeFalse();
        service.HasUnsavedChanges(project.Modules.Single()).Should().BeFalse();

        _format.Stamp = "two";

        service.HasUnsavedChanges(project.Forms.Single()).Should().BeTrue();
        service.HasUnsavedChanges(project.Modules.Single()).Should().BeTrue();
    }

    [Fact]
    public async Task A_save_writes_what_the_format_rendered()
    {
        var service = await Opened();
        _format.Stamp = "two";

        await service.SaveProject(_loaded.Single(), saveAs: false);

        File.ReadAllText(_frm).Should().Be("form Form1 as Form1.frm: two");
        File.ReadAllText(_bas).Should().Be("module Module1 as Module1.bas: two");
        File.ReadAllText(_vbp).Should().Be("project Test: two");
    }

    [Fact]
    public async Task A_save_writes_the_companion_the_format_rendered_where_the_format_says()
    {
        var service = await Opened();
        _format.Companion = [1, 2, 3];

        await service.SaveProject(_loaded.Single(), saveAs: false);

        File.ReadAllBytes(Path.ChangeExtension(_frm, ".standin")).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task A_group_file_is_what_the_format_rendered()
    {
        var vbg = Path.Join(_dir, "Group.vbg");
        File.WriteAllText(vbg, "not a group");
        _format.Group = new("Test.vbp", ["Test.vbp"], []);
        var service = await Opened(vbg);

        await service.SaveAllProjects(saveAs: false);

        File.ReadAllText(vbg).Should().Be("group of 1: one");
    }

    [Fact]
    public async Task The_code_page_check_is_of_the_text_the_format_rendered()
    {
        var service = await Opened();
        var form = _loaded.Single().Forms.Single();
        form.TextEncoding = new Vb6TextEncoding(1252);
        var before = File.ReadAllText(_frm);
        _format.Stamp = "こ";

        Func<Task> save = () => service.SaveForm(form, saveAs: false, interactive: false);

        await save.Should().ThrowAsync<SourceEncodingRefusal>();
        File.ReadAllText(_frm).Should().Be(before);
    }

    [Fact]
    public async Task Crash_recovery_keeps_what_the_format_rendered()
    {
        var service = await Opened();
        _format.Stamp = "two";

        var captured = service.CaptureUnsavedWork().Single();

        captured.ProjectFile!.Text.Should().Be("project Test: two");
        captured.Documents.Should().Contain(document => document.Content!.Text == "form Form1 as Form1.frm: two");
        captured.Documents.Should().Contain(document => document.Content!.Text == "module Module1 as Module1.bas: two");
    }

    [Fact]
    public async Task A_designer_change_takes_the_header_the_format_rendered()
    {
        await Opened();
        var form = _loaded.Single().Forms.Single();
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel>());
        using var refresher = new HeaderRefresher(new HexIDE.Runtime.Serialization.Vb6ReadOnlyRegionWriter(), new EventBus(), dock, new BreakpointService(), new BookmarkService(), _format);

        refresher.LayoutChanged(form);

        form.DesignerText.Should().Be("header as Form1.frm: one");
    }

    [Theory]
    [InlineData("one", ReloadDecision.CleanReload)]
    [InlineData("two", ReloadDecision.Conflict)]
    public void An_unopened_module_is_compared_with_the_file_as_the_format_renders_it(string stamp, ReloadDecision expected)
    {
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Module1", ModuleKind.StandardModule);
        var baseline = new FileBaselineStore();
        baseline.Record(_bas, "module Module1 as Module1.bas: one");
        _format.Stamp = stamp;

        new DirtyDetector(baseline, Substitute.For<IProjectService>(), _format)
            .Classify(new WatchedFileTarget(_bas, null, module, null, null))
            .Should().Be(expected);
    }

    [AvaloniaFact]
    public async Task A_language_servers_project_text_is_what_the_format_rendered()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { AbsolutePath = _vbp };
        _projects.LoadedProjects.Returns([project]);
        var workspace = new ProjectLspWorkspace(() => _projects, _format);

        var text = await workspace.Projects.Single().ProjectText!(TestContext.Current.CancellationToken);

        text.Should().Be("project Test: one");
        workspace.Dispose();
    }

    [Fact]
    public void A_tools_write_protects_the_companion_the_format_names()
    {
        var project = TestHelpers.CreateProjectWithForm("Project1");
        project.AbsolutePath = _vbp;
        var form = project.Forms[0];
        form.AbsolutePath = _frm;
        var companion = Path.ChangeExtension(_frm, ".standin");
        File.WriteAllBytes(companion, [1, 2, 3]);

        ProjectWriteSafety.Prepare(DocumentIdentity.For(form), _format, out var note).Should().BeNull();

        note.Should().Contain(companion);
        Directory.GetFiles(_dir, "Form1.standin.hexide-recovery-*").Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_rewrites_read_the_formats_prefix(bool moduleFile)
    {
        var writer = Substitute.For<IReadOnlyRegionWriter>();
        writer.SaveRewrites(Arg.Any<string>(), Arg.Any<int>()).Returns(Array.Empty<TextChange>());
        var service = Service(writer);
        await service.OpenProject(_vbp);
        var project = _loaded.Single();
        var form = project.Forms.Single();
        var module = project.Modules.Single();
        form.UpdateCode("plain form code");
        module.UpdateCode("plain module code");
        writer.ClearReceivedCalls();

        if (moduleFile) await service.SaveModule(module, saveAs: false, interactive: false);
        else service.SerializeFormToFile(form, _frm, ownCommand: true).Should().BeTrue();

        var prefix = moduleFile ? _format.CodePrefix(module) : _format.CodePrefix(form);
        writer.Received(1).SaveRewrites(prefix + (moduleFile ? module.Code : form.Code), prefix.Length);
    }

    [Fact]
    public async Task Header_refresh_moves_marks_from_the_formats_current_prefix()
    {
        await Opened();
        var form = _loaded.Single().Forms.Single();
        form.RecordDesignerText("old\r\nold\r\nold\r\n");
        var document = DocumentIdentity.For(form);
        var breakpoints = new BreakpointService();
        var bookmarks = new BookmarkService();
        breakpoints.SetDocument(document, [5]);
        bookmarks.SetBookmarks(document, [4]);
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel>());
        using var refresher = new HeaderRefresher(new Vb6ReadOnlyRegionWriter(), new EventBus(), dock,
            breakpoints, bookmarks, _format);

        refresher.ApplyHeader(form, "new\r\nnew\r\n");

        breakpoints.GetBreakpoints(document).Should().Equal(6);
        bookmarks.GetBookmarks(document).Should().Equal(5);
    }

    [Theory]
    [InlineData(false, ModuleKind.StandardModule)]
    [InlineData(true, ModuleKind.UserControl)]
    public void A_closed_modules_name_is_in_the_place_the_format_says(bool carriesName, ModuleKind kind)
    {
        _format.CarriesName = carriesName;
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "New", kind);
        module.UpdateCode("title Old");
        var writer = Substitute.For<IReadOnlyRegionWriter>();
        writer.NameRewrite("title Old", 0, "New").Returns(new TextChange(6, 3, "New"));
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel>());
        using var refresher = new HeaderRefresher(writer, new EventBus(), dock, new BreakpointService(),
            new BookmarkService(), _format);

        refresher.NameChanged(DocumentIdentity.For(module));

        module.Code.Should().Be(carriesName ? "title Old" : "title New");
    }

    [AvaloniaFact]
    public void An_open_modules_rename_refreshes_the_formats_prefix_when_it_carries_the_name()
    {
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Old", ModuleKind.UserControl);
        using var editor = new CodeEditorViewModel(_windows, Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), new EventBus(), Substitute.For<ILspClient>(),
            Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(), new BookmarkService(),
            new BreakpointService(), Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<IRunScope>(), new LocalizationService(), new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader());
        editor.Initialize(module);
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel> { editor });
        using var refresher = new HeaderRefresher(Substitute.For<IReadOnlyRegionWriter>(), new EventBus(), dock,
            new BreakpointService(), new BookmarkService(), _format);
        module.Name = "New";

        refresher.NameChanged(DocumentIdentity.For(module));

        editor.Document.Text.Should().Be(_format.CodePrefix(module) + module.Code);
    }

    private sealed class PrefixReader : IReadOnlyRegionReader
    {
        public IReadOnlyList<TextRegion> Regions(string text, int prefixLength)
        {
            var end = text.IndexOf("plain", StringComparison.Ordinal);
            var locked = text.IndexOf("sealed", StringComparison.Ordinal);
            var regions = new List<TextRegion>();
            if (text.StartsWith("prefix of ", StringComparison.Ordinal) && end > 0)
                regions.Add(new TextRegion(0, end));
            if (locked >= 0) regions.Add(new TextRegion(locked, locked + "sealed\r\n".Length, Anchor: locked - 2));
            return regions;
        }
    }

    private CodeEditorViewModel Window(EventBus? events = null, IReadOnlyRegionReader? reader = null) => new(_windows, Substitute.For<IEditorService>(),
        Substitute.For<IProjectService>(), events ?? new EventBus(), Substitute.For<ILspClient>(),
        Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(), new BookmarkService(),
        new BreakpointService(), Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
        Substitute.For<IRunScope>(), new LocalizationService(), _format, reader ?? new PrefixReader());

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_code_window_and_its_undo_use_the_formats_prefix(bool moduleFile)
    {
        var project = TestHelpers.CreateProjectWithForm("P");
        var module = TestHelpers.CreateModule(project, "Item");
        var form = project.Forms.Single();
        using var editor = Window();
        if (moduleFile) { module.UpdateCode("plain\r\n"); editor.Initialize(module); }
        else { form.UpdateCode("plain\r\n"); editor.Initialize(form); }
        var prefix = moduleFile ? _format.CodePrefix(module) : _format.CodePrefix(form);
        editor.Document.Text.Should().Be(prefix + "plain\r\n");
        _format.Stamp = "two";
        var next = moduleFile ? _format.CodePrefix(module) : _format.CodePrefix(form);
        editor.RefreshPrefix(next);

        editor.UndoRequested(editor.Document.UndoStack.Undo);

        editor.Document.Text.Should().Be(next + "plain\r\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Closed_text_and_project_find_use_the_formats_prefix(bool moduleFile)
    {
        var project = TestHelpers.CreateProjectWithForm("P");
        var module = TestHelpers.CreateModule(project, "Item");
        project.AddModule(module);
        var form = project.Forms.Single();
        form.UpdateCode("plain form");
        module.UpdateCode("plain module");
        var document = moduleFile ? DocumentIdentity.For(module) : DocumentIdentity.For(form);
        var expected = moduleFile ? _format.CodePrefix(module) + module.Code : _format.CodePrefix(form) + form.Code;
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns([]);
        _projects.LoadedProjects.Returns([project]);

        DocumentContent.Read(document, null, _format, new PrefixReader()).Should().Be(expected);
        var find = new FindProjectDocuments(dock, _projects, Substitute.For<IEditorService>(), () => project,
            _format, new PrefixReader());
        find.GetDocuments(false).Should().Contain(d => d.Document.Text == expected);
    }

    [Fact]
    public void A_closed_write_uses_the_formats_prefix()
    {
        var module = TestHelpers.CreateModule();
        module.UpdateCode("plain old");
        var rules = Substitute.For<IReadOnlyRegionRules>();
        var prefix = _format.CodePrefix(module);
        rules.Replace(prefix + module.Code, prefix.Length, "plain new").Returns(new ContentReplacement(prefix + "plain new", null, true));

        DocumentContent.Write(DocumentIdentity.For(module), null, "plain new", rules, _format, reader: new PrefixReader()).Refusal.Should().BeNull();

        module.Code.Should().Be("plain new");
        rules.Received(1).Replace(prefix + "plain old", prefix.Length, "plain new");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mark_lines_use_the_reader_on_open_and_closed_documents(bool open)
    {
        var module = TestHelpers.CreateModule();
        module.UpdateCode("title\r\nplain\r\nsealed\r\nlast\r\n");
        using var editor = Window();
        editor.Initialize(module);

        var text = DocumentContent.TextOf(DocumentIdentity.For(module), open ? editor : null, _format, new PrefixReader());

        text.HeaderLineCount.Should().Be(2);
        MarkLineRules.Refusal(text, "P/Item", [1], [], 1, "breakpoint").Should().Contain("code starts at 3");
        MarkLineRules.Refusal(text, "P/Item", [4], [], 1, "breakpoint").Should().NotBeNull();
        MarkLineRules.Refusal(text, "P/Item", [3, 5], [], 1, "breakpoint").Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_omitted_leading_text_is_decided_by_the_reader_and_the_formats_name_rule(bool carriesName)
    {
        _format.CarriesName = carriesName;
        var module = TestHelpers.CreateModule(kind: ModuleKind.UserControl);
        module.UpdateCode("title\r\nplain\r\n");

        DocumentContent.LeftOutLeadingRegion(DocumentIdentity.For(module), "plain\r\n", _format, new PrefixReader()).HasValue
            .Should().Be(!carriesName);
    }

    private sealed class FirstLineReader : IReadOnlyRegionReader
    {
        public IReadOnlyList<TextRegion> Regions(string text, int prefixLength) =>
            [new TextRegion(0, text.IndexOf('\n') + 1)];
    }

    [AvaloniaFact]
    public void Event_navigation_starts_after_the_readers_leading_region()
    {
        var project = TestHelpers.CreateProjectWithForm("P");
        var form = project.Forms.Single();
        _format.Stamp = "one\r\nSub Button_Click()\r\nEnd Sub\r\n";
        form.UpdateCode("plain\r\nSub Button_Click()\r\nEnd Sub\r\n");
        var events = new EventBus();
        using var editor = Window(events, new FirstLineReader());
        editor.Initialize(form);

        events.Publish(new CreateOrNavigateToSubEvent(form, "Button_Click"));

        editor.CaretOffset.Should().BeLessThan(editor.Document.Text.IndexOf("plain", StringComparison.Ordinal));
        editor.CaretOffset.Should().BeGreaterThan(editor.Document.Text.IndexOf("Sub Button_Click", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reload_moves_marks_by_the_formats_prefix_change()
    {
        var form = TestHelpers.CreateProjectWithForm("P").Forms.Single();
        var marks = new BreakpointService();
        var document = DocumentIdentity.For(form);
        marks.SetDocument(document, [5]);
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns([]);
        using var refresher = new HeaderRefresher(new Vb6ReadOnlyRegionWriter(), new EventBus(), dock,
            marks, new BookmarkService(), _format);
        var reloader = new FileReloader(Substitute.For<IProjectService>(), new EventBus(),
            Substitute.For<IStatusBarService>(), new LocalizationService(), refresher, _format);

        await reloader.ReplaceAsync(new WatchedFileTarget("Item.frm", form, null, null, null), () =>
        {
            _format.Stamp = "two\r\nextra";
            return Task.FromResult(true);
        }, announce: false);

        marks.GetBreakpoints(document).Should().Equal(6);
    }

    [Fact]
    public void References_in_closed_documents_read_the_formats_prefix()
    {
        var project = TestHelpers.CreateProjectWithForm("P");
        var module = TestHelpers.CreateModule(project, "Item");
        project.AddModule(module);
        module.UpdateCode("plain module");
        _projects.LoadedProjects.Returns([project]);
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns([]);
        var results = new HexIDE.Tools.FindReferencesToolViewModel(Substitute.For<ILspClient>(),
            Substitute.For<IEditorService>(), _projects, dock, new LocalizationService(), _format);

        results.ShowLocations([new HexIDE.Lsp.Messages.Location(DocumentWireName.For(DocumentIdentity.For(module)),
            new HexIDE.Lsp.Messages.Range(new(0, 0), new(0, 6)))]);

        results.Rows[1].Highlight.Should().Be("prefix");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Sidecar_marks_and_conditions_use_the_readers_leading_region(int version)
    {
        _format.Stamp = "one\r\nextra";
        var project = TestHelpers.CreateProjectWithForm("P");
        project.AbsolutePath = _vbp;
        var module = TestHelpers.CreateModule(project, "Item");
        module.AbsolutePath = _bas;
        project.AddModule(module);
        module.UpdateCode("title\r\nplain\r\nsealed\r\nlast\r\n");
        var marks = version == 1 ? "2,3,4,5" : version == 2 ? "2,4,5,6,7" : "1,2,3,4";
        var key = version == 3 ? "codeBreakpoints" : version == 2 ? "fileBreakpoints" : "breakpoints";
        File.WriteAllText(Path.ChangeExtension(_vbp, ".user.hexproj"), """
            {"version":VERSION,"KEY":{"Item":[MARKS]},"codeBreakpointConditions":{"Item":{"1":"True"}}}
            """.Replace("VERSION", version.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("KEY", key).Replace("MARKS", marks));
        var breakpoints = new BreakpointService();
        var sidecar = new UserSidecarService(new BookmarkService(), breakpoints, _projects, _format, new PrefixReader())
            { Debounce = _ => Task.CompletedTask };

        await sidecar.LoadAsync(project);

        var document = DocumentIdentity.For(module);
        breakpoints.GetBreakpoints(document).Should().Equal(version == 2 ? new[] { 2, 4, 6, 7 } : [4, 6, 7]);
        breakpoints.GetCondition(document, 4).Should().Be("True");
        await sidecar.SaveAsync(project);
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(_vbp, ".user.hexproj")));
        json.RootElement.GetProperty("codeBreakpoints").GetProperty("Item").EnumerateArray()
            .Select(n => n.GetInt32()).Should().Equal(1, 3, 4);
        json.RootElement.GetProperty("codeBreakpointConditions").GetProperty("Item").GetProperty("1")
            .GetString().Should().Be("True");
    }

    [AvaloniaTheory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task A_tools_omission_note_uses_the_formats_phrase(bool open, bool words)
    {
        var project = TestHelpers.CreateProjectWithForm("P");
        var form = project.Forms.Single();
        project.AbsolutePath = _vbp;
        form.AbsolutePath = _frm;
        form.UpdateCode("title\r\nplain\r\n");
        _projects.LoadedProjects.Returns([project]);
        var dock = Substitute.For<IDocumentDockService>();
        using var editor = Window();
        editor.Initialize(form);
        editor.Document.Text = "plain\r\n";
        dock.OpenDocuments.Returns(open ? new List<BaseEditorWindowViewModel> { editor } : []);
        var service = Substitute.For<IProjectService>();
        service.SaveForm(form, false, false, Arg.Any<SaveRewriteReport?>()).Returns(Task.FromResult(true));
        var rules = Substitute.For<IReadOnlyRegionRules>();
        rules.Replace(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>()).Returns(c => new ContentReplacement(c.ArgAt<string>(0), null, true));
        rules.DescribeRegions(_format.CodePrefix(form) + form.Code, _format.CodePrefix(form).Length,
            Arg.Is<IReadOnlyList<TextRegion>>(r => r.Count == 1 && r[0].Start == 0 && r[0].End == (_format.CodePrefix(form) + "title\r\n").Length))
            .Returns(words ? "the title block" : null);
        using var context = new IdeContext(_projects, dock, Substitute.For<ILspClient>(), null!,
            Substitute.For<IProjectRunnerService>(), service, new EventBus(), new BookmarkService(),
            new BreakpointService(), null!, null!, null!, null!, null!, null!, null!, null!, new LocalizationService(),
            null!, _format, new PrefixReader(), rules);

        var result = await new HexIdeTools(context).SetFileContentAsync(form.Name, "plain\r\n");

        result.Success.Should().BeTrue(result.Error);
        result.Note.Should().Contain($"Kept {(words ? "the title block" : "read-only text")}, which the content left out. Call get_file_content first and edit what it returns, which includes it.");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tool_reads_and_breakpoints_consult_the_format_and_reader(bool open)
    {
        var project = TestHelpers.CreateProjectWithForm("P");
        var form = project.Forms.Single();
        form.UpdateCode("title\r\nplain\r\nsealed\r\nlast\r\n");
        _projects.LoadedProjects.Returns([project]);
        using var editor = Window();
        editor.Initialize(form);
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(open ? new List<BaseEditorWindowViewModel> { editor } : []);
        var breakpoints = new BreakpointService();
        using var context = new IdeContext(_projects, dock, Substitute.For<ILspClient>(), null!,
            Substitute.For<IProjectRunnerService>(), Substitute.For<IProjectService>(), new EventBus(),
            new BookmarkService(), breakpoints, null!, Composition.Root, null!, null!, null!, null!, null!, null!,
            new LocalizationService(), null!, _format, new PrefixReader());
        var tools = new HexIdeTools(context);

        (await tools.GetFileContentAsync(form.Name)).Content.Should().Be(_format.CodePrefix(form) + form.Code);
        (await tools.SetBreakpointsAsync(form.Name, [1])).Error.Should().Contain("code starts at 3");
        (await tools.SetBreakpointsAsync(form.Name, [4])).Success.Should().BeFalse();
        (await tools.SetBreakpointsAsync(form.Name, [3, 5])).Success.Should().BeTrue();
        breakpoints.GetBreakpoints(DocumentIdentity.For(form)).Should().Equal(3, 5);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_rename_reads_a_closed_targets_prefix_from_the_format(bool moduleFile)
    {
        var project = TestHelpers.CreateProjectWithForm("P");
        var form = project.Forms.Single();
        var module = TestHelpers.CreateModule(project, "Item");
        var identity = moduleFile ? DocumentIdentity.For(module) : DocumentIdentity.For(form);
        if (moduleFile) module.UpdateCode("plain"); else form.UpdateCode("plain");
        using var target = Window();
        if (moduleFile) target.Initialize(module); else target.Initialize(form);
        var editors = Substitute.For<IEditorService>();
        var uri = DocumentWireName.For(identity);
        editors.CodeDocumentForUri(uri).Returns(identity);
        editors.OpenCodeForUri(uri).Returns(target);
        var sourceModule = TestHelpers.CreateModule(project, "Source");
        using var source = new CodeEditorViewModel(_windows, editors, Substitute.For<IProjectService>(),
            new EventBus(), Substitute.For<ILspClient>(), Substitute.For<ISettingsService>(),
            Substitute.For<IStatusBarService>(), new BookmarkService(), new BreakpointService(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(), Substitute.For<IRunScope>(),
            new LocalizationService(), _format, new PrefixReader());
        source.Initialize(sourceModule);
        var edit = new HexIDE.Lsp.Messages.WorkspaceEdit(new()
        {
            [uri] = [new(new(new(1, 0), new(1, 5)), "other")]
        });

        source.ApplyRename(edit, "plain").Should().BeNull();

        target.Document.Text.Should().EndWith("other");
    }

    [Fact]
    public void Only_the_format_names_the_readers_and_writers()
    {
        var names = new Regex(@"\b(FormSerializer|ProjectSerializer|GroupSerializer|FormDeserializer|FrxDeserializer|ProjectDeserializer|GroupDeserializer|ProjectLoader)\b"
                              + @"|\bModuleFileFormat\s*\.\s*(ToFileContent|SplitHeader)\b");
        // A contributed project's module is split by name until the add-in type reads modules itself: #3411's slice 7.
        ProductSource.FilesMatching(names, "Vb6ProjectFileFormat.cs", "ContributedProjects.cs")
            .Should().BeEmpty("the IDE reaches a project's file formats through IProjectFileFormat");
    }

    [Fact]
    public void Only_the_composition_root_chooses_the_format()
    {
        ProductSource.FilesMatching(new Regex(@"\bVb6ProjectFileFormat\b"), "Vb6ProjectFileFormat.cs", "DISetup.cs")
            .Should().BeEmpty("a class is given its format, so that one binding decides it");
    }

    internal sealed class StandInFormat : IProjectFileFormat
    {
        public string CodePrefix(FormDefinition form) => $"prefix of {form.Name}: {Stamp}\r\n";
        public string CodePrefix(ModuleDefinition module) => $"prefix of {module.Name}: {Stamp}\r\n";
        public bool CarriesName { get; set; } = true;
        public bool PrefixCarriesName(ModuleDefinition module) => CarriesName;

        public string Stamp { get; set; } = "one";
        public byte[]? Companion { get; set; }
        public string FormName { get; set; } = "Form1";
        public string? Citation { get; set; }
        public IReadOnlyList<string> Skipped { get; set; } = [];
        public SerializedProjectGroup Group { get; set; } = new(null, [], []);
        public List<(string Text, byte[]? Companion)> FormsRead { get; } = [];
        public List<(string Text, byte[]? Companion)> DesignerPartsRead { get; } = [];

        public RenderedFile RenderForm(FormDefinition form, string fileName) =>
            new($"form {form.Name} as {fileName}: {Stamp}", Companion);

        public RenderedFile RenderModule(ModuleDefinition module, string fileName) =>
            new($"module {module.Name} as {fileName}: {Stamp}", null);

        public string RenderDesignerHeader(FormDefinition form, string fileName) => $"header as {fileName}: {Stamp}";

        public string RenderProject(ProjectDefinition project, string projectPath, Func<object, string?>? pathOf = null,
            bool includeRelatedDocuments = true) => $"project {project.Name}: {Stamp}";

        public string RenderGroup(string groupPath, IReadOnlyList<ProjectDefinition> projects, ProjectDefinition? startupProject,
            IReadOnlyList<string>? unknownLines, Func<object, string?>? pathOf) => $"group of {projects.Count}: {Stamp}";

        public IReadOnlyList<ComponentInstance> RootChildren(IReadOnlyList<ComponentInstance> components, ComponentInstance root) => [];

        public Task<ProjectFileRead> ReadProjectAsync(string path, Vb6TextEncoding hostAnsi, Action<string, byte[]>? fileRead,
            IDeserializeErrorSink problems)
        {
            var directory = Path.GetDirectoryName(path)!;
            var project = new ProjectDefinition(VBProjectType.EXE, "Test") { AbsolutePath = path, ItemLinesDirectory = directory };
            var form = new FormDefinition(project, FormComponentClass.Instance, "Form1") { AbsolutePath = Path.Join(directory, "Form1.frm") };
            project.AddForm(form);
            var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule) { AbsolutePath = Path.Join(directory, "Module1.bas") };
            project.AddModule(module);
            foreach (var member in new[] { form.AbsolutePath!, module.AbsolutePath! }.Where(File.Exists))
                fileRead?.Invoke(member, File.ReadAllBytes(member));
            return Task.FromResult(new ProjectFileRead(project, Skipped, [], File.ReadAllBytes(path)));
        }

        public SerializedProjectGroup ReadGroup(string text) => Group;

        public FormDefinition? ReadForm(ProjectDefinition project, string text, byte[]? companion, IDeserializeErrorSink problems)
        {
            FormsRead.Add((text, companion));
            return Read(project, text);
        }

        public FormDefinition? ReadDesignerPart(ProjectDefinition project, string text, byte[]? companion, IDeserializeErrorSink problems)
        {
            DesignerPartsRead.Add((text, companion));
            return Read(project, text);
        }

        private FormDefinition Read(ProjectDefinition project, string text)
        {
            var form = new FormDefinition(project, FormComponentClass.Instance, FormName);
            form.UpdateCode("code of " + text);
            return form;
        }

        public (string Header, string Body) SplitModule(string text, ModuleKind kind) => ("header of " + text, "body of " + text);

        public string CompanionExtension(string path) => ".standin";

        public string MemberLine(ModuleKind kind, string name, string relativePath) => $"member {name}";

        public bool IsCompanionCitation(string value) => value == Citation;
    }
}

/// <summary>That the application's own composition gives each part the one format it binds (#3564).</summary>
public class ProjectFileFormatCompositionTests : CompositionTestBase
{
    private static object? Field(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);

    [AvaloniaFact]
    public void Everything_that_renders_gets_the_same_format()
    {
        var composition = Composition;
        _ = composition.Root;
        var format = Field(composition.ProjectService, "format");

        format.Should().BeOfType<Vb6ProjectFileFormat>();
        Field(Field(composition.ProjectService, "headerRefresher")!, "format").Should().BeSameAs(format);
        Field(composition.LspWorkspace, "_format").Should().BeSameAs(format);
        Field(Field(composition.FileWatcherService, "dirtyDetector")!, "_format").Should().BeSameAs(format);
        Field(((Func<FormEditViewModel>)Field(composition.EditorService, "formEditViewModelFactory")!)(), "format").Should().BeSameAs(format);
    }
}
