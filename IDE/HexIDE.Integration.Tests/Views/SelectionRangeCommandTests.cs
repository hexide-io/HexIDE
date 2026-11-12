using HexIDE.Runtime.Serialization;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RoutedCommand = Avalonia.Labs.Input.RoutedCommand;
using Avalonia.Threading;
using AvaloniaEdit;
using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

public class SelectionRangeCommandTests : IDisposable
{
    private readonly ILspClient _lspClient = Substitute.For<ILspClient>();
    private readonly List<Window> _windows = [];

    public void Dispose()
    {
        foreach (var window in _windows) window.Close();
        GC.SuppressFinalize(this);
    }

    private (CodeEditorView View, TextEditor Editor, CodeEditorViewModel Vm) Open(bool provider = true)
    {
        _lspClient.IsRunning.Returns(true);
        if (typeof(ILspClient).GetMethod("SupportsSelectionRanges") is { } supports)
            ((bool)supports.Invoke(_lspClient, [Arg.Any<string>()])!).Returns(provider);
        _lspClient.AdvertisedCapabilities.Returns(System.Text.Json.JsonDocument.Parse(
            provider ? "{\"selectionRangeProvider\":true}" : "{}").RootElement.Clone());
        _lspClient.RequestSelectionRangesAsync(Arg.Any<string>(), Arg.Any<Position[]>(), Arg.Any<CancellationToken>())
            .Returns([new SelectionRange(new(new(0, 2), new(0, 4)),
                new SelectionRange(new(new(0, 1), new(0, 7)),
                    new SelectionRange(new(new(0, 0), new(0, 10)))))]);
        var vm = MakeViewModel();
        vm.Initialize(AModule());
        vm.Document.Text = "abcdefghij";
        var view = Show(vm);
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        editor.CaretOffset = 3;
        editor.TextArea.Focus();
        return (view, editor, vm);
    }

    private static void Run(CodeEditorView view, string name)
    {
        var field = typeof(ApplicationCommands).GetField(name + "Command", BindingFlags.Public | BindingFlags.Static);
        field.Should().NotBeNull("the command must be available in the keymap");
        ((RoutedCommand)field!.GetValue(null)!).Execute(null, view);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ExpandTwiceClimbsServerParents()
    {
        var (view, editor, vm) = Open();
        using (vm)
        {
            Run(view, "ExpandSelection");
            editor.SelectedText.Should().Be("cd");
            Run(view, "ExpandSelection");
            editor.SelectedText.Should().Be("bcdefg");
        }
    }

    [AvaloniaFact]
    public void ShrinkReturnsToPreviousSelection()
    {
        var (view, editor, vm) = Open();
        using (vm)
        {
            Run(view, "ExpandSelection");
            Run(view, "ExpandSelection");
            Run(view, "ShrinkSelection");
            editor.SelectedText.Should().Be("cd");
        }
    }

    [AvaloniaFact]
    public void EditDiscardsChain()
    {
        var (view, editor, vm) = Open();
        using (vm)
        {
            Run(view, "ExpandSelection");
            editor.Document.Insert(10, "x");
            var before = editor.SelectedText;
            Run(view, "ShrinkSelection");
            editor.SelectedText.Should().Be(before);
        }
    }

    [AvaloniaFact]
    public void CaretMoveDiscardsChain()
    {
        var (view, editor, vm) = Open();
        using (vm)
        {
            Run(view, "ExpandSelection");
            editor.Select(8, 0);
            Run(view, "ShrinkSelection");
            editor.CaretOffset.Should().Be(8);
            editor.SelectionLength.Should().Be(0);
        }
    }

    [AvaloniaFact]
    public void MenuCommandsAreDisabledWithoutProvider()
    {
        var (view, editor, vm) = Open(false);
        using (vm)
        {
            var menu = (MenuFlyout)editor.ContextFlyout!;
            menu.ShowAt(editor);
            Dispatcher.UIThread.RunJobs();
            foreach (var name in new[] { "ExpandSelection", "ShrinkSelection" })
            {
                var field = typeof(ApplicationCommands).GetField(name + "Command");
                field.Should().NotBeNull();
                var command = (RoutedCommand)field!.GetValue(null)!;
                command.CanExecute(null, view).Should().BeFalse();
                System.Linq.Enumerable.OfType<MenuItem>(menu.Items)
                    .Should().ContainSingle(item => item.Command == command).Which.IsEffectivelyEnabled.Should().BeFalse();
            }
        }
    }

    [AvaloniaFact]
    public void NonemptySelectionRequestsBothEndsAndSkipsSmallerRanges()
    {
        var (view, editor, vm) = Open();
        using (vm)
        {
            editor.Select(2, 3);
            Run(view, "ExpandSelection");
            editor.SelectedText.Should().Be("bcdefg");
            _lspClient.Received().RequestSelectionRangesAsync(Arg.Any<string>(),
                Arg.Is<Position[]>(p => p.Length == 2 && p[0] == new Position(0, 2) && p[1] == new Position(0, 5)),
                Arg.Any<CancellationToken>());
        }
    }

    [AvaloniaFact]
    public void ShrinkingFirstRangeRestoresOriginalCaret()
    {
        var (view, editor, vm) = Open();
        using (vm)
        {
            Run(view, "ExpandSelection");
            Run(view, "ShrinkSelection");
            editor.SelectionLength.Should().Be(0);
            editor.CaretOffset.Should().Be(3);
        }
    }

    [AvaloniaFact]
    public void ReplyAfterCaretMoveDoesNotChangeSelection()
    {
        var (view, editor, vm) = Open();
        using (vm)
        {
            var pending = new TaskCompletionSource<SelectionRange[]>();
            _lspClient.RequestSelectionRangesAsync(Arg.Any<string>(), Arg.Any<Position[]>(), Arg.Any<CancellationToken>())
                .Returns(pending.Task);
            Run(view, "ExpandSelection");
            editor.Select(8, 0);
            pending.SetResult([new SelectionRange(new(new(0, 0), new(0, 10)))]);
            Dispatcher.UIThread.RunJobs();
            editor.CaretOffset.Should().Be(8);
            editor.SelectionLength.Should().Be(0);
        }
    }

    [AvaloniaFact]
    public void UsesEnclosingRangesFromBothEndpoints()
    {
        var (view, editor, vm) = Open();
        using (vm)
        {
            var outer = new SelectionRange(new(new(0, 0), new(0, 10)));
            _lspClient.RequestSelectionRangesAsync(Arg.Any<string>(), Arg.Any<Position[]>(), Arg.Any<CancellationToken>())
                .Returns([outer, new SelectionRange(new(new(0, 1), new(0, 7)), outer)]);
            editor.Select(2, 3);
            Run(view, "ExpandSelection");
            editor.SelectedText.Should().Be("bcdefg");
            Run(view, "ExpandSelection");
            editor.SelectedText.Should().Be("abcdefghij");
        }
    }

    private CodeEditorViewModel MakeViewModel()
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(
            Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(
            Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(
            Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<DocumentSavedEvent>(
            Arg.Any<Action<DocumentSavedEvent>>()).Returns(Substitute.For<IDisposable>());

        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Document.CodeSuffix").Returns("Code");

        // A real value: the view pushes it into AvaloniaEdit, which rejects a non-positive indentation
        // size, so a substitute's default of 0 throws out of the attach handler.
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);

        return new CodeEditorViewModel(
            Substitute.For<IWindowManager>(),
            Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(),
            eventBus,
            _lspClient,
            settings,
            Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(),
            Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<HexIDE.Debugging.IRunScope>(),
            localization, new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader());
    }

    private static ModuleDefinition AModule()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("abcdefghij");
        return module;
    }

    private CodeEditorView Show(CodeEditorViewModel vm)
    {
        var window = new Window { Width = 1200, Height = 800 };
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var view = new CodeEditorView { DataContext = vm };
        window.Content = view;
        Dispatcher.UIThread.RunJobs();
        return view;
    }
}
