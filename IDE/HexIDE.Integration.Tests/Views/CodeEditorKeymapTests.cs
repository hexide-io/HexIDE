using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using HexIDE.Forms.Views;
using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Keymaps;

namespace HexIDE.Integration.Tests.Views;

public class CodeEditorKeymapTests
{
    [AvaloniaTheory]
    [InlineData("FindReferencesCommand", "Shift+F12", "")]
    [InlineData("GoToDefinitionCommand", "F12", "")]
    [InlineData("GoToDeclarationCommand", "command+F12", "")]
    [InlineData("FormatDocumentCommand", "Shift+Alt+F", "")]
    [InlineData("FindReferencesCommand", "Shift+F12", "F10")]
    [InlineData("GoToDefinitionCommand", "F12", "F10")]
    [InlineData("GoToDeclarationCommand", "command+F12", "F10")]
    [InlineData("FormatDocumentCommand", "Shift+Alt+F", "F10")]
    public void CodeWindowHonoursBindings(string command, string oldGesture, string binding)
    {
        if (oldGesture == "command+F12")
            oldGesture = new KeyGesture(Key.F12, ApplicationCommands.CopyCommand.Gestures[0].KeyModifiers).ToString();
        var routed = CommandKeyMapping.Table[command];
        var original = routed.Gestures.ToArray();
        var selectAll = ApplicationCommands.SelectAllCommand;
        var originalSelectAll = selectAll.Gestures.ToArray();
        using var vm = MakeViewModel();
        vm.Initialize(new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Module1", ModuleKind.StandardModule));
        var view = new CodeEditorView { DataContext = vm };
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            editor.Text = "some code";
            editor.TextArea.Focus();
            routed.Gestures.Clear();
            if (binding.Length > 0) routed.Gestures.Add(KeyGesture.Parse(binding));

            var key = Press(editor, oldGesture);
            key.Handled.Should().BeFalse("the binding removed the old gesture from the code window");
            if (binding == "F10")
                Press(editor, binding).Handled.Should().BeTrue("the moved gesture reaches the routed editor command");

            selectAll.Gestures.Clear();
            selectAll.Gestures.Add(KeyGesture.Parse(oldGesture));
            Press(editor, oldGesture);
            editor.SelectedText.Should().Be("some code", "the old gesture now belongs to Select All");
        }
        finally
        {
            window.Close();
            routed.Gestures.Clear();
            foreach (var gesture in original) routed.Gestures.Add(gesture);
            selectAll.Gestures.Clear();
            foreach (var gesture in originalSelectAll) selectAll.Gestures.Add(gesture);
        }
    }

    private static CodeEditorViewModel MakeViewModel()
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
            Substitute.For<ILspClient>(),
            settings,
            Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(),
            Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<HexIDE.Debugging.IRunScope>(),
            localization);
    }

    private static KeyEventArgs Press(TextEditor editor, string gesture)
    {
        var parsed = KeyGesture.Parse(gesture);
        var key = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent, Key = parsed.Key, KeyModifiers = parsed.KeyModifiers
        };
        editor.TextArea.RaiseEvent(key);
        return key;
    }
}
