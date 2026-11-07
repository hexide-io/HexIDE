using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Controls;

/// <summary>
/// Code reads left to right whatever the IDE's language (#1944). Under Arabic the window's right-to-left flow reached the
/// code window, which drew <c>VERSION 5.00</c> as mirror-image glyphs with its line numbers and minimap on the wrong side.
/// VB6 has no right-to-left syntax; an Arabic string inside a line is bidirectional text within a left-to-right line.
/// Rendered for real under Skia, as <see cref="DirectionalIconTests"/> are.
/// </summary>
public class CodeReadsLeftToRightTests : IDisposable
{
    private const int Width = 640, Height = 360;
    private const string Code = "VERSION 5.00\r\nOption Explicit\r\n\r\nPrivate Sub Foo()\r\n    MsgBox \"Hello\"\r\nEnd Sub\r\n";
    private readonly List<Window> windows = [];
    private readonly List<CodeEditorViewModel> made = [];

    public void Dispose()
    {
        foreach (var w in windows) w.Close();
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private Window Show(Control content, FlowDirection direction)
    {
        var window = new Window { Width = Width, Height = Height, Background = Brushes.White, FlowDirection = direction, Content = content };
        windows.Add(window);
        window.Show();
        for (var i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();
        return window;
    }

    // Rows top to bottom of the window, inked or not, so antialiasing cannot decide a comparison.
    private static byte[] Rows(Window window, int top, int bottom)
    {
        using var frame = window.CaptureRenderedFrame();
        frame.Should().NotBeNull();
        var bytes = new byte[Width * Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { frame!.CopyPixels(new PixelRect(0, 0, Width, Height), handle.AddrOfPinnedObject(), bytes.Length, Width * 4); }
        finally { handle.Free(); }
        var rows = bytes.Skip(top * Width * 4).Take((bottom - top) * Width * 4).Where((_, i) => i % 4 == 0)
            .Select(b => b < 128 ? (byte)0 : (byte)255).ToArray();
        rows.Distinct().Should().HaveCount(2, "the region must actually paint");
        return rows;
    }

    private CodeEditorView CodeWindow()
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<DocumentSavedEvent>(Arg.Any<Action<DocumentSavedEvent>>()).Returns(Substitute.For<IDisposable>());
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        settings.IsMinimapVisible.Returns(true);
        var lsp = Substitute.For<ILspClient>();
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());
        lsp.RequestFoldingRangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<FoldingRange>());

        var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), Substitute.For<IProjectService>(),
            eventBus, lsp, settings, Substitute.For<IStatusBarService>(), new BookmarkService(), new BreakpointService(),
            Substitute.For<IDebugController>(), new RunScope(), Substitute.For<ILocalizationService>());
        made.Add(vm);
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Module1", ModuleKind.StandardModule);
        module.UpdateCode(Code);
        vm.Initialize(module);
        return new CodeEditorView { DataContext = vm };
    }

    [AvaloniaFact]
    public void The_code_window_draws_its_text_gutter_and_minimap_the_same_under_a_right_to_left_language()
    {
        byte[] Code(FlowDirection direction)
        {
            var view = CodeWindow();
            var window = Show(view, direction);
            var editor = view.FindControl<TextEditor>("TextEditor")!;
            var minimap = view.FindControl<Control>("Minimap")!;
            minimap.IsVisible.Should().BeTrue("the premise: the minimap is part of what is compared");
            editor.TranslatePoint(default, minimap)!.Value.X.Should().BeLessThan(0,
                "the minimap is beside the code's right edge, where its lines end, in either language");
            // From the editor's top down: the Object and Procedure boxes above it are chrome, and follow the language.
            var top = (int)Math.Ceiling(editor.TranslatePoint(default, window)!.Value.Y);
            return Rows(window, top, Height);
        }

        Code(FlowDirection.RightToLeft).Should().Equal(Code(FlowDirection.LeftToRight));
    }

    [AvaloniaFact]
    public void Every_text_editor_reads_left_to_right()
    {
        // The Immediate window, the archive viewer, the export preview and the protocol inspector all hold a TextEditor.
        static TextEditor Editor() => new()
        {
            Text = Code, ShowLineNumbers = true, FontFamily = new FontFamily("Courier New,Monospace"), FontSize = 13,
            Foreground = Brushes.Black, Background = Brushes.White,
        };

        Rows(Show(Editor(), FlowDirection.RightToLeft), 0, Height).Should().Equal(Rows(Show(Editor(), FlowDirection.LeftToRight), 0, Height));
    }

    [AvaloniaFact]
    public void A_menu_opened_from_code_follows_the_IDE_language()
    {
        // A popup takes its direction from the control it opens from, so the code window's context menu read left to right
        // with the code, Arabic captions and all, until it read the language's direction instead.
        Application.Current!.Resources[LocalizationService.FlowDirectionKey] = FlowDirection.RightToLeft;
        var item = new MenuItem { Header = "Cut" };
        var flyout = new MenuFlyout { Items = { item } };
        var editor = new TextEditor { Text = Code, ContextFlyout = flyout };
        try
        {
            Show(editor, FlowDirection.RightToLeft);
            flyout.ShowAt(editor);
            Dispatcher.UIThread.RunJobs();

            editor.FlowDirection.Should().Be(FlowDirection.LeftToRight, "the premise: the code reads left to right");
            item.FindAncestorOfType<MenuFlyoutPresenter>().Should().NotBeNull("the premise: the menu is open")
                .And.Subject.As<MenuFlyoutPresenter>().FlowDirection.Should().Be(FlowDirection.RightToLeft);
        }
        finally
        {
            flyout.Hide();
            Application.Current.Resources.Remove(LocalizationService.FlowDirectionKey);
        }
    }

    [AvaloniaFact]
    public void Find_and_Replace_edit_code_left_to_right_inside_a_right_to_left_dialog()
    {
        var view = new FindReplaceView();
        Show(view, FlowDirection.RightToLeft);

        // The view's own boxes: a ComboBox's template holds one too, for editing, and that one is the ComboBox's.
        var boxes = view.GetVisualDescendants().OfType<TextBox>().Where(b => b.TemplatedParent is null).ToList();
        boxes.Should().HaveCount(2, "Find What and Replace With");
        boxes.Should().OnlyContain(b => b.FlowDirection == FlowDirection.LeftToRight);
        view.GetVisualDescendants().OfType<ComboBox>().Should().OnlyContain(c => c.FlowDirection == FlowDirection.RightToLeft,
            "the rest of the dialog is chrome, and follows the language");
    }
}
