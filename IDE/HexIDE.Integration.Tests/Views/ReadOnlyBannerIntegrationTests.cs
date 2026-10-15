using System;
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
/// surface rather than guarding twenty-five mutation methods, and the code editor installs a read-only section
/// provider that carries the whole-document verdict (#273 task 3.7).
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
            form.MarkUnfaithfulToSave(UnfaithfulSaveCause.NestedContainers,
                "it contains controls nested inside a container, which HexIDE would flatten onto the form on save");
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
            Substitute.For<HexIDE.Debugging.IRunScope>(),
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
    /// is verified here is the provider actually installed on a rendered editor.
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
        var area = view.FindControl<TextEditor>("TextEditor")!.TextArea;
        area.ReadOnlySectionProvider.Should().BeSameAs(vm.ReadOnlySections,
            "binding TextEditor.IsReadOnly would replace the provider and lose the region rules");
        area.ReadOnlySectionProvider.CanInsert(vm.Document.TextLength)
            .Should().BeFalse("typing must be blocked, not merely discouraged");

        // Silent read-only would be worse than either alternative — the reason must be on screen, and it
        // must be resolved text rather than a raw Str.* key.
        // Presence, not text: DynamicResource does not resolve without the localization dictionary
        // merged into the headless app. That the key exists is covered by LocalizationCoverageTests.
        view.FindControl<Border>("ReadOnlyBanner")!.IsVisible
            .Should().BeTrue("silent read-only would be worse than either alternative");
    }

    /// <summary>
    /// The banner shows the whole-file key, whose words claim only what is enforced (#273 task 3.20). Its
    /// predecessor said the form's code "cannot be edited", which read like the header's read-only lines and
    /// was untrue of Replace All and the other programmatic writers (#701). The key was renamed rather than
    /// reworded, because pack parity checks only that a key is present; so the binding is pinned here by
    /// name, which neither parity nor <c>LocalizationCoverageTests</c> does: both pass for any key that
    /// exists, the designer's banner included. The words are held to design.md's rules too ("Settled while
    /// building phase 3"): they say the file will not be saved, speak of the file rather than the form, and do
    /// not say "read-only", which is what the header's grey lines are.
    /// </summary>
    [AvaloniaFact]
    public void CodeEditor_ForAnUnfaithfulForm_TheBannerSaysTheFileWillNotBeSaved()
    {
        const string key = "Str.CodeEditor.FileNotSaved.Banner";
        var english = new LocalizationService().GetString(key);
        english.Should().NotBe(key, "the key must be in the canonical pack");
        english.Should().NotContainEquivalentOf("cannot be edited",
            "Replace All, formatting, rename, add-ins and automation can still change the code (#701)");
        english.Should().Contain("will not be saved", "that is the one thing every writer is held to");
        english.Should().NotContainEquivalentOf("read-only",
            "the header's grey lines are read-only; this is about the whole file, and must not read as them");
        english.Should().NotContainEquivalentOf("this form",
            "a UserControl's or a PropertyPage's code window shows the same banner");

        var vm = MakeCodeEditorViewModel();
        vm.Initialize(MakeForm(faithful: false));
        var view = new CodeEditorView { DataContext = vm };
        // The pack's own text, supplied where the binding looks first: the headless app merges no language
        // dictionary, and applying one would change it for every test after this one.
        view.Resources[key] = english;
        Render(view);

        var banner = view.FindControl<Border>("ReadOnlyBanner")!;
        banner.IsVisible.Should().BeTrue();
        var text = banner.Child.Should().BeOfType<TextBlock>().Which;
        text.Text.Should().Be(english);

        // And it follows the resource, so it is a binding: English written into the AXAML would match the line
        // above as well, and leave every other language's IDE showing English.
        view.Resources[key] = "a sentinel no pack holds";
        text.Text.Should().Be("a sentinel no pack holds", "the banner must follow the language pack, not hold its own text");
    }

    [AvaloniaFact]
    public void CodeEditor_ForAnOrdinaryForm_StaysEditableWithNoBanner()
    {
        var vm = MakeCodeEditorViewModel();
        vm.Initialize(MakeForm(faithful: true));

        var view = Render(new CodeEditorView { DataContext = vm });

        vm.IsReadOnly.Should().BeFalse("the gate must be narrow");
        var area = view.FindControl<TextEditor>("TextEditor")!.TextArea;
        area.ReadOnlySectionProvider.Should().BeSameAs(vm.ReadOnlySections);
        vm.Document.Insert(vm.Document.TextLength, "\r\nPrivate Sub Form_Load()\r\nEnd Sub\r\n");
        area.ReadOnlySectionProvider.CanInsert(vm.Document.Text.IndexOf("End Sub", StringComparison.Ordinal))
            .Should().BeTrue("a faithful form's code takes typing");
        view.FindControl<Border>("ReadOnlyBanner")!.IsVisible.Should().BeFalse();
    }

    /// <summary>
    /// A reload that flips the verdict takes effect in an open code window without reopening it (#475), in
    /// both directions. The reload goes through the real <see cref="FileReloader"/>, over a project service
    /// that does what the real one does to the model. The code is unchanged, which is the case that slipped
    /// past a notification placed after <c>ReloadFrom</c>'s early return: an edit to the <c>.frx</c> alone.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async System.Threading.Tasks.Task CodeEditor_WhenAReloadFlipsTheVerdict_FollowsIt(bool startFaithful)
    {
        var form = MakeForm(startFaithful);
        var vm = MakeCodeEditorViewModel();
        vm.Initialize(form);
        vm.Document.Insert(vm.Document.TextLength, "\r\nPrivate Sub Form_Load()\r\nEnd Sub\r\n");
        var view = Render(new CodeEditorView { DataContext = vm });
        var banner = view.FindControl<Border>("ReadOnlyBanner")!;
        var area = view.FindControl<TextEditor>("TextEditor")!.TextArea;
        banner.IsVisible.Should().Be(!startFaithful);

        var projectService = Substitute.For<IProjectService>();
        projectService.ReloadFormFromDisk(form).Returns(_ =>
        {
            form.AdoptFidelityState(MakeForm(!startFaithful));
            return System.Threading.Tasks.Task.FromResult(true);
        });
        var reloader = new FileReloader(projectService, Substitute.For<IEventBus>(),
            Substitute.For<IStatusBarService>(), Substitute.For<ILocalizationService>(),
            Substitute.For<IHeaderRefresher>());
        form.UpdateCode(vm.BufferBody);

        await reloader.ReloadAsync(new WatchedFileTarget("Form1.frm", form, null, vm, null));

        vm.IsReadOnly.Should().Be(startFaithful);
        banner.IsVisible.Should().Be(startFaithful, "the banner must describe the file as it now is");
        area.ReadOnlySectionProvider.CanInsert(vm.Document.Text.IndexOf("End Sub", StringComparison.Ordinal))
            .Should().Be(!startFaithful, "and typing must follow the verdict, not the one the tab opened with");
    }
}
