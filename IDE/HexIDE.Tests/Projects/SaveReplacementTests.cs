using System.IO;
using Avalonia.Platform.Storage;
using HexIDE.IDE;
using HexIDE.Desktop.Server;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;
using HexIDE.Forms.ViewModels;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.Projects;

// VB6 SP6 measurements and the deliberate orphan-companion divergence: hexide-io/HexIDE#4505, oracle PR #4501.
public sealed class SaveReplacementTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("hexide-replace-").FullName;
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();
    private readonly IProjectManager manager = Substitute.For<IProjectManager>();
    private readonly ProjectService service;
    private readonly ProjectDefinition project = new(VBProjectType.EXE, "Project1");
    private const string Question = "{0} already exists. Do you want to replace it?";

    public SaveReplacementTests()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.FilePicker.ReplaceExisting").Returns(Question);
        service = new ProjectService(() => throw new InvalidOperationException(), windows,
            Substitute.For<IEventBus>(), manager, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
            localization, Substitute.For<IHeaderRefresher>(), InBoxRegistry.Create());
        manager.LoadedProjects.Returns(Array.Empty<ProjectDefinition>());
    }

    private string Target(string name) => Path.Combine(root, name);
    public void Dispose() => Directory.Delete(root, true);

    private void Answer(MessageBoxResult answer) => windows.MessageBox(Arg.Any<string>(),
        buttons: MessageBoxButtons.YesNo, icon: MessageBoxIcon.Warning,
        layout: MessageBoxLayout.SecondButtonDefault).Returns(answer);

    private async Task Asked(string name) => await windows.Received(1).MessageBox(string.Format(Question, name),
        buttons: MessageBoxButtons.YesNo, icon: MessageBoxIcon.Warning,
        layout: MessageBoxLayout.SecondButtonDefault);

    private async Task<Func<Task>> Operation(string kind)
    {
        switch (kind)
        {
            case "firstForm":
            case "form":
                var form = await service.AddNewForm(project, "Form1", interactive: false);
                return async () => { await service.SaveForm(form, saveAs: kind == "form"); };
            case "firstModule":
            case "module":
                var module = await service.AddNewModule(project, "Module1", ModuleKind.StandardModule, interactive: false);
                return async () => { await service.SaveModule(module, saveAs: kind == "module"); };
            case "project": return () => service.SaveProject(project, saveAs: true);
            case "group":
                manager.CurrentGroup.Returns(new ProjectGroupDefinition("Group1"));
                return () => service.SaveAllProjects(saveAs: true);
            case "make":
                service.RuntimeDirectory = root;
                Directory.CreateDirectory(Target("standalone"));
                File.WriteAllText(Path.Combine(Target("standalone"), ProjectService.StandaloneRunnerName()!), "runner");
                return () => service.MakeProject(project);
            default: throw new ArgumentException(kind);
        }
    }

    [Theory]
    [InlineData("form", "Yes")]
    [InlineData("module", "Yes")]
    [InlineData("project", "Yes")]
    [InlineData("group", "Yes")]
    [InlineData("make", "Yes")]
    [InlineData("form", "Retry")]
    [InlineData("module", "Retry")]
    [InlineData("project", "Retry")]
    [InlineData("group", "Retry")]
    [InlineData("make", "Retry")]
    [InlineData("form", "Cancel")]
    [InlineData("module", "Cancel")]
    [InlineData("project", "Cancel")]
    [InlineData("group", "Cancel")]
    [InlineData("make", "Cancel")]
    [InlineData("firstForm", "Yes")]
    [InlineData("firstForm", "Retry")]
    [InlineData("firstForm", "Cancel")]
    [InlineData("firstModule", "Yes")]
    [InlineData("firstModule", "Retry")]
    [InlineData("firstModule", "Cancel")]
    public async Task ExistingDestinationRequiresConsent(string kind, string action)
    {
        var save = await Operation(kind);
        var existing = Target("existing");
        var other = Target("other");
        File.WriteAllText(existing, "original");
        var picks = new List<(string? Name, bool? Prompt, string? Directory)>();
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>()).Returns(call =>
        {
            var options = call.Arg<FilePickerSaveOptions>();
            picks.Add((options.SuggestedFileName, options.ShowOverwritePrompt, call.ArgAt<string?>(1)));
            return picks.Count == 1 ? existing : action == "Retry" ? other : null;
        });
        Answer(action == "Yes" ? MessageBoxResult.Yes : MessageBoxResult.No);

        if (action == "Cancel") await save.Should().ThrowAsync<OperationCanceledException>();
        else await save();

        await Asked("existing");
        picks.Should().HaveCount(action == "Yes" ? 1 : 2);
        picks.Should().OnlyContain(p => p.Prompt == false);
        if (action != "Yes")
        {
            picks[1].Name.Should().Be("existing");
            picks[1].Directory.Should().Be(root);
            File.ReadAllText(existing).Should().Be("original");
            File.Exists(other).Should().Be(action == "Retry");
        }
        else File.ReadAllText(existing).Should().NotBe("original");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task CompanionsAreConfirmedOrPreserved(bool designerExists, bool hasPicture)
    {
        var form = await service.AddNewForm(project, "Form1", interactive: false);
        if (hasPicture) form.Components[0].SetProperty(VBProperties.IconProperty, new byte[] { 1, 2, 3, 4 });
        else form.RecordCitedCompanionBlobCount(1);
        form.AbsolutePath = Target("original.frm");
        var destination = Target("chosen.frm");
        var companion = Target("chosen.frx");
        if (designerExists) File.WriteAllText(destination, "other form");
        File.WriteAllBytes(companion, [9, 8, 7]);
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>()).Returns(destination);
        Answer(MessageBoxResult.Yes);

        (await service.SaveForm(form, saveAs: true)).Should().BeTrue();
        File.ReadAllText(destination).Should().Contain("Begin VB.Form Form1");

        if (designerExists || hasPicture) await Asked(designerExists ? "chosen.frm" : "chosen.frx");
        else await windows.DidNotReceiveWithAnyArgs().MessageBox(default!);
        if (hasPicture) File.ReadAllBytes(companion).Should().NotEqual([9, 8, 7]);
        else File.ReadAllBytes(companion).Should().Equal([9, 8, 7]);
        await windows.ReceivedWithAnyArgs(designerExists || hasPicture ? 1 : 0).MessageBox(default!);
        if (!hasPicture)
        {
            windows.ClearReceivedCalls();
            await service.SaveForm(form, saveAs: false);
            File.ReadAllBytes(companion).Should().Equal([9, 8, 7], "a later ordinary Save must still leave it alone");
            await windows.DidNotReceiveWithAnyArgs().MessageBox(default!);
        }
    }

    [Fact]
    public async Task OrdinaryFormSaveAsksNothingButSaveAsToItsOwnFileAsks()
    {
        var form = await service.AddNewForm(project, "Form1", interactive: false);
        form.AbsolutePath = Target("Form1.frm");
        File.WriteAllText(form.AbsolutePath, "original");
        await service.SaveForm(form, saveAs: false);
        await windows.DidNotReceiveWithAnyArgs().MessageBox(default!);
        await windows.DidNotReceiveWithAnyArgs().SaveFilePickerAsync(default!);
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>()).Returns(form.AbsolutePath);
        Answer(MessageBoxResult.Yes);
        await service.SaveForm(form, saveAs: true);
        await Asked("Form1.frm");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SaveProjectAsReplacesConfirmedFileWithoutConflict(bool dirty, bool ownPath)
    {
        await service.SaveProjectToDirectory(project, root);
        if (dirty) project.Name = "Changed";
        var destination = ownPath ? project.AbsolutePath! : Target("other.vbp");
        File.WriteAllText(destination, "unrelated");
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>()).Returns(destination);
        Answer(MessageBoxResult.Yes);
        await service.SaveProject(project, saveAs: true);
        await Asked(Path.GetFileName(destination));
        File.ReadAllText(destination).Should().Contain("Name=\"" + project.Name + "\"");
        await windows.DidNotReceive().ShowDialog(Arg.Any<FileConflictViewModel>());
    }
    [Fact]
    public async Task ScriptedPickerAnswerStillRequiresConsent()
    {
        ScriptedFileDialogs.Clear();
        try
        {
            var form = await service.AddNewForm(project, "Form1", interactive: false);
            var destination = Target("scripted.frm");
            File.WriteAllText(destination, "original");
            var realWindows = new WindowManager(Substitute.For<ILocalizationService>());
            windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>())
                .Returns(call => realWindows.SaveFilePickerAsync(call.Arg<FilePickerSaveOptions>(), call.ArgAt<string?>(1)));
            ScriptedFileDialogs.AnswerNextWith(destination);
            ScriptedFileDialogs.AnswerNextWith(null);
            Answer(MessageBoxResult.No);
            var save = () => service.SaveForm(form, saveAs: true);
            await save.Should().ThrowAsync<OperationCanceledException>();
            await Asked("scripted.frm");
            File.ReadAllText(destination).Should().Be("original");
            ScriptedFileDialogs.Pending.Should().Be(0);
        }
        finally { ScriptedFileDialogs.Clear(); }
    }

    [Fact]
    public async Task SaveProjectAsKeepsTwoProjectGroupIdentityOrderAndStartup()
    {
        var second = new ProjectDefinition(VBProjectType.EXE, "Second");
        await service.SaveProjectToDirectory(project, root);
        await service.SaveProjectToDirectory(second, root);
        manager.LoadedProjects.Returns(new[] { second, project });
        manager.StartupProject.Returns(project);
        var group = new ProjectGroupDefinition("Together") { AbsolutePath = Target("Together.vbg") };
        manager.CurrentGroup.Returns(group);
        await service.SaveAllProjects(saveAs: false);
        var groupBytes = File.ReadAllBytes(group.AbsolutePath);
        var destination = Target("other.vbp");
        File.WriteAllText(destination, "unrelated");
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>()).Returns(destination);
        Answer(MessageBoxResult.Yes);
        await service.SaveProject(project, saveAs: true);
        await Asked("other.vbp");
        manager.CurrentGroup.Should().BeSameAs(group);
        group.Name.Should().Be("Together");
        manager.LoadedProjects.Should().Equal(second, project);
        manager.StartupProject.Should().BeSameAs(project);
        service.HasUnsavedChanges(project).Should().BeFalse();
        service.HasUnsavedChanges(second).Should().BeFalse();
        File.ReadAllBytes(group.AbsolutePath).Should().Equal(groupBytes);
    }

    [Fact]
    public async Task MakeReportsTheExecutableQuestionWhileItWaitsForConsent()
    {
        var save = await Operation("make");
        var destination = Target("existing.exe");
        File.WriteAllText(destination, "original");
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>())
            .Returns(destination, (string?)null);
        var answer = new TaskCompletionSource<MessageBoxResult>();
        windows.MessageBox(Arg.Any<string>(), buttons: MessageBoxButtons.YesNo, icon: MessageBoxIcon.Warning,
            layout: MessageBoxLayout.SecondButtonDefault).Returns(answer.Task);
        var saving = save();
        try
        {
            var make = MakeOutcomes.Latest!;
            make.Current.Should().Be(MakeOutcomes.Stage.Asking);
            (await MakeReply.AwaitAsync(make, TestContext.Current.CancellationToken)).Should().Be(MakeReply.Asking(destination));
            saving.IsCompleted.Should().BeFalse();
        }
        finally { answer.TrySetResult(MessageBoxResult.No); }
        Func<Task> finish = () => saving;
        await finish.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllText(destination).Should().Be("original");
    }

    [Theory]
    [InlineData(ModuleKind.UserControl)]
    [InlineData(ModuleKind.PropertyPage)]
    public async Task DesignerModuleSaveAsDoesNotClaimAnUnwrittenCompanion(ModuleKind kind)
    {
        var module = kind == ModuleKind.UserControl
            ? await service.AddNewUserControl(project, "Designer1", interactive: false)
            : await service.AddNewPropertyPage(project, "Designer1", interactive: false);
        var extension = kind == ModuleKind.UserControl ? "ctl" : "pag";
        var companionExtension = kind == ModuleKind.UserControl ? "ctx" : "pgx";
        var designer = module.FormPart!;
        designer.RecordCitedCompanionBlobCount(1);
        module.AbsolutePath = designer.AbsolutePath = Target("original." + extension);
        var destination = Target("chosen." + extension);
        var companion = Target("chosen." + companionExtension);
        File.WriteAllText(destination, "other designer");
        File.WriteAllBytes(companion, [9, 8, 7]);
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>()).Returns(destination);
        Answer(MessageBoxResult.Yes);
        await service.SaveModule(module, saveAs: true);
        await Asked(Path.GetFileName(destination));
        File.ReadAllBytes(companion).Should().Equal([9, 8, 7]);
        windows.ClearReceivedCalls();
        await service.SaveModule(module, saveAs: false);
        File.ReadAllBytes(companion).Should().Equal([9, 8, 7], "a later ordinary Save must still leave it alone");
        await windows.DidNotReceiveWithAnyArgs().MessageBox(default!);
    }

}
