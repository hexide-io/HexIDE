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
/// VB6's (#3623). Held read-only where the type only reads, and written through the type's own format where it can render
/// (#3666).
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
    private readonly StandInFormat _format = new();
    private readonly HeaderRefresher _headers;
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
            "Str.ContributedProject.FormReadOnly" => "An add-in read this, so it wasn't saved.",
            "Str.ContributedProject.RenderRefused" => "{0} wasn't written by {1}.",
            "Str.ContributedProject.WithdrawWithChanges" => "{0} has unsaved forms: {1}",
            "Str.ContributedProject.WithdrawnWithChanges" => "{0} has gone, and these weren't saved: {1}",
            var key => key + " {0} {1}",
        });
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(new List<HexIDE.Forms.ViewModels.BaseEditorWindowViewModel>());
        _headers = new HeaderRefresher(new HexIDE.Runtime.Serialization.Vb6ReadOnlyRegionWriter(), new EventBus(), dock, new BreakpointService(), new BookmarkService(), new Vb6ProjectFileFormat());
        _service = new ProjectService(new HexIDE.Runtime.Serialization.Vb6ReadOnlyRegionWriter(), () => throw new InvalidOperationException("no new-project dialog"), _windows,
            _events, projects, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), sidecar, _baselines, strings, _headers,
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
        await ToldOf("An add-in read this, so it wasn't saved.");
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
        await ToldOf("An add-in read this, so it wasn't saved.");
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
    public async Task VB6_s_own_format_can_be_a_type_s_format_and_writes_a_real_form_as_a_vbp_s_save_would()
    {
        var frm = Path.Join(_dir, "Real.frm");
        File.WriteAllText(frm, "VERSION 5.00\r\nBegin VB.Form Real\r\n   Caption         =   \"Real\"\r\nEnd\r\nAttribute VB_Name = \"Real\"\r\nPrivate Sub Form_Load()\r\nEnd Sub\r\n");
        using var type = Contribute(new Vb6ProjectFileFormat(), new AddinProjectMember(frm, AddinMemberKind.Form));
        var form = await OpenedForm();

        form.Name.Should().Be("Real");
        form.CanSaveFaithfully.Should().BeTrue("VB6's format renders what it reads, by whichever route it was asked");
        _service.HasUnsavedChanges(form).Should().BeFalse();
        form.UpdateCode(form.Code + "Private Sub Form_Click()\r\nEnd Sub\r\n");
        (await _service.SaveForm(form, saveAs: false)).Should().BeTrue();

        File.ReadAllText(frm).Should().Be(new Vb6ProjectFileFormat().RenderForm(form, "Real.frm").Text);
        File.ReadAllText(frm).Should().Contain("Form_Click");
    }

    [AvaloniaTheory]
    [InlineData(AddinMemberKind.UserControl, ModuleKind.UserControl)]
    [InlineData(AddinMemberKind.PropertyPage, ModuleKind.PropertyPage)]
    public async Task A_designer_member_is_read_by_its_type_s_reader_and_joins_as_a_module_with_its_designer_half(AddinMemberKind kind, ModuleKind moduleKind)
    {
        using var type = Contribute(_forms, new AddinProjectMember(FormFile, kind));

        var module = (await Opened()).Modules.Should().ContainSingle().Subject;

        module.Name.Should().Be("Main");
        module.Kind.Should().Be(moduleKind);
        module.Code.Should().Be("first");
        module.AbsolutePath.Should().Be(FormFile);
        module.FormPart.Should().NotBeNull();
        module.FormPart!.AbsolutePath.Should().Be(FormFile);
        _forms.DesignerReads.Should().ContainSingle();
        _forms.DesignerReads[0].Companion.Should().Equal(1, 2, 3);
        _baselines.Matches(CompanionFile, [1, 2, 3]).Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task A_designer_member_an_add_in_read_is_read_only_is_never_rendered_and_a_save_leaves_its_file_alone()
    {
        using var type = Contribute(_forms, new AddinProjectMember(FormFile, AddinMemberKind.UserControl));
        var module = (await Opened()).Modules.Single();
        var before = File.ReadAllBytes(FormFile);

        module.UpdateCode("changed");
        var saved = await _service.SaveModule(module, saveAs: false);

        module.FormPart!.CanSaveFaithfully.Should().BeFalse();
        _service.HasUnsavedChanges(module).Should().BeFalse("nothing can render it, and it opens read-only");
        saved.Should().BeFalse();
        File.ReadAllBytes(FormFile).Should().Equal(before);
        await ToldOf("An add-in read this, so it wasn't saved.");
    }

    [AvaloniaFact]
    public async Task UserControls_are_read_before_the_forms_that_can_hold_them()
    {
        var control = Path.Join(_dir, "Gauge.sform");
        File.WriteAllText(control, "Gauge\ncontrol");
        using var type = Contribute(_forms, new AddinProjectMember(FormFile, AddinMemberKind.Form), new AddinProjectMember(control, AddinMemberKind.UserControl));
        var heldAtFormRead = new List<string>();
        var read = _forms.Read;
        _forms.Instead = (project, _) =>
        {
            heldAtFormRead.AddRange(project.Modules.Select(module => module.Name));
            return read(project);
        };

        await Opened();

        heldAtFormRead.Should().Equal("Gauge");
    }

    [AvaloniaFact]
    public async Task A_designer_member_reloads_and_recovers_through_its_type_s_reader()
    {
        using var type = Contribute(_forms, new AddinProjectMember(FormFile, AddinMemberKind.PropertyPage));
        var module = (await Opened()).Modules.Single();
        WriteForm("second");
        File.WriteAllBytes(CompanionFile, [4]);

        (await _service.ReloadModuleFromDisk(module)).Should().BeTrue();

        module.Code.Should().Be("second");
        _forms.DesignerReads[1].Companion.Should().Equal(4);
        module.FormPart!.CanSaveFaithfully.Should().BeFalse();

        _service.ApplyRecovered(module, new UnsavedFile("Main.sform", "Main\nrecovered", "Main.sbin", [7])).Should().BeTrue();

        module.Code.Should().Be("recovered");
        _forms.DesignerReads[2].Companion.Should().Equal(7);
        module.FormPart.CanSaveFaithfully.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task A_designer_member_with_no_reader_is_left_out_and_named()
    {
        using var type = Contribute(null, new AddinProjectMember(FormFile, AddinMemberKind.UserControl));

        (await Opened()).Modules.Should().BeEmpty();

        await ToldOf(FormFile);
    }

    [AvaloniaFact]
    public async Task A_designer_member_moved_takes_its_companion_s_baseline_with_it()
    {
        using var type = Contribute(_forms, new AddinProjectMember(FormFile, AddinMemberKind.UserControl));
        var module = (await Opened()).Modules.Single();
        var (movedForm, movedCompanion) = (Path.Join(_dir, "Moved.sform"), Path.Join(_dir, "Moved.sbin"));
        File.Move(FormFile, movedForm);
        File.Move(CompanionFile, movedCompanion);

        _changes!.MemberMoved(FormFile, movedForm);
        Dispatcher.UIThread.RunJobs();

        module.AbsolutePath.Should().Be(movedForm);
        module.FormPart!.AbsolutePath.Should().Be(movedForm);
        _baselines.Matches(movedCompanion, [1, 2, 3]).Should().BeTrue();
        _baselines.TryGet(CompanionFile).Should().BeNull();
    }

    [AvaloniaTheory]
    [InlineData(UnfaithfulSaveCause.ReadByAddin, true)]
    [InlineData(UnfaithfulSaveCause.RefusedByFormat, true)]
    [InlineData(UnfaithfulSaveCause.NestedContainers, false)]
    public void The_designer_s_banner_has_its_own_words_for_a_form_an_add_in_read(UnfaithfulSaveCause cause, bool ownWords)
    {
        var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "Ledger"), [new ComponentInstance(Root, "Main")], "");
        form.MarkUnfaithfulToSave(cause, "the reason");

        using var designer = new HexIDE.VisualDesigner.FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), Substitute.For<IWindowManager>(), Substitute.For<ISettingsService>(),
            Substitute.For<ILocalizationService>(), new Vb6ProjectFileFormat()).Initialize(form);

        designer.IsReadOnly.Should().BeTrue();
        designer.IsReadByAddin.Should().Be(ownWords);
    }

    [AvaloniaFact]
    public async Task A_form_its_type_can_render_is_written_through_that_format_companion_first()
    {
        using var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form));
        var form = await OpenedForm();
        form.CanSaveFaithfully.Should().BeTrue("its type can write what it read");
        form.UnfaithfulSaveCauses.Should().NotHaveFlag(UnfaithfulSaveCause.ReadByAddin);
        _service.HasUnsavedChanges(form).Should().BeFalse("nothing has changed since it was read");

        form.UpdateCode("changed");
        _format.Companion = [4, 5];
        _service.HasUnsavedChanges(form).Should().BeTrue();
        var opened = new List<string>();
        AtomicFileWriter.OpenStream.Value = temporary =>
        {
            opened.Add(Path.GetFileName(temporary));
            return new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        };
        try
        {
            (await _service.SaveForm(form, saveAs: false)).Should().BeTrue();
        }
        finally
        {
            AtomicFileWriter.OpenStream.Value = null;
        }

        File.ReadAllText(FormFile).Should().Be("Main\nchanged");
        File.ReadAllBytes(CompanionFile).Should().Equal(4, 5);
        opened.Should().HaveCount(2);
        opened[0].Should().StartWith("Main.sbin.", "an interrupted save then leaves a pair that refuses to load, not one that loads wrongly");
        opened[1].Should().StartWith("Main.sform.");
        _service.HasUnsavedChanges(form).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Saving_the_project_writes_its_edited_forms_and_modules_and_never_its_project_file()
    {
        var bas = Path.Join(_dir, "Code.bas");
        File.WriteAllText(bas, "Attribute VB_Name = \"Code\"\r\nSub Main()\r\nEnd Sub\r\n");
        using var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form), new AddinProjectMember(bas, AddinMemberKind.StandardModule));
        var project = await Opened();
        project.Forms.Single().UpdateCode("changed");
        project.Modules.Single().UpdateCode("Sub Main()\r\n    Debug.Print 1\r\nEnd Sub\r\n");

        await _service.SaveAllProjects(saveAs: false);

        File.ReadAllText(FormFile).Should().Be("Main\nchanged");
        File.ReadAllText(bas).Should().Contain("Debug.Print 1");
        File.ReadAllText(ProjectFile).Should().Be("a project");
    }

    [AvaloniaTheory]
    [InlineData(AddinMemberKind.UserControl)]
    [InlineData(AddinMemberKind.PropertyPage)]
    public async Task A_designer_member_its_type_can_render_is_written_through_that_format(AddinMemberKind kind)
    {
        using var type = Contribute(_format, new AddinProjectMember(FormFile, kind));
        var module = (await Opened()).Modules.Single();
        module.FormPart!.CanSaveFaithfully.Should().BeTrue();
        _service.HasUnsavedChanges(module).Should().BeFalse();

        module.UpdateCode("changed");
        _format.Companion = [6];
        (await _service.SaveModule(module, saveAs: false)).Should().BeTrue();

        File.ReadAllText(FormFile).Should().Be("Main\nchanged");
        File.ReadAllBytes(CompanionFile).Should().Equal(6);
        _format.DesignerRenders.Should().NotBeEmpty();
        _service.HasUnsavedChanges(module).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task A_form_its_reader_refuses_opens_read_only_and_a_save_names_it_with_the_add_in_s_words()
    {
        using var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form));
        _format.Instead = (project, _) =>
        {
            var refused = _format.Read(project);
            refused.MarkUnfaithfulToSave(UnfaithfulSaveCause.RefusedByFormat, "it holds a gauge I can't write");
            return refused;
        };
        var form = await OpenedForm();
        var before = File.ReadAllBytes(FormFile);

        form.UpdateCode("changed");
        var saved = await _service.SaveForm(form, saveAs: false);

        form.CanSaveFaithfully.Should().BeFalse();
        saved.Should().BeFalse();
        File.ReadAllBytes(FormFile).Should().Equal(before);
        await ToldOf("An add-in read this, so it wasn't saved.");
        await ToldOf("Main: it holds a gauge I can't write");
    }

    [AvaloniaFact]
    public async Task A_form_its_format_cannot_render_when_it_is_read_opens_read_only_with_the_add_in_s_words()
    {
        using var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form));
        _format.Throws = new InvalidOperationException("the gauge is broken");

        var form = await OpenedForm();

        form.CanSaveFaithfully.Should().BeFalse("a format that can't render what it just read can't save it");
        form.UnfaithfulSaveCauses.Should().HaveFlag(UnfaithfulSaveCause.RefusedByFormat);
        form.UnfaithfulSaveReason.Should().Contain("the gauge is broken");
    }

    [AvaloniaFact]
    public async Task A_render_that_throws_ends_the_save_before_anything_is_written_and_says_why()
    {
        using var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form));
        var form = await OpenedForm();
        var before = File.ReadAllBytes(FormFile);
        form.UpdateCode("changed");
        _format.Companion = [9];
        _format.Throws = new InvalidOperationException("the gauge is broken");

        var save = () => _service.SaveForm(form, saveAs: false);

        await save.Should().ThrowAsync<RenderRefusal>();
        File.ReadAllBytes(FormFile).Should().Equal(before);
        File.ReadAllBytes(CompanionFile).Should().Equal(1, 2, 3);
        await ToldOf("Main wasn't written by A provider.");
        await ToldOf("the gauge is broken");
    }

    [AvaloniaFact]
    public async Task A_render_that_does_not_end_with_the_code_is_never_written()
    {
        using var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form));
        var form = await OpenedForm();
        var before = File.ReadAllBytes(FormFile);
        form.UpdateCode("changed");
        _format.Text = (name, code) => code + "\n" + name;

        var save = () => _service.SaveForm(form, saveAs: false);

        await save.Should().ThrowAsync<RenderRefusal>("the code window takes the header to be the render less the code");
        File.ReadAllBytes(FormFile).Should().Equal(before);
        await ToldOf("Main wasn't written by A provider.");
    }

    [AvaloniaFact]
    public async Task A_companion_outside_the_form_s_folder_is_never_read_or_written()
    {
        var elsewhere = Path.Join(_dir, "elsewhere", "Main.sbin");
        Directory.CreateDirectory(Path.GetDirectoryName(elsewhere)!);
        File.WriteAllBytes(elsewhere, [8]);
        _format.CompanionAt = _ => elsewhere;
        using var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form));

        var form = await OpenedForm();
        form.UpdateCode("changed");
        var saved = await _service.SaveForm(form, saveAs: false);

        _format.Reads.Single().Companion.Should().BeNull("a companion is read from beside its file or not at all");
        form.CanSaveFaithfully.Should().BeFalse("its render has a companion with nowhere beside the file to go");
        saved.Should().BeFalse();
        File.ReadAllBytes(elsewhere).Should().Equal(8);
    }

    [AvaloniaFact]
    public async Task A_companion_named_as_the_form_s_own_file_is_never_written_over_it()
    {
        _format.CompanionAt = path => path;
        using var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form));

        var form = await OpenedForm();
        form.UpdateCode("changed");
        var saved = await _service.SaveForm(form, saveAs: false);

        _format.Reads.Single().Companion.Should().BeNull();
        saved.Should().BeFalse();
        File.ReadAllText(FormFile).Should().Be("Main\nfirst");
    }

    [AvaloniaTheory]
    [InlineData("member")]
    [InlineData("project")]
    public async Task A_companion_named_as_another_of_the_project_s_files_ends_the_save_before_anything_changes(string which)
    {
        var bas = Path.Join(_dir, "Code.bas");
        File.WriteAllText(bas, "Attribute VB_Name = \"Code\"\r\nSub Main()\r\nEnd Sub\r\n");
        using var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form), new AddinProjectMember(bas, AddinMemberKind.StandardModule));
        var form = (await Opened()).Forms.Single();
        var sibling = which == "member" ? bas : ProjectFile;
        var (siblingBefore, formBefore, companionBefore) = (File.ReadAllBytes(sibling), File.ReadAllBytes(FormFile), File.ReadAllBytes(CompanionFile));
        _format.CompanionAt = _ => sibling;
        form.UpdateCode("changed");

        var save = () => _service.SaveForm(form, saveAs: false);

        await save.Should().ThrowAsync<RenderRefusal>("a companion written there would overwrite one of the project's own files");
        File.ReadAllBytes(sibling).Should().Equal(siblingBefore);
        File.ReadAllBytes(FormFile).Should().Equal(formBefore);
        File.ReadAllBytes(CompanionFile).Should().Equal(companionBefore);
        await ToldOf("Main wasn't written by A provider.");
    }

    [AvaloniaFact]
    public async Task A_form_whose_format_names_another_member_as_its_companion_is_never_read_with_it_and_opens_read_only()
    {
        var bas = Path.Join(_dir, "Code.bas");
        File.WriteAllText(bas, "Attribute VB_Name = \"Code\"\r\nSub Main()\r\nEnd Sub\r\n");
        _format.CompanionAt = _ => bas;
        using var type = Contribute(_format, new AddinProjectMember(bas, AddinMemberKind.StandardModule), new AddinProjectMember(FormFile, AddinMemberKind.Form));
        var before = File.ReadAllBytes(bas);

        var form = (await Opened()).Forms.Single();
        form.UpdateCode("changed");
        var saved = await _service.SaveForm(form, saveAs: false);

        _format.Reads.Single().Companion.Should().BeNull("a module's bytes are no form's companion");
        form.CanSaveFaithfully.Should().BeFalse();
        saved.Should().BeFalse();
        File.ReadAllBytes(bas).Should().Equal(before);
    }

    [AvaloniaFact]
    public async Task Once_the_add_in_has_gone_crash_recovery_offers_its_copy_rather_than_asking_it()
    {
        var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form));
        var form = await OpenedForm();
        type.Dispose();

        _service.RecoveredMatches(form, new UnsavedFile("Main.sform", "Main\nfirst")).Should().BeFalse(
            "nothing can say the copy matches, so it's offered");
    }

    [AvaloniaFact]
    public async Task Crash_recovery_keeps_a_copy_rendered_by_the_type_s_format()
    {
        using var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form));
        var form = await OpenedForm();
        form.UpdateCode("changed");
        _format.Companion = [4];

        var copy = _service.CaptureUnsavedWork().Single().Documents.Single().Content;

        copy.Should().NotBeNull();
        copy!.Text.Should().Be("Main\nchanged");
        copy.CompanionFileName.Should().Be("Main.sbin");
        copy.Companion.Should().Equal(4);
    }

    [AvaloniaFact]
    public async Task A_form_whose_render_fails_keeps_what_its_last_capture_said()
    {
        var edited = Path.Join(_dir, "Edited.sform");
        File.WriteAllText(edited, "Edited\nfirst");
        using var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form), new AddinProjectMember(edited, AddinMemberKind.Form));
        var project = await Opened();
        var (untouched, changed) = (project.Forms.Single(f => f.Name == "Main"), project.Forms.Single(f => f.Name == "Edited"));
        changed.UpdateCode("changed");
        _service.CaptureUnsavedWork();
        // Asked again between captures, as the title bar and the close prompt ask: the copy has to outlive that too.
        _service.HasUnsavedChanges(changed).Should().BeTrue();

        _format.Throws = new InvalidOperationException("the gauge is broken");
        changed.UpdateCode("changed again");

        _service.HasUnsavedChanges(changed).Should().BeTrue("its last capture carried an edit");
        _service.HasUnsavedChanges(untouched).Should().BeFalse("its last capture carried none");
        var documents = _service.CaptureUnsavedWork().Single().Documents;
        documents.Single(d => d.Name == "Edited").Content!.Text.Should().Be("Edited\nchanged", "the copy from the last capture that could render it");
        documents.Single(d => d.Name == "Main").Content.Should().BeNull();
    }

    [AvaloniaFact]
    public async Task Once_the_add_in_has_gone_its_format_is_never_called_again()
    {
        var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form));
        var form = await OpenedForm();
        form.UpdateCode("changed");
        _service.CaptureUnsavedWork();
        var (renders, reads) = (_format.Renders.Count, _format.Reads.Count);

        type.Dispose();
        _service.HasUnsavedChanges(form).Should().BeTrue("the last capture before it went carried the edit");
        _service.CaptureUnsavedWork().Single().Documents.Single().Content!.Text.Should().Be("Main\nchanged");
        _headers.LayoutChanged(form);
        WriteForm("second");
        await _service.ReloadFormFromDisk(form);
        (await _service.SaveForm(form, saveAs: false)).Should().BeFalse("what it wrote is read-only once it has gone");

        _format.Renders.Should().HaveCount(renders, "its consent, or its being turned on, is what let it run");
        _format.Reads.Should().HaveCount(reads);
        File.ReadAllText(FormFile).Should().Be("Main\nsecond");
    }

    [AvaloniaFact]
    public async Task The_header_over_the_code_is_rendered_by_the_form_s_own_format()
    {
        using var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form));
        var form = await OpenedForm();

        _headers.LayoutChanged(form);

        _format.Renders.Should().Contain("header Main.sform");
        form.DesignerText.Should().Be("Main\n");
    }

    [AvaloniaFact]
    public async Task Before_its_add_in_goes_an_edited_form_is_asked_about_and_Save_writes_it_while_it_can()
    {
        var gauge = Path.Join(_dir, "Gauge.sform");
        File.WriteAllText(gauge, "Gauge\ncontrol");
        using var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form), new AddinProjectMember(gauge, AddinMemberKind.UserControl));
        var project = await Opened();
        var (form, control) = (project.Forms.Single(), project.Modules.Single());
        form.UpdateCode("changed");
        control.UpdateCode("control changed");
        Answer(MessageBoxResult.Yes);

        (await _service.ConfirmAddinWithdrawal(Provider.Id)).Should().BeTrue();

        await ToldOf("A provider has unsaved forms:");
        await ToldOf("Main");
        await ToldOf("Gauge");
        File.ReadAllText(FormFile).Should().Be("Main\nchanged");
        File.ReadAllText(gauge).Should().Be("Gauge\ncontrol changed");
        _service.HasUnsavedChanges(form).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Don_t_Save_lets_the_add_in_go_and_the_edit_with_it()
    {
        var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form));
        var form = await OpenedForm();
        form.UpdateCode("changed");
        _service.CaptureUnsavedWork();
        Answer(MessageBoxResult.No);

        (await _service.ConfirmAddinWithdrawal(Provider.Id)).Should().BeTrue();
        type.Dispose();
        Dispatcher.UIThread.RunJobs();

        File.ReadAllText(FormFile).Should().Be("Main\nfirst");
        _service.HasUnsavedChanges(form).Should().BeFalse("the user let it go");
        _service.CaptureUnsavedWork().Should().BeEmpty();
        await _windows.DidNotReceive().MessageBox(Arg.Is<string>(said => said.Contains("weren't saved")), Arg.Any<string?>(),
            Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());
    }

    [AvaloniaFact]
    public async Task Cancel_keeps_the_add_in_and_the_edit()
    {
        using var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form));
        var form = await OpenedForm();
        form.UpdateCode("changed");
        Answer(MessageBoxResult.Cancel);

        (await _service.ConfirmAddinWithdrawal(Provider.Id)).Should().BeFalse();

        File.ReadAllText(FormFile).Should().Be("Main\nfirst");
        _service.HasUnsavedChanges(form).Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task A_save_that_fails_keeps_the_add_in()
    {
        using var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form));
        var form = await OpenedForm();
        form.UpdateCode("changed");
        _service.HasUnsavedChanges(form).Should().BeTrue();
        Answer(MessageBoxResult.Yes);
        _format.Throws = new InvalidOperationException("the gauge is broken");

        (await _service.ConfirmAddinWithdrawal(Provider.Id)).Should().BeFalse("its edit would otherwise be lost with it");

        File.ReadAllText(FormFile).Should().Be("Main\nfirst");
    }

    [AvaloniaFact]
    public async Task An_add_in_with_nothing_unsaved_goes_without_a_question()
    {
        using var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form));
        await OpenedForm();

        (await _service.ConfirmAddinWithdrawal(Provider.Id)).Should().BeTrue();

        await _windows.DidNotReceiveWithAnyArgs().MessageBox(default!, default, default, default, default, default);
    }

    [AvaloniaFact]
    public async Task Another_add_in_going_asks_nothing_about_this_one_s_forms()
    {
        using var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form));
        var form = await OpenedForm();
        form.UpdateCode("changed");

        (await _service.ConfirmAddinWithdrawal("someone-else")).Should().BeTrue();

        await _windows.DidNotReceiveWithAnyArgs().MessageBox(default!, default, default, default, default, default);
    }

    [AvaloniaFact]
    public async Task Once_its_add_in_has_gone_what_it_wrote_is_read_only_and_its_views_are_told()
    {
        var gauge = Path.Join(_dir, "Gauge.sform");
        File.WriteAllText(gauge, "Gauge\ncontrol");
        var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form), new AddinProjectMember(gauge, AddinMemberKind.UserControl));
        var project = await Opened();
        var (form, control) = (project.Forms.Single(), project.Modules.Single().FormPart!);

        type.Dispose();

        form.CanSaveFaithfully.Should().BeFalse();
        form.UnfaithfulSaveCauses.Should().HaveFlag(UnfaithfulSaveCause.ReadByAddin);
        control.CanSaveFaithfully.Should().BeFalse();
        _events.Received(1).Publish(Arg.Is<FormReadOnlyChangedEvent>(e => e.Designer == form));
        _events.Received(1).Publish(Arg.Is<FormReadOnlyChangedEvent>(e => e.Designer == control));
    }

    [AvaloniaFact]
    public async Task Gone_with_no_one_asked_its_forms_keep_the_last_copy_and_the_user_is_told_later_changes_weren_t_kept()
    {
        var type = Contribute(_format, new AddinProjectMember(FormFile, AddinMemberKind.Form));
        var form = await OpenedForm();
        form.UpdateCode("changed");
        _service.CaptureUnsavedWork();
        form.UpdateCode("changed again");

        type.Dispose();
        Dispatcher.UIThread.RunJobs();

        await ToldOf("A provider has gone, and these weren't saved:");
        await ToldOf("Main");
        _service.HasUnsavedChanges(form).Should().BeTrue();
        _service.CaptureUnsavedWork().Single().Documents.Single().Content!.Text.Should().Be("Main\nchanged");
    }

    [AvaloniaFact]
    public void A_form_made_read_only_outside_a_reload_tells_its_designer()
    {
        var bus = new EventBus();
        var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "Ledger"), [new ComponentInstance(Root, "Main")], "");
        using var designer = new HexIDE.VisualDesigner.FormEditViewModel(null!, bus, Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), Substitute.For<IWindowManager>(), Substitute.For<ISettingsService>(),
            Substitute.For<ILocalizationService>(), new Vb6ProjectFileFormat()).Initialize(form);
        var raised = new List<string?>();
        designer.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        form.MarkUnfaithfulToSave(UnfaithfulSaveCause.ReadByAddin, "it has gone");
        bus.Publish(new FormReadOnlyChangedEvent(form));
        Dispatcher.UIThread.RunJobs();

        designer.IsReadOnly.Should().BeTrue();
        raised.Should().Contain(nameof(designer.IsReadOnly)).And.Contain(nameof(designer.IsReadByAddin));
    }

    private void Answer(MessageBoxResult answer) =>
        _windows.MessageBox(Arg.Is<string>(said => said.Contains("has unsaved forms")), Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(),
            Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>()).Returns(answer);

    [Fact]
    public void No_product_code_renders_a_form_past_the_format_its_project_names() =>
        ProductSource.FilesMatching(new Regex(@"\b_?format\s*\.\s*(RenderForm|RenderDesignerFile|RenderDesignerHeader|CompanionExtension)\b"))
            .Should().BeEmpty("a project an add-in opened has its forms written by that add-in's format, or by nothing");

    [Fact]
    public void No_product_code_reads_a_form_past_the_reader_its_project_names() =>
        ProductSource.FilesMatching(new Regex(@"\b_?format\s*\.\s*(ReadForm|ReadDesignerPart)\b"))
            .Should().BeEmpty("a project an add-in opened has its forms read by that add-in, on every path that reads one");

    private static IComponentClass StandInRoot()
    {
        var root = Substitute.For<IComponentClass>();
        root.Name.Returns("Form");
        root.VBTypeName.Returns("VB.Form");
        return root;
    }

    // A format that is nothing like VB6's: the name on the first line and the code after it, beside a .sbin.
    private class StandInForms : IFormFileReader
    {
        private string _text = "";

        public List<(string Text, byte[]? Companion)> Reads { get; } = [];

        public List<(string Text, byte[]? Companion)> DesignerReads { get; } = [];

        public Func<ProjectDefinition, IDeserializeErrorSink, FormDefinition?>? Instead { get; set; }

        public FormDefinition? ReadForm(ProjectDefinition project, string text, byte[]? companion, IDeserializeErrorSink problems)
        {
            Reads.Add((text, companion));
            _text = text;
            return Instead is { } instead ? instead(project, problems) : Read(project);
        }

        public FormDefinition? ReadDesignerPart(ProjectDefinition project, string text, byte[]? companion, IDeserializeErrorSink problems)
        {
            DesignerReads.Add((text, companion));
            _text = text;
            return Read(project);
        }

        public FormDefinition Read(ProjectDefinition project)
        {
            var parts = _text.Split('\n', 2);
            return new FormDefinition(project, [new ComponentInstance(Root, parts[0])], parts[1]);
        }

        public Func<string, string>? CompanionAt { get; set; }

        public string? CompanionPath(string path) => CompanionAt?.Invoke(path) ?? Path.ChangeExtension(path, ".sbin");
    }

    // The same format, and it renders: the name, a line break and the code, beside the companion it is given.
    private sealed class StandInFormat : StandInForms, IFormFileFormat
    {
        public List<string> Renders { get; } = [];

        public List<string> DesignerRenders { get; } = [];

        public byte[]? Companion { get; set; } = [1, 2, 3];

        public Func<string, string, string>? Text { get; set; }

        public Exception? Throws { get; set; }

        public RenderedFile RenderForm(FormDefinition form, string fileName) => Render(form.Name, form.Code, fileName);

        public RenderedFile RenderDesignerFile(ModuleDefinition module, string fileName)
        {
            DesignerRenders.Add(fileName);
            return Render(module.Name, module.Code, fileName);
        }

        public string RenderDesignerHeader(FormDefinition form, string fileName)
        {
            Renders.Add("header " + fileName);
            if (Throws is { } thrown) throw thrown;
            return form.Name + "\n";
        }

        private RenderedFile Render(string name, string code, string fileName)
        {
            Renders.Add(fileName);
            if (Throws is { } thrown) throw thrown;
            return new((Text ?? ((n, c) => n + "\n" + c))(name, code), Companion);
        }
    }
}
