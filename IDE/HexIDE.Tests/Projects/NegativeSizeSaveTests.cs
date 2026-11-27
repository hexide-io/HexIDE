using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

// #4179 / oracle PR #4350: recovered load errors require Save As.
public class NegativeSizeSaveTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-negative-size-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> loaded = new();
    private ProjectGroupDefinition? group;

    public NegativeSizeSaveTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private ProjectService MakeService(IHeaderRefresher? refresher = null)
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(_ => loaded);
        projectManager.CurrentGroup.Returns(_ => group);
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
            .Do(ci => loaded.Add(ci.Arg<ProjectDefinition>()));

        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);

        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel>());

        // A member with no file asks where its first save goes: here, under the name it suggests.
        var windowManager = Substitute.For<IWindowManager>();
        picker = windowManager;
        windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>(), Arg.Any<string?>())
            .Returns(ci => Task.FromResult<string?>(
                Path.Join(dir, ci.Arg<Avalonia.Platform.Storage.FilePickerSaveOptions>().SuggestedFileName)));

        var localization = Substitute.For<HexIDE.Localization.ILocalizationService>();
        localization.GetString("Str.Dialog.SaveLoadedForm.ChooseNewFile").Returns("Localized warning for {0}: choose another file.");
        localization.GetString("Str.ProjectLoad.PropertyNotSet").Returns("Line {0}: Property {1} in {2} could not be set.");

        return new ProjectService(
            () => throw new InvalidOperationException("new-project dialog must not be reached"),
            windowManager,
            Substitute.For<IEventBus>(),
            projectManager,
            Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(),
            sidecar,
            new FileBaselineStore(),
            localization,
            refresher ?? new HeaderRefresher(new EventBus(), dock, new BreakpointService(), new BookmarkService()),
            InBoxRegistry.Create());
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private IWindowManager picker = null!;

    private (ProjectDefinition Project, FormDefinition Form, string Original) Load(string width = "-150", string property = "Width", bool button = true)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P") { AbsolutePath = Path.Join(dir, "P.vbp") };
        var properties = $"{property} = {width}\r\n";
        var source = "VERSION 5.00\r\nBegin VB.Form Form1\r\n" +
            (button ? "Begin VB.CommandButton Command2\r\n" + properties + "End\r\n" : properties) +
            "End\r\nAttribute VB_Name = \"Form1\"\r\n";
        var form = new FormDeserializer().Deserialize(project, source, NullSink.Instance)!;
        form.AbsolutePath = Path.Join(dir, "Form1.frm");
        File.WriteAllText(form.AbsolutePath, source);
        project.AddForm(form);
        loaded.Add(project);
        return (project, form, source);
    }

    [Fact]
    public async Task Opening_a_saved_project_shows_the_diagnostic_and_keeps_the_original()
    {
        var (project, form, original) = Load();
        var path = form.AbsolutePath!;
        var bytes = File.ReadAllBytes(path);
        var time = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(project.AbsolutePath!, "Type=Exe\r\nForm=Form1.frm\r\nStartup=\"Form1\"\r\nName=\"P\"\r\n");
        loaded.Clear();
        var service = MakeService();
        await service.OpenProject(project.AbsolutePath!);
        var opened = loaded.Single().Forms.Single();
        opened.LoadedWithErrors.Should().BeTrue();
        opened.CanSaveFaithfully.Should().BeTrue();
        opened.Components[1].GetPropertyOrDefault(HexIDE.Runtime.Components.VBProperties.WidthProperty).Should().Be(0);
        await picker.Received().MessageBox(Arg.Is<string>(message => message.Contains(
            "Line 3: Property Width in Command2 could not be set.")), Arg.Any<string>(),
            Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>());
        File.ReadAllBytes(path).Should().Equal(bytes);
        File.ReadAllText(path).Should().Be(original);
        File.GetLastWriteTimeUtc(path).Should().Be(time);
    }

    // #4096's measured Height = -1 on a form's button takes this recovery too, not #4096's read-only hold: both
    // measurements have VB6 read 0 and open Save As (#4096 issuecomment-6915523429, oracle PR #4350).
    // Too-large values: #4422, measured in #4096 issuecomment-6915523429.
    [Theory]
    [InlineData("-1")]
    [InlineData("2147483584")]
    [InlineData("3.7E+18")]
    public async Task A_refused_Height_saves_through_Save_As_and_preserves_the_original(string value)
    {
        var (_, form, original) = Load(value, "Height");
        form.LoadedWithErrors.Should().BeTrue();
        form.CanSaveFaithfully.Should().BeTrue();
        var service = MakeService();
        var path = form.AbsolutePath!;
        var copy = Path.Join(dir, "Recovered.frm");
        picker.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>()).Returns(Task.FromResult<string?>(copy));
        (await service.SaveForm(form, false)).Should().BeTrue();
        await picker.Received(1).SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>());
        File.ReadAllText(path).Should().Be(original);
        File.ReadAllText(copy).Should().Contain("Height          =   0");
    }

    // #4317 / #4096 issuecomment-6915523429: a refused duplicate recovers and protects its source.
    [Fact]
    public async Task A_refused_duplicate_saves_through_Save_As_and_preserves_the_original()
    {
        var (_, form, _) = Load("2000\r\nClientHeight = abc", "ClientHeight", button: false);
        var path = form.AbsolutePath!;
        var bytes = File.ReadAllBytes(path);
        var time = File.GetLastWriteTimeUtc(path);
        form.LoadedWithErrors.Should().BeTrue();
        form.CanSaveFaithfully.Should().BeTrue();
        var service = MakeService();
        var copy = Path.Join(dir, "Recovered.frm");
        picker.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>()).Returns(Task.FromResult<string?>(copy));
        (await service.SaveForm(form, false)).Should().BeTrue();
        await picker.Received(1).SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>());
        File.ReadAllBytes(path).Should().Equal(bytes);
        File.GetLastWriteTimeUtc(path).Should().Be(time);
        var lines = File.ReadAllText(copy).ReplaceLineEndings("\n").Split('\n')
            .Where(line => line.TrimStart().StartsWith("ClientHeight ", StringComparison.OrdinalIgnoreCase)).ToArray();
        lines.Should().ContainSingle();
        double.Parse(lines[0].Split('=')[1].Trim(), System.Globalization.CultureInfo.InvariantCulture).Should().Be(2000);
        form.LoadedWithErrors.Should().BeFalse();
    }

    // #4317: these unreadable shapes retain the existing hold, including Save As.
    [Theory]
    [InlineData("ClientHeight", "abc\r\nClientHeight = 2000", false)]
    [InlineData("ClientHeight", "abc\r\nClientHeight = xyz", false)]
    [InlineData("BackColor", "&H8000000F&\r\nBackColor = &H80000019&", true)]
    [InlineData("Height", "375\r\nHeight = --5", true)]
    [InlineData("BackColor", "&H8000000F&\r\nBackColor = True", true)]
    [InlineData("BackColor", "&H8000000F&\r\nBackColor = False", true)]
    [InlineData("BackColor", "&H8000000F&\r\nBackColor = Infinity", true)]
    public async Task Held_duplicates_refuse_Save_and_Save_As(string property, string value, bool button)
    {
        var (_, form, _) = Load(value, property, button);
        form.UnfaithfulSaveCauses.Should().HaveFlag(UnfaithfulSaveCause.UnreadableDuplicateProperty);
        var path = form.AbsolutePath!;
        var bytes = File.ReadAllBytes(path);
        var time = File.GetLastWriteTimeUtc(path);
        var service = MakeService();
        (await service.SaveForm(form, false)).Should().BeFalse();
        (await service.SaveForm(form, true)).Should().BeFalse();
        await picker.DidNotReceive().SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>());
        File.ReadAllBytes(path).Should().Equal(bytes);
        File.GetLastWriteTimeUtc(path).Should().Be(time);
        Directory.GetFiles(dir).Should().Equal(path);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Save_and_Save_Project_choose_a_new_file_and_preserve_the_original(int saveKind)
    {
        var (project, form, original) = Load();
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule)
        {
            AbsolutePath = Path.Join(dir, "Module1.bas")
        };
        module.UpdateCode("Public Sub Main()\r\nEnd Sub\r\n");
        project.AddModule(module);
        if (saveKind == 2)
            group = new ProjectGroupDefinition("Group1") { AbsolutePath = Path.Join(dir, "Group1.vbg") };
        var service = MakeService();
        var path = form.AbsolutePath!;
        var time = File.GetLastWriteTimeUtc(path);
        var copy = Path.Join(dir, "Recovered.frm");
        picker.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>()).Returns(Task.FromResult<string?>(copy));
        if (saveKind == 2) await service.SaveAllProjects(false);
        else if (saveKind == 1) await service.SaveProject(project, false);
        else (await service.SaveForm(form, false)).Should().BeTrue();
        await picker.Received(1).SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>());
        File.ReadAllText(path).Should().Be(original);
        File.GetLastWriteTimeUtc(path).Should().Be(time);
        File.ReadAllText(copy).Should().Contain("Width           =   0");
        form.AbsolutePath.Should().Be(copy);
        form.LoadedWithErrors.Should().BeFalse();
        if (saveKind > 0)
        {
            File.ReadAllText(project.AbsolutePath!).Should().Contain("Recovered.frm");
            File.ReadAllText(module.AbsolutePath!).Should().Contain("Public Sub Main()");
        }
        if (group is not null) File.ReadAllText(group.AbsolutePath!).Should().Contain("P.vbp");
        picker.ClearReceivedCalls();
        await service.SaveForm(form, false);
        await picker.DidNotReceive().SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Cancellation_writes_nothing()
    {
        var (_, form, original) = Load();
        var time = File.GetLastWriteTimeUtc(form.AbsolutePath!);
        var service = MakeService();
        picker.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>()).Returns(Task.FromResult<string?>(null));
        var save = () => service.SaveForm(form, false);
        await save.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllText(form.AbsolutePath!).Should().Be(original);
        File.GetLastWriteTimeUtc(form.AbsolutePath!).Should().Be(time);
        form.LoadedWithErrors.Should().BeTrue();
    }

    [Fact]
    public async Task Restoring_a_normalized_backup_keeps_the_original_protected()
    {
        var (_, form, original) = Load();
        var service = MakeService();
        var text = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        service.ApplyRecovered(form, new HexIDE.Recovery.UnsavedFile("Form1.frm", text)).Should().BeTrue();
        var save = () => service.SaveForm(form, false, interactive: false);
        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("Refusing*Form1*");
        File.ReadAllText(form.AbsolutePath!).Should().Be(original);
    }

    [Fact]
    public void Packaging_a_copy_does_not_clear_the_originals_save_restriction()
    {
        var (_, form, original) = Load();
        var path = form.AbsolutePath!;
        var service = MakeService();
        var copy = Path.Join(dir, "Packaged.frm");
        try
        {
            service.SerializeFormToFile(form, copy, adoptHeader: false).Should().BeTrue();
            File.ReadAllText(copy).Should().Contain("Width           =   0");
            form.LoadedWithErrors.Should().BeTrue();
        }
        finally
        {
            // Make restores the temporary path after packaging; it has not saved the developer's form.
            form.AbsolutePath = path;
        }
        var write = () => service.SerializeFormToFile(form, path, adoptHeader: false);
        write.Should().Throw<InvalidOperationException>().WithMessage("Refusing*Form1*");
        File.ReadAllText(path).Should().Be(original);
    }

    [Fact]
    public void Internal_writer_refuses_the_original_before_touching_either_file()
    {
        var (_, form, original) = Load();
        var service = MakeService();
        var time = File.GetLastWriteTimeUtc(form.AbsolutePath!);
        var write = () => service.SerializeFormToFile(form, form.AbsolutePath!, adoptHeader: false);
        write.Should().Throw<InvalidOperationException>().WithMessage("Refusing*Form1*");
        File.ReadAllText(form.AbsolutePath!).Should().Be(original);
        File.GetLastWriteTimeUtc(form.AbsolutePath!).Should().Be(time);
    }

    [Fact]
    public async Task Clean_form_saves_in_place_without_a_picker()
    {
        var (_, form, _) = Load("150");
        var service = MakeService();
        (await service.SaveForm(form, false)).Should().BeTrue();
        File.ReadAllText(form.AbsolutePath!).Should().Contain("Width           =   150");
        await picker.DidNotReceive().SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Explicit_automation_Save_As_can_choose_a_new_file()
    {
        var (_, form, original) = Load();
        var path = form.AbsolutePath!;
        var service = MakeService();
        var copy = Path.Join(dir, "Recovered.frm");
        picker.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>())
            .Returns(Task.FromResult<string?>(copy));
        (await service.SaveForm(form, true, interactive: false)).Should().BeTrue();
        File.ReadAllText(path).Should().Be(original);
        File.ReadAllText(copy).Should().Contain("Width           =   0");
        form.LoadedWithErrors.Should().BeFalse();
    }

    [Fact]
    public async Task Choosing_the_original_asks_again_until_a_new_path_is_chosen()
    {
        var (_, form, original) = Load();
        var path = form.AbsolutePath!;
        var service = MakeService();
        // Agree to replacement so this test reaches the behavior it checks (#4505).
        picker.MessageBox(Arg.Any<string>(), buttons: MessageBoxButtons.YesNo, icon: MessageBoxIcon.Warning,
            layout: MessageBoxLayout.SecondButtonDefault).Returns(MessageBoxResult.Yes);
        var copy = Path.Join(dir, "Recovered.frm");
        picker.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>())
            .Returns(Task.FromResult<string?>(Path.Join(dir, ".", "Form1.frm")), Task.FromResult<string?>(copy));
        await service.SaveForm(form, false);
        await picker.Received(2).SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>());
        await picker.Received(1).MessageBox("Localized warning for Form1: choose another file.", icon: MessageBoxIcon.Warning);
        Received.InOrder(() =>
        {
            _ = picker.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>());
            _ = picker.MessageBox("Localized warning for Form1: choose another file.", icon: MessageBoxIcon.Warning);
            _ = picker.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>());
        });
        File.ReadAllText(path).Should().Be(original);
        File.Exists(copy).Should().BeTrue();
    }

    [Fact]
    public async Task Choosing_a_new_path_shows_no_warning()
    {
        var (_, form, original) = Load();
        var path = form.AbsolutePath!;
        var service = MakeService();
        var copy = Path.Join(dir, "Recovered.frm");
        picker.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>())
            .Returns(Task.FromResult<string?>(copy));

        (await service.SaveForm(form, false)).Should().BeTrue();

        await picker.DidNotReceiveWithAnyArgs().MessageBox("");
        await picker.Received(1).SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>());
        File.ReadAllText(path).Should().Be(original);
        File.Exists(copy).Should().BeTrue();
    }

    [Fact]
    public async Task Cancelling_the_picker_shows_no_warning_and_writes_nothing()
    {
        var (_, form, original) = Load();
        var path = form.AbsolutePath!;
        var service = MakeService();
        picker.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>())
            .Returns(Task.FromResult<string?>(null));

        var save = () => service.SaveForm(form, false);
        await save.Should().ThrowAsync<OperationCanceledException>();

        await picker.DidNotReceiveWithAnyArgs().MessageBox("");
        await picker.Received(1).SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>());
        File.ReadAllText(path).Should().Be(original);
        Directory.GetFiles(dir).Should().Equal(path);
        form.AbsolutePath.Should().Be(path);
        form.LoadedWithErrors.Should().BeTrue();
    }

    [Fact]
    public async Task Automation_cannot_overwrite_the_original()
    {
        var (_, form, original) = Load();
        var service = MakeService();
        picker.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>()).Returns(Task.FromResult(form.AbsolutePath));
        var save = () => service.SaveForm(form, false, interactive: false);
        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("Refusing*Form1*");
        File.ReadAllText(form.AbsolutePath!).Should().Be(original);
    }
}
