using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
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
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The line numbers a code window shows count from the top of the file, header included
/// (hexide-io/HexIDE#273 task 3.16, the code-editor delta's "Lines SHALL be numbered from the top of the file").
/// </summary>
/// <remarks>
/// The debugger's half of the same rule (current statement, Call Stack) is pinned by
/// <c>DebuggerLinesAreFileLinesTests</c>, and the automation and add-in halves count in
/// <see cref="CodeWindowText"/>.
/// </remarks>
public class LineNumbersFromTheTopOfTheFileTests : IDisposable
{
    private readonly List<Window> windows = [];
    private readonly List<CodeEditorViewModel> made = [];
    private readonly StatusBarService statusBar = new(Substitute.For<ILocalizationService>());

    public void Dispose()
    {
        foreach (var w in windows) w.Close();
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string ClassCode =
        "Option Explicit\r\n" +
        "\r\n" +
        "Public Function Total() As Currency\r\n" +
        "    Total = 0\r\n" +
        "End Function\r\n";

    private CodeEditorViewModel OpenClass()
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<DocumentSavedEvent>(Arg.Any<Action<DocumentSavedEvent>>()).Returns(Substitute.For<IDisposable>());

        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        var lsp = Substitute.For<ILspClient>();
        lsp.IsRunning.Returns(false);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());

        var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), Substitute.For<IProjectService>(),
            eventBus, lsp, settings, statusBar, new BookmarkService(), new BreakpointService(),
            Substitute.For<IDebugController>(), new RunScope(), Substitute.For<ILocalizationService>());
        made.Add(vm);

        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Order", ModuleKind.ClassModule);
        module.UpdateCode(ClassCode);
        return vm.Initialize(module);
    }

    private TextEditor Show(CodeEditorViewModel vm)
    {
        var window = new Window { Width = 1200, Height = 800 };
        windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var view = new CodeEditorView { DataContext = vm };
        window.Content = view;
        Dispatcher.UIThread.RunJobs();

        var editor = view.FindControl<TextEditor>("TextEditor")!;
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        return editor;
    }

    [AvaloniaFact]
    public void AClassWithTheStandardHeader()
    {
        // The code-editor delta's scenario: the status bar counts a class's first line of code from the top of the
        // file. The expected number is where the class's own header ends, not a constant, so the test states the
        // rule rather than today's header.
        var vm = OpenClass();
        var editor = Show(vm);
        var header = vm.CodeWindowText.HeaderLineCount;
        header.Should().BeGreaterThan(0, "a class carries a header");

        editor.CaretOffset = vm.Document.Text.IndexOf("Option Explicit", StringComparison.Ordinal);
        Dispatcher.UIThread.RunJobs();

        statusBar.Line.Should().Be(header + 1);
    }

    [AvaloniaFact]
    public void TheLineNumberMarginNumbersTheWholeFile()
    {
        // AvaloniaEdit's margin numbers the document's lines, so what this pins is that the document IS the
        // file: its first line is the header's first, and the count includes the header.
        var vm = OpenClass();
        var editor = Show(vm);

        editor.ShowLineNumbers.Should().BeTrue();
        editor.TextArea.LeftMargins.Should().ContainSingle(m => m is LineNumberMargin);
        editor.Document.GetText(editor.Document.GetLineByNumber(1)).Should().StartWith("VERSION 1.0 CLASS");
        editor.Document.LineCount.Should().Be(vm.CodeWindowText.LineCount);
    }
}
