using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using HexIDE.Automation;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using HexIDE.Tools;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// The Immediate window's editor is a node that <c>match</c> finds, and inspecting it reads its text (#2513).
/// </summary>
/// <remarks>
/// <c>dump_visual_tree</c>'s description promised that <c>match: "TextEditor"</c> finds the Immediate window's editor, and
/// it found nothing. A TextEditor with no AutomationId, no provider of its own and no focus (its TextArea takes the focus)
/// is folded away as a structural wrapper, leaving an unnamed TextArea that answers to nothing. The code window's editor
/// escapes that only because its context menu gives it a provider. A caller reading a program's Debug.Print output, the
/// commonest check after a run, could not tell an empty window from a hidden or an unreachable one.
/// </remarks>
public class ImmediateWindowTreeTests
{
    private static (Window Window, UiNode Tree) Shown(string text)
    {
        var vm = new ImmediateToolViewModel(Substitute.For<ILocalizationService>(), Substitute.For<IDebugController>());
        vm.Document.Text = text;
        var window = new Window { Content = new ImmediateToolView { DataContext = vm }, Width = 500, Height = 250 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, UiAutomationDriver.Dump(window, "Window", 60, interactiveOnly: false));
    }

    [AvaloniaFact]
    public void Match_finds_the_editor_by_its_class_and_by_the_windows_name()
    {
        var (window, tree) = Shown("Hello\nWorld");
        try
        {
            var editor = UiAutomationDriver.Find(tree, "TextEditor", 50).Matches.Should().ContainSingle().Subject;
            editor.Path.Should().EndWith("[Immediate.Editor]", "its AutomationId names it, in every language");
            UiAutomationDriver.Find(tree, "Immediate", 50).Matches.Select(n => n.Path).Should().Contain(editor.Path);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Inspecting_the_editor_reads_the_windows_text()
    {
        var (window, tree) = Shown("Hello\nWorld");
        try
        {
            var path = UiAutomationDriver.Find(tree, "TextEditor", 50).Matches.Single().Path;
            var (control, error) = UiAutomationDriver.Resolve(window, path);
            control.Should().NotBeNull(error);

            var detail = UiAutomationDriver.Inspect(control!, path, window);
            detail.Value.Should().Be("Hello\nWorld", "the editor is where a person reads the text, and it has no value provider to give it");
            detail.IsReadOnly.Should().BeFalse("a value says whether it can be written, as a value provider's does; the Immediate window takes typing");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_code_windows_editor_still_leaves_its_file_to_get_file_content()
    {
        // A code window's document is a project file, and putting it into every inspection of the editor is what #562 undid.
        var editor = new TextEditor { DataContext = Substitute.For<ISearchableDocument>() };
        editor.Document.Text = "Option Explicit";
        var window = new Window { Content = editor, Width = 400, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            UiAutomationDriver.Inspect(editor, "Window/None", window).Value.Should().BeNull();
        }
        finally { window.Close(); }
    }
}
