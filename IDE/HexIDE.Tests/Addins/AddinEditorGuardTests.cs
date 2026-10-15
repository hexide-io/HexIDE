using HexIDE.Addins;
using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.Addins;

/// <summary>
/// An add-in's writes into a code window go through the guarded path and come back false when refused
/// (hexide-io/HexIDE#273 task 3.9). The rules themselves are tested on the view model, in
/// <c>GuardedWritersTests</c>; this is the add-in's own surface, which is what an add-in author sees.
/// </summary>
public class AddinEditorGuardTests : IDisposable
{
    private readonly IDocumentDockService dock = Substitute.For<IDocumentDockService>();
    private readonly IProjectManager projects = Substitute.For<IProjectManager>();
    private readonly CodeEditorViewModel editor;
    private readonly AddinEditorService sut;

    public AddinEditorGuardTests()
    {
        var project = TestHelpers.CreateProject();
        var module = TestHelpers.CreateModule(project, "Order", ModuleKind.ClassModule);
        module.UpdateCode("Option Explicit\r\n\r\nPrivate mTotal As Currency\r\n");
        project.AddModule(module);

        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        editor = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), eventBus, Substitute.For<ILspClient>(),
            Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(), Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<HexIDE.Debugging.IRunScope>(), Substitute.For<ILocalizationService>())
            .Initialize(module);

        dock.OpenDocuments.Returns([editor]);
        projects.LoadedProjects.Returns([project]);
        sut = new AddinEditorService(dock, Substitute.For<IEditorService>(), projects);
    }

    public void Dispose()
    {
        editor.Dispose();
        GC.SuppressFinalize(this);
    }

    [AvaloniaFact]
    public async Task ContentWhoseHeaderDiffersIsRefused()
    {
        var before = editor.Document.Text;

        var written = await sut.SetContent("Order", before.Replace("VB_Name = \"Order\"", "VB_Name = \"Other\""), null);

        written.Should().BeFalse("a caller that writes and reads back has to be able to tell its write did not land");
        editor.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public async Task CodeAloneIsAppliedAndTheHeaderKept()
    {
        var header = editor.Document.Text[..editor.Document.Text.IndexOf("Option Explicit", StringComparison.Ordinal)];

        (await sut.SetContent("Order", "Option Explicit\r\n", null)).Should().BeTrue();

        editor.Document.Text.Should().Be(header + "Option Explicit\r\n");
    }

    [AvaloniaFact]
    public async Task EditsReachingTheHeaderAreRefusedAllOrNothing()
    {
        var before = editor.Document.Text;
        var codeLine = before[..before.IndexOf("Private mTotal", StringComparison.Ordinal)].Split('\n').Length;

        var written = await sut.ApplyEdits("Order",
            [new AddinTextEdit(codeLine, 1, codeLine, 8, "Public"), new AddinTextEdit(1, 1, 1, 8, "Version")], null);

        written.Should().BeFalse();
        editor.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public async Task EditsInTheCodeAreApplied()
    {
        var before = editor.Document.Text;
        var codeLine = before[..before.IndexOf("Private mTotal", StringComparison.Ordinal)].Split('\n').Length;

        (await sut.ApplyEdits("Order", [new AddinTextEdit(codeLine, 1, codeLine, 8, "Public")], null)).Should().BeTrue();

        editor.Document.Text.Should().Be(before.Replace("Private mTotal", "Public mTotal"));
    }
}
