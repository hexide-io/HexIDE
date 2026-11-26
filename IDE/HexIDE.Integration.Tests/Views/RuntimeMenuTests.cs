using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;
using HexIDE.Runtime.Interpreter;
using static HexIDE.Integration.Tests.Views.ContainerRuntimeHarness;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Issue #85 — a form's menus never appeared when the project ran.
///
/// The bar was docked and left empty, while every menu WAS instantiated into a MenuItem and placed on the
/// form's canvas — where a menu has no Left, Top, Width or Height, so each one landed as a zero-sized child
/// at the origin. Net effect: no menu bar, and a stray invisible control per menu item.
///
/// The tree these need was already there. #83 records a menu's sub-items on the parent so the writer can
/// walk them; this is the other consumer of the same tree.
/// </summary>
public class RuntimeMenuTests : WindowTestFixture
{
    /// <summary>Stands in for the running form, which is what dispatches a VB6 event procedure.</summary>
    private sealed class RecordingRoot : Window, IModuleExecutionRoot
    {
        public List<string> Executed { get; } = new();
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) => Executed.Add(name);
    }

    // A File menu of the shape VB6's own templates use: nested items, shortcuts, and a separator spelled as
    // a caption of one hyphen.
    private const string MenuForm = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           Begin VB.CommandButton Command1
              Caption         =   "On the form"
              Left            =   300
              Top             =   300
              Width           =   1200
              Height          =   375
           End
           Begin VB.TextBox Text1
              Left            =   300
              Top             =   900
              Width           =   1200
              Height          =   375
           End
           Begin VB.Menu mnuFile
              Caption         =   "&File"
              Begin VB.Menu mnuFileNew
                 Caption         =   "&New"
                 Shortcut        =   ^N
              End
              Begin VB.Menu mnuFileBar1
                 Caption         =   "-"
              End
              Begin VB.Menu mnuFileExit
                 Caption         =   "E&xit"
              End
           End
           Begin VB.Menu mnuHelp
              Caption         =   "&Help"
              Begin VB.Menu mnuHelpAbout
                 Caption         =   "&About"
                 Shortcut        =   {F1}
              End
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static (Menu bar, Canvas canvas, RecordingRoot root) Run(string frm)
    {
        var (content, canvas, _, _) = Spawn(frm);
        var root = new RecordingRoot { Width = 400, Height = 300 };
        // The application's own dictionaries, so what these tests render is what a user sees. Without them
        // the menu draws against a theme set that exists nowhere else, which is how a separator shipped
        // twice — once invisible, once as a dot — under a green suite.
        MergeAppResources(root);
        root.Content = content;
        root.Show();
        Dispatcher.UIThread.RunJobs();
        var bar = ((DockPanel)content).Children.OfType<Menu>().Single();
        return (bar, canvas, root);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Editor_choice_survives_save_and_drives_the_runtime(bool array)
    {
        var form = new FormDeserializer().Deserialize(
            new ProjectDefinition(VBProjectType.EXE, "P"),
            MenuForm.Replace("Shortcut        =   ^N", ""), Substitute.For<IDeserializeErrorSink>())!;
        var component = form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "mnuFileNew");
        var model = new MenuEditorViewModel
            { SelectedMenu = new MenuViewModel(component) };
        var view = new MenuEditorView { DataContext = model };
        var editor = new Window { Content = view, Width = 650, Height = 500 };
        IdeDialogChrome.Apply(editor);
        editor.Show();
        editor.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        if (array)
        {
            var index = view.GetLogicalDescendants().OfType<TextBox>().ElementAt(2);
            index.Should().NotBeNull();
            index!.Focus();
            editor.KeyTextInput("0");
        }
        else
        {
            var shortcut = view.GetLogicalDescendants().OfType<ComboBox>().First();
            shortcut.Should().NotBeNull();
            shortcut!.SelectedIndex = 14; // Ctrl+N, after (None) and Ctrl+A through Ctrl+M.
        }
        model.SelectedMenu!.Apply(component);
        editor.Close();
        var saved = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        saved.Should().Contain(array ? "Index           =   0" : "Shortcut        =   ^N");
        var (bar, canvas, root) = Run(saved);
        var item = Item(Item(bar, "&File"), "&New");
        if (array)
        {
            ControlArrayGroup.TryGetArrayOf(item, out var group).Should().BeTrue();
            group.Elements.Keys.Should().Equal(0);
            group.Elements[0].Should().BeSameAs(item);
        }
        else
        {
            canvas.Children.OfType<TextBox>().First().Focus();
            Dispatcher.UIThread.RunJobs();
            root.Executed.Clear();
            root.KeyPress(Key.N, RawInputModifiers.Control, PhysicalKey.None, "n");
            Dispatcher.UIThread.RunJobs();
            root.Executed.Should().Equal("mnuFile_Click", "mnuFileNew_Click");
        }
    }

    private static MenuItem Item(ItemsControl parent, string header) =>
        parent.Items.OfType<MenuItem>().First(i => (i.Header as string) == header);

    // interact invoke clicks as Avalonia's menu does, tick included, so it meets the same guard (#947).
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvokingAMenuItemThroughAutomation_LeavesItsCheckedAsTheProgramSetIt(bool programChecked)
    {
        var (bar, _, root) = Run(MenuForm);
        var file = Item(bar, "&File");
        var newItem = (VBMenu)Item(file, "&New");
        newItem.Checked = programChecked;
        HexIDE.Automation.UiAutomationDriver.Interact(file, "expand", null);
        Dispatcher.UIThread.RunJobs();

        var outcome = HexIDE.Automation.UiAutomationDriver.Interact(newItem, "invoke", null);
        Dispatcher.UIThread.RunJobs();

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().NotContain("ticked");
        // Expanding File raised its Click, as opening it does in VB6 (measured, #1344).
        root.Executed.Should().Equal("mnuFile_Click", "mnuFileNew_Click");
        newItem.IsChecked.Should().Be(programChecked);
        file.IsSubMenuOpen.Should().BeFalse();
    }
}
