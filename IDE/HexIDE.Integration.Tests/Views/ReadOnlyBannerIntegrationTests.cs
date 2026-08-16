using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AvaloniaEdit;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Verifies the read-only gate as it actually renders (issues #21/#22).
///
/// The gate is enforced in the view layer, not the view-model: the designer disables its whole design
/// surface rather than guarding twenty-five mutation methods, and the code editor sets TextEditor.IsReadOnly.
/// Neither is observable from a view-model test, so it is checked here against a real (headless) visual tree.
/// </summary>
public class ReadOnlyBannerIntegrationTests
{
    private static FormDefinition MakeForm(bool faithful)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var root = new ComponentInstance(FormComponentClass.Instance, "Form1");
        var form = new FormDefinition(project, [root], "");
        if (!faithful)
            form.MarkUnfaithfulToSave("it contains nested controls or menus, which HexIDE would flatten on save");
        return form;
    }

    private static CodeEditorViewModel MakeCodeEditorViewModel()
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(
            Arg.Any<System.Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<System.IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(
            Arg.Any<System.Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<System.IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(
            Arg.Any<System.Action<FormUnloadedEvent>>()).Returns(Substitute.For<System.IDisposable>());

        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Document.CodeSuffix").Returns("Code");

        return new CodeEditorViewModel(
            Substitute.For<IWindowManager>(),
            Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(),
            eventBus,
            Substitute.For<ILspClient>(),
            Substitute.For<ISettingsService>(),
            Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(),
            Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            localization);
    }

    private static T Render<T>(T view, int w = 900, int h = 700) where T : Control
    {
        view.Measure(new Size(w, h));
        view.Arrange(new Rect(0, 0, w, h));
        return view;
    }

    /// <summary>
    /// The code editor is the surface where a read-only form actually costs the developer something —
    /// someone typing a procedure into a form whose save is refused. FormEditViewModel needs seven
    /// injected services (one of them a concrete Tool with its own graph) to reach Initialize, so the
    /// designer's identical one-line IsReadOnly is covered by CodeEditorViewModelTests instead, and what
    /// is verified here is the binding actually taking effect in a rendered tree.
    /// </summary>
    [AvaloniaFact]
    public void CodeEditor_ForAnUnfaithfulForm_IsReadOnlyAndShowsTheBanner()
    {
        var vm = MakeCodeEditorViewModel();
        vm.Initialize(MakeForm(faithful: false));

        var view = Render(new CodeEditorView { DataContext = vm });

        vm.IsReadOnly.Should().BeTrue();

        // Named lookup rather than a visual walk: the editor sits inside a templated decorator that a
        // headless measure/arrange pass does not fully realise.
        view.FindControl<TextEditor>("TextEditor")!.IsReadOnly
            .Should().BeTrue("typing must be blocked, not merely discouraged");

        // Silent read-only would be worse than either alternative — the reason must be on screen, and it
        // must be resolved text rather than a raw Str.* key.
        // Presence, not text: DynamicResource does not resolve without the localization dictionary
        // merged into the headless app. That the key exists is covered by LocalizationCoverageTests.
        view.FindControl<Border>("ReadOnlyBanner")!.IsVisible
            .Should().BeTrue("silent read-only would be worse than either alternative");
    }

    [AvaloniaFact]
    public void CodeEditor_ForAnOrdinaryForm_StaysEditableWithNoBanner()
    {
        var vm = MakeCodeEditorViewModel();
        vm.Initialize(MakeForm(faithful: true));

        var view = Render(new CodeEditorView { DataContext = vm });

        vm.IsReadOnly.Should().BeFalse("the gate must be narrow");
        view.FindControl<TextEditor>("TextEditor")!.IsReadOnly.Should().BeFalse();
        view.FindControl<Border>("ReadOnlyBanner")!.IsVisible.Should().BeFalse();
    }
}
