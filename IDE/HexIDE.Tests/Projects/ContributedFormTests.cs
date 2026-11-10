using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Localization;
using HexIDE.Recovery;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;
using HexIDE.Tests.Infrastructure;

namespace HexIDE.Tests.Projects;

/// <summary>
/// A form an add-in's project type lists: read through the type's own reader, from a format and with controls that aren't
/// VB6's, and held read-only because nothing can write what an add-in read yet (#3623).
/// </summary>
public sealed class ContributedFormTests : IDisposable
{
    private static readonly AddinOrigin Provider = new("provider", "A provider");
    private static readonly IComponentClass Root = StandInRoot();

    private readonly string _dir = Path.Join(Path.GetTempPath(), "hexide-contributed-forms-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> _loaded = [];
    private readonly IWindowManager _windows = Substitute.For<IWindowManager>();
    private readonly IEventBus _events = Substitute.For<IEventBus>();
    private readonly FileBaselineStore _baselines = new();
    private readonly ContributedProjects _types = new() { ReadFile = path => Task.FromResult(File.ReadAllBytes(path)) };
    private readonly StandInForms _forms = new();
    private readonly ProjectService _service;
    private IAddinProjectChanges? _changes;

    public ContributedFormTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ProjectFile, "a project");
        WriteForm();
        File.WriteAllBytes(CompanionFile, [1, 2, 3]);

        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns(_ => _loaded);
        projects.When(m => m.AddProject(Arg.Any<ProjectDefinition>())).Do(call => _loaded.Add(call.Arg<ProjectDefinition>()));
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        var strings = Substitute.For<ILocalizationService>();
        strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() switch
        {
            "Str.Dialog.UnfaithfulSave.Body.One" => "Not saved: {0}",
            var key => key + " {0} {1}",
        });
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(new List<HexIDE.Forms.ViewModels.BaseEditorWindowViewModel>());
        _service = new ProjectService(() => throw new InvalidOperationException("no new-project dialog"), _windows,
            _events, projects, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), sidecar, _baselines, strings,
            new HeaderRefresher(new EventBus(), dock, new BreakpointService(), new BookmarkService(), new Vb6ProjectFileFormat()),
            new Vb6ProjectFileFormat(), new ComponentRegistry(),
            contributed: _types);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private string ProjectFile => Path.Join(_dir, "Ledger.sproj");
    private string FormFile => Path.Join(_dir, "Main.sform");
    private string CompanionFile => Path.Join(_dir, "Main.sbin");

    private void WriteForm(string code = "first") => File.WriteAllText(FormFile, "Main\n" + code);

    private IDisposable Contribute(IFormFileReader? forms, params AddinProjectMember[] members) =>
        _types.Register(new AddinProjectType("Stand-in projects")
        {
            FileNames = ["*.sproj"],
            Forms = forms,
            OpenAsync = (_, changes, _) =>
            {
                _changes = changes;
                return Task.FromResult(new AddinProjectContent("Ledger") { Members = members });
            },
        }, Provider);

    private IDisposable Contribute() => Contribute(_forms, new AddinProjectMember(FormFile, AddinMemberKind.Form));

    private async Task<ProjectDefinition> Opened()
    {
        await _service.OpenProject(ProjectFile);
        return _loaded.Should().ContainSingle().Subject;
    }

    private async Task<FormDefinition> OpenedForm() => (await Opened()).Forms.Should().ContainSingle().Subject;

    private Task ToldOf(string text) => _windows.Received().MessageBox(Arg.Is<string>(said => said.Contains(text)),
        Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());

    [AvaloniaFact]
    public async Task A_form_is_read_by_its_type_s_reader_from_its_text_and_companion_and_joins_the_project()
    {
        using var type = Contribute();

        var form = await OpenedForm();

        form.Name.Should().Be("Main");
        form.Code.Should().Be("first");
        form.AbsolutePath.Should().Be(FormFile);
        _forms.Reads.Should().ContainSingle();
        _forms.Reads[0].Text.Should().Be("Main\nfirst");
        _forms.Reads[0].Companion.Should().Equal(1, 2, 3);
        _baselines.Matches(FormFile, File.ReadAllBytes(FormFile)).Should().BeTrue("a change to the file later must be noticed");
        _baselines.Matches(CompanionFile, [1, 2, 3]).Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task A_form_with_no_companion_is_read_with_none()
    {
        File.Delete(CompanionFile);
        using var type = Contribute();

        await OpenedForm();

        _forms.Reads[0].Companion.Should().BeNull();
    }

    [AvaloniaFact]
    public async Task A_form_an_add_in_read_is_read_only_and_a_save_leaves_its_file_alone()
    {
        using var type = Contribute();
        var form = await OpenedForm();
        var before = File.ReadAllBytes(FormFile);

        var saved = await _service.SaveForm(form, saveAs: false);

        form.CanSaveFaithfully.Should().BeFalse("nothing can write what an add-in read yet");
        form.UnfaithfulSaveCauses.Should().HaveFlag(UnfaithfulSaveCause.ReadByAddin);
        saved.Should().BeFalse();
        File.ReadAllBytes(FormFile).Should().Equal(before);
        await ToldOf("Not saved:");
    }

    // As for the test below, VB6's writer throws on the stand-in's root, so this passing says the save never rendered the form.
    [AvaloniaFact]
    public async Task Saving_the_project_writes_its_edited_modules_and_refuses_its_forms()
    {
        var bas = Path.Join(_dir, "Code.bas");
        File.WriteAllText(bas, "Attribute VB_Name = \"Code\"\r\nSub Main()\r\nEnd Sub\r\n");
        using var type = Contribute(_forms, new AddinProjectMember(FormFile, AddinMemberKind.Form), new AddinProjectMember(bas, AddinMemberKind.StandardModule));
        var project = await Opened();
        var before = File.ReadAllBytes(FormFile);
        project.Modules.Single().UpdateCode("Sub Main()\r\n    Debug.Print 1\r\nEnd Sub\r\n");

        await _service.SaveAllProjects(saveAs: false);

        File.ReadAllText(bas).Should().Contain("Debug.Print 1");
        File.ReadAllBytes(FormFile).Should().Equal(before);
        File.ReadAllText(ProjectFile).Should().Be("a project");
        await ToldOf("Not saved:");
    }

    // The stand-in's root isn't VB6's Form class, so VB6's writer throws on it, and this passing says nothing rendered it.
    [AvaloniaFact]
    public async Task A_form_an_add_in_read_is_never_rendered_so_it_never_counts_as_edited()
    {
        using var type = Contribute();
        var form = await OpenedForm();

        form.UpdateCode("changed");

        _service.HasUnsavedChanges(form).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task A_form_listed_by_a_type_with_no_reader_is_left_out_and_named()
    {
        using var type = Contribute(null, new AddinProjectMember(FormFile, AddinMemberKind.Form));

        var project = await Opened();

        project.Forms.Should().BeEmpty();
        project.Modules.Should().BeEmpty("a form isn't a module for want of a reader");
        await ToldOf(FormFile);
    }

    [AvaloniaFact]
    public async Task A_form_its_reader_cannot_read_is_left_out_with_what_the_reader_said()
    {
        _forms.Instead = (_, problems) =>
        {
            problems.LogError("line 3 is not a control");
            return null;
        };
        using var type = Contribute();

        var project = await Opened();

        project.Forms.Should().BeEmpty();
        await ToldOf(FormFile);
        await ToldOf("line 3 is not a control");
    }

    [AvaloniaFact]
    public async Task A_form_read_with_problems_joins_the_project_and_they_are_shown()
    {
        var read = _forms.Read;
        _forms.Instead = (project, problems) =>
        {
            problems.LogError("a property was skipped");
            return read(project);
        };
        using var type = Contribute();

        await OpenedForm();

        await ToldOf("a property was skipped");
    }

    [AvaloniaFact]
    public async Task A_reader_that_throws_has_not_read_the_form_and_the_project_still_opens()
    {
        _forms.Instead = (_, _) => throw new InvalidDataException("the add-in's bug");
        using var type = Contribute();

        var project = await Opened();

        project.Forms.Should().BeEmpty();
        await ToldOf("the add-in's bug");
    }

    [AvaloniaFact]
    public async Task A_form_with_no_root_has_not_been_read()
    {
        _forms.Instead = (project, _) => new FormDefinition(project, [], "");
        using var type = Contribute();

        var project = await Opened();

        project.Forms.Should().BeEmpty();
        await ToldOf(FormFile);
    }

    [AvaloniaFact]
    public async Task A_form_whose_name_another_member_has_is_left_out_and_named()
    {
        var other = Path.Join(_dir, "Other.sform");
        File.WriteAllText(other, "Main\nsecond");
        using var type = Contribute(_forms, new AddinProjectMember(FormFile, AddinMemberKind.Form), new AddinProjectMember(other, AddinMemberKind.Form));

        (await Opened()).Forms.Should().ContainSingle().Which.AbsolutePath.Should().Be(FormFile);

        await ToldOf("Str.ContributedProject.NameTaken Other.sform Main");
    }

    [AvaloniaFact]
    public async Task A_reload_reads_the_form_through_its_type_s_reader_and_it_stays_read_only()
    {
        using var type = Contribute();
        var form = await OpenedForm();
        WriteForm("second");
        File.WriteAllBytes(CompanionFile, [4]);

        (await _service.ReloadFormFromDisk(form)).Should().BeTrue();

        form.Code.Should().Be("second");
        _forms.Reads.Should().HaveCount(2);
        _forms.Reads[1].Companion.Should().Equal(4);
        form.CanSaveFaithfully.Should().BeFalse("a reload takes its verdict from the read");
    }

    [AvaloniaFact]
    public async Task Crash_recovery_reads_the_form_through_its_type_s_reader()
    {
        using var type = Contribute();
        var form = await OpenedForm();

        _service.ApplyRecovered(form, new UnsavedFile("Main.sform", "Main\nrecovered", "Main.sbin", [7])).Should().BeTrue();

        form.Code.Should().Be("recovered");
        _forms.Reads[1].Companion.Should().Equal(7);
        form.CanSaveFaithfully.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Once_the_add_in_has_gone_a_reload_asks_nothing_and_leaves_the_form_as_it_was()
    {
        var type = Contribute();
        var form = await OpenedForm();
        type.Dispose();
        WriteForm("second");

        (await _service.ReloadFormFromDisk(form)).Should().BeFalse();

        form.Code.Should().Be("first");
        _forms.Reads.Should().ContainSingle();
    }

    [AvaloniaFact]
    public async Task What_the_add_in_reports_about_a_form_changes_the_project_as_it_would_for_a_module()
    {
        using var type = Contribute();
        var project = await Opened();
        var form = project.Forms.Single();
        var other = Path.Join(_dir, "Other.sform");
        File.WriteAllText(other, "Other\nsecond");

        _changes!.MemberAdded(new(other, AddinMemberKind.Form));
        _changes.MemberAdded(new(other, AddinMemberKind.Form));
        Dispatcher.UIThread.RunJobs();

        project.Forms.Select(member => member.Name).Should().Equal("Main", "Other");
        var added = project.Forms[1];

        var (movedForm, movedCompanion) = (Path.Join(_dir, "Moved.sform"), Path.Join(_dir, "Moved.sbin"));
        var (text, companion) = (File.ReadAllBytes(FormFile), File.ReadAllBytes(CompanionFile));
        File.Move(FormFile, movedForm);
        File.Move(CompanionFile, movedCompanion);
        _changes.MemberMoved(FormFile, movedForm);
        _changes.MemberRemoved(other);
        Dispatcher.UIThread.RunJobs();

        form.AbsolutePath.Should().Be(movedForm);
        _baselines.Matches(movedForm, text).Should().BeTrue("the file holds what was read, now at its new path");
        _baselines.Matches(movedCompanion, companion).Should().BeTrue();
        _baselines.TryGet(FormFile).Should().BeNull();
        _baselines.TryGet(CompanionFile).Should().BeNull();
        _events.Received(1).Publish(Arg.Is<FormMovedEvent>(e => e.Form == form));
        project.Forms.Should().ContainSingle().Which.Should().BeSameAs(form);
        _events.Received(1).Publish(Arg.Is<FormUnloadedEvent>(e => e.Form == added));
    }

    [AvaloniaFact]
    public async Task VB6_s_own_format_can_be_a_type_s_reader_with_a_real_form_and_its_companion()
    {
        var frm = Path.Join(_dir, "Real.frm");
        File.WriteAllText(frm, "VERSION 5.00\r\nBegin VB.Form Real\r\n   Caption         =   \"Real\"\r\nEnd\r\nAttribute VB_Name = \"Real\"\r\nPrivate Sub Form_Load()\r\nEnd Sub\r\n");
        using var type = Contribute(new Vb6ProjectFileFormat(), new AddinProjectMember(frm, AddinMemberKind.Form));

        var form = await OpenedForm();

        form.Name.Should().Be("Real");
        form.Code.Should().Contain("Form_Load");
        form.CanSaveFaithfully.Should().BeFalse();
    }

    [Fact]
    public void No_product_code_reads_a_form_past_the_reader_its_project_names() =>
        ProductSource.FilesMatching(new Regex(@"\b_?format\s*\.\s*ReadForm\b"))
            .Should().BeEmpty("a project an add-in opened has its forms read by that add-in, on every path that reads one");

    private static IComponentClass StandInRoot()
    {
        var root = Substitute.For<IComponentClass>();
        root.Name.Returns("Form");
        root.VBTypeName.Returns("VB.Form");
        return root;
    }

    // A format that is nothing like VB6's: the name on the first line and the code after it, beside a .sbin.
    private sealed class StandInForms : IFormFileReader
    {
        private string _text = "";

        public List<(string Text, byte[]? Companion)> Reads { get; } = [];

        public Func<ProjectDefinition, IDeserializeErrorSink, FormDefinition?>? Instead { get; set; }

        public FormDefinition? ReadForm(ProjectDefinition project, string text, byte[]? companion, IDeserializeErrorSink problems)
        {
            Reads.Add((text, companion));
            _text = text;
            return Instead is { } instead ? instead(project, problems) : Read(project);
        }

        public FormDefinition Read(ProjectDefinition project)
        {
            var parts = _text.Split('\n', 2);
            return new FormDefinition(project, [new ComponentInstance(Root, parts[0])], parts[1]);
        }

        public string? CompanionPath(string path) => Path.ChangeExtension(path, ".sbin");
    }
}
