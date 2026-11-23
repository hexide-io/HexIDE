using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Location = HexIDE.Lsp.Messages.Location;
using HexIDE.Keymaps;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

// Measurements: docs/vb6-fidelity-oracle/view-last-position.md and https://github.com/hexide-io/HexIDE/pull/5426.
public class LastPositionTests
{
    private static KeyModifiers CommandKey => ApplicationCommands.ToggleBookmarkCommand.Gestures.Single().KeyModifiers;

    private static void SendKey(Window window, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var raw = RawInputModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Control)) raw |= RawInputModifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Meta)) raw |= RawInputModifiers.Meta;
        if (modifiers.HasFlag(KeyModifiers.Shift)) raw |= RawInputModifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Alt)) raw |= RawInputModifiers.Alt;
        window.KeyPress(key, raw, PhysicalKey.None, null);
        window.KeyRelease(key, raw, PhysicalKey.None, null);
    }

    [AvaloniaTheory]
    [InlineData("Default")]
    [InlineData("VB6")]
    public async Task ShortcutNavigatesAndKeepsBookmarks(string keymap)
    {
        await WithIde((c, window, vm, view, editor) =>
        {
            var keys = c.KeymapService;
            keys.Apply(keymap);
            var line = vm.Document.GetLineByNumber(4);
            editor.CaretOffset = line.EndOffset;
            editor.TextArea.Focus().Should().BeTrue();
            window.KeyTextInput(" ' a");
            SendKey(window, Key.Down);
            SendKey(window, Key.Down);
            vm.BookmarkService.Toggle(vm.Identity, 8);
            SendKey(window, Key.F2, CommandKey | KeyModifiers.Shift);
            Dispatcher.UIThread.RunJobs();
            editor.CaretOffset.Should().Be(vm.Document.GetLineByNumber(4).Offset);
            vm.BookmarkService.GetBookmarks(vm.Identity).Should().Contain(8);
            ApplicationCommands.ClearAllBookmarksCommand.Gestures.Should().BeEmpty();
        });
    }

    [AvaloniaFact]
    public async Task MenuIsDisabledUntilAnEditedLineIsLeft()
    {
        await WithIde((c, window, vm, view, editor) =>
        {
            editor.TextArea.Focus().Should().BeTrue();
            var menu = view.GetLogicalDescendants().OfType<Menu>().Single();
            ((MenuItem)menu.Items[2]!).IsSubMenuOpen = true;
            var item = MenuPath.Resolve(menu.Items, "View/Last Position").Item!;
            Avalonia.Labs.Input.CommandManager.InvalidateRequerySuggested();
            Dispatcher.UIThread.RunJobs();
            item.IsEffectivelyEnabled.Should().BeFalse();
            ((MenuItem)menu.Items[2]!).IsSubMenuOpen = false;
            editor.CaretOffset = vm.Document.GetLineByNumber(4).EndOffset;
            editor.TextArea.Focus().Should().BeTrue();
            window.KeyTextInput(" ' a");
            SendKey(window, Key.Down);
            Avalonia.Labs.Input.CommandManager.InvalidateRequerySuggested();
            Dispatcher.UIThread.RunJobs();
            ((MenuItem)menu.Items[2]!).IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            item.IsEffectivelyEnabled.Should().BeTrue();
            item.Command!.Execute(item.CommandParameter);
            editor.CaretOffset.Should().Be(vm.Document.GetLineByNumber(4).Offset);
        });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArrowKeysAndNextProcedureMakeNoEntry(bool procedure)
    {
        await WithIde((c, window, vm, view, editor) =>
        {
            editor.CaretOffset = vm.Document.GetLineByNumber(4).Offset;
            editor.TextArea.Focus().Should().BeTrue();
            SendKey(window, Key.Down, procedure ? CommandKey : KeyModifiers.None);
            SendKey(window, Key.Down, procedure ? CommandKey : KeyModifiers.None);
            var before = editor.CaretOffset;
            SendKey(window, Key.F2, CommandKey | KeyModifiers.Shift);
            editor.CaretOffset.Should().Be(before);
            var menu = view.GetLogicalDescendants().OfType<Menu>().Single();
            ((MenuItem)menu.Items[2]!).IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            MenuPath.Resolve(menu.Items, "View/Last Position").Item!.IsEffectivelyEnabled.Should().BeFalse();
        });
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DefinitionRecordsOriginAndPressesDoNotAddEntries(bool repeat, bool otherWindow)
    {
        string? targetUri = null;
        var lsp = Substitute.For<ILspClient>();
        lsp.IsRunning.Returns(true);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());
        lsp.RequestDefinitionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<Location[]?>([new Location(targetUri ?? call.ArgAt<string>(0),
                new HexIDE.Lsp.Messages.Range(new Position(7, 0), new Position(7, 3)))]));
        await WithIdeAsync(async (c, window, vm, main, editor) =>
        {
            c.KeymapService.Apply("VB6");
            if (otherWindow)
            {
                var target = new ModuleDefinition(vm.Identity.Project, "Module2", ModuleKind.StandardModule);
                target.UpdateCode(vm.Identity.Module!.Code);
                vm.Identity.Project.AddModule(target);
                targetUri = DocumentWireName.For(DocumentIdentity.For(target));
            }
            editor.CaretOffset = vm.Document.GetLineByNumber(4).Offset + 5;
            editor.TextArea.Focus().Should().BeTrue();
            SendKey(window, Key.F2, KeyModifiers.Shift);
            var timeout = DateTime.UtcNow.AddSeconds(15);
            while (ReferenceEquals(c.DocumentDockService.ActiveDocument, vm) && editor.TextArea.Caret.Line == 4
                   && DateTime.UtcNow < timeout)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            var destination = (CodeEditorViewModel)c.DocumentDockService.ActiveDocument!;
            destination.Document.GetLineByOffset(destination.CaretOffset).LineNumber.Should().Be(8);
            SendKey(window, Key.F2, CommandKey | KeyModifiers.Shift);
            if (repeat) SendKey(window, Key.F2, CommandKey | KeyModifiers.Shift);
            c.DocumentDockService.ActiveDocument.Should().BeSameAs(vm);
            vm.CaretOffset.Should().Be(vm.Document.GetLineByNumber(4).Offset);
            Dispatcher.UIThread.RunJobs();
            var activeEditor = main.GetVisualDescendants().OfType<CodeEditorView>()
                .Single(v => ReferenceEquals(v.DataContext, vm)).GetVisualDescendants()
                .OfType<HexIDE.Controls.TextEditing.TextEditor>().Single();
            activeEditor.CaretOffset.Should().Be(vm.Document.GetLineByNumber(4).Offset);
        }, lsp);
    }

    [AvaloniaTheory]
    [InlineData("enter")]
    [InlineData("delete")]
    public async Task StructuralEditsAboveEntryMakeNoneAndKeepItsNumber(string edit)
    {
        await WithIde((c, window, vm, view, editor) =>
        {
            editor.CaretOffset = vm.Document.GetLineByNumber(13).EndOffset;
            editor.TextArea.Focus().Should().BeTrue();
            window.KeyTextInput(" ' a");
            SendKey(window, Key.Home, CommandKey);
            editor.CaretOffset = vm.Document.GetLineByNumber(edit == "enter" ? 3 : 7).Offset;
            if (edit == "enter") { SendKey(window, Key.Enter); SendKey(window, Key.Enter); }
            else
            {
                SendKey(window, Key.Down, KeyModifiers.Shift);
                SendKey(window, Key.Delete);
            }
            SendKey(window, Key.Home, CommandKey);
            SendKey(window, Key.F2, CommandKey | KeyModifiers.Shift);
            editor.CaretOffset.Should().Be(vm.Document.GetLineByNumber(13).Offset);
        });
    }

    private static Task WithIde(Action<DISetup, Window, CodeEditorViewModel, MainView, HexIDE.Controls.TextEditing.TextEditor> test) =>
        WithIdeAsync((c, w, vm, view, editor) => { test(c, w, vm, view, editor); return Task.CompletedTask; });

    private static async Task WithIdeAsync(Func<DISetup, Window, CodeEditorViewModel, MainView, HexIDE.Controls.TextEditing.TextEditor, Task> test, ILspClient? lsp = null)
    {
        await using var owned = new RealComposition();
        var c = owned.Composition;
        c.LocalizationService.Apply("en");
        var view = new MainView { DataContext = c.Root };
        view.DataTemplates.Add(new ViewLocator());
        var window = new Window { Content = view, Width = 1100, Height = 800 };
        var project = new ProjectDefinition(VBProjectType.EXE, "LastPosition");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Option Explicit\nSub Main()\n    Foo\n    Bar\nEnd Sub\n\nSub Foo()\n    Debug.Print 1\nEnd Sub\n\nSub Bar()\n    Debug.Print 2\nEnd Sub\n");
        project.AddModule(module);
        c.ProjectManager.AddProject(project);
        try
        {
            window.Show();
            if (lsp is null) c.EditorService.EditCode(module);
            else
            {
                var code = new CodeEditorViewModel(Substitute.For<IWindowManager>(), c.EditorService,
                    Substitute.For<IProjectService>(), c.EventBus, lsp, c.SettingsService,
                    Substitute.For<IStatusBarService>(), Substitute.For<HexIDE.Bookmarks.IBookmarkService>(),
                    Substitute.For<HexIDE.Debugging.IBreakpointService>(),
                    Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(), Substitute.For<HexIDE.Debugging.IRunScope>(),
                    c.LocalizationService).Initialize(module);
                c.DocumentDockService.OpenDocument(code);
            }
            Dispatcher.UIThread.RunJobs();
            var vm = c.DocumentDockService.OpenDocuments.OfType<CodeEditorViewModel>().Single();
            var editor = view.GetVisualDescendants().OfType<CodeEditorView>().Single()
                .GetVisualDescendants().OfType<HexIDE.Controls.TextEditing.TextEditor>().Single();
            await test(c, window, vm, view, editor);
        }
        finally
        {
            c.KeymapService.Apply("Default");
            c.ProjectManager.UnloadAllProjects();
            window.Close();
            view.DataContext = null;
        }
    }
}
