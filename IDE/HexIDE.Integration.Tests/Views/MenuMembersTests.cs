using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using static HexIDE.Integration.Tests.Views.ContainerRuntimeHarness;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Menus used from code at run time (#1126). Every expected line is what vb6.exe printed for the same statements on the
/// same menus, native and p-code alike: the probes and their logs are in docs/vb6-fidelity-oracle.md, "Menus at run time,
/// and the menu bar".
/// </summary>
public class MenuMembersTests
{
    // The probe's form: a Game menu holding New on F2, a separator, and the three-element array mnuLevel(0 To 2).
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Probe"
           Begin VB.Menu mnuGame
              Caption         =   "&Game"
              Begin VB.Menu mnuNew
                 Caption         =   "&New"
                 Shortcut        =   {F2}
              End
              Begin VB.Menu mnuSep1
                 Caption         =   "-"
              End
              Begin VB.Menu mnuLevel
                 Caption         =   "&Beginner"
                 Checked         =   -1  'True
                 Index           =   0
              End
              Begin VB.Menu mnuLevel
                 Caption         =   "&Intermediate"
                 Index           =   1
              End
              Begin VB.Menu mnuLevel
                 Caption         =   "&Expert"
                 Index           =   2
              End
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static async Task<List<string>> Printed(string code)
    {
        var (_, _, ctx, env) = Spawn(Form);
        return (await ContainerRuntimeHarness.Run(ctx, env, code + "\r\n")).Select(v => System.Convert.ToString(v.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "").ToList();
    }

    [AvaloniaFact]
    public async Task A_menu_reads_as_vb6_exe_read_it()
    {
        var printed = await Printed("""
            Debug.Print TypeName(mnuNew) & " " & TypeName(mnuLevel) & " " & TypeName(mnuLevel(0)) & " " & TypeName(mnuSep1)
            Debug.Print mnuLevel.LBound & " " & mnuLevel.UBound & " " & mnuLevel.Count
            Debug.Print mnuLevel(0).Caption & "|" & mnuLevel(0).Checked & "|" & mnuLevel(0).Enabled & "|" & mnuLevel(0).Visible
            Debug.Print TypeName(mnuLevel(0).Checked) & " " & TypeName(mnuLevel(0).Caption)
            Debug.Print mnuSep1.Caption & " " & mnuSep1.Enabled & " " & mnuNew.WindowList
            """);

        printed.Should().Equal("Menu Object Menu Menu", "0 2 3", "&Beginner|True|True|True", "Boolean String", "- True False");
    }

    [AvaloniaFact]
    public async Task A_menu_is_written_as_vb6_exe_wrote_it()
    {
        var printed = await Printed("""
            Dim i As Integer
            For i = 0 To 2
                mnuLevel(i).Checked = (i = 2)
            Next i
            Debug.Print mnuLevel(0).Checked & " " & mnuLevel(1).Checked & " " & mnuLevel(2).Checked
            mnuLevel(1).Checked = 5
            Debug.Print mnuLevel(1).Checked
            mnuLevel(1).Enabled = False
            mnuLevel(1).Caption = "&Medium"
            Debug.Print mnuLevel(1).Enabled & " " & mnuLevel(1).Caption
            mnuNew.Tag = "t"
            Debug.Print mnuNew.Tag
            """);

        printed.Should().Equal("False False True", "True", "False &Medium", "t");
    }

    [AvaloniaFact]
    public async Task A_menu_refuses_what_vb6_exe_refused()
    {
        var printed = await Printed("""
            On Error Resume Next
            mnuGame.Checked = True
            Debug.Print Err.Number & "|" & Err.Description: Err.Clear
            mnuSep1.Checked = True
            Debug.Print Err.Number: Err.Clear
            mnuSep1.Enabled = False
            Debug.Print Err.Number & " " & mnuSep1.Enabled: Err.Clear
            mnuNew.Caption = "-"
            Debug.Print Err.Number & "|" & Err.Description & "|" & mnuNew.Caption: Err.Clear
            mnuLevel(0).Caption = "-"
            Debug.Print Err.Number: Err.Clear
            mnuLevel(5).Checked = True
            Debug.Print Err.Number: Err.Clear
            """);

        printed.Should().Equal(
            "387|'Checked' property can't be set on this control",
            "387",
            "387 True",
            "395|Cannot use separator bar as menu name for this control|&New",
            "395",
            // vb6.exe's text names the element, "Control array element '5' doesn't exist", and HexIDE's doesn't.
            "340");
    }

    [AvaloniaFact]
    public async Task Hiding_the_last_visible_menu_under_a_menu_is_refused_even_when_it_is_a_separator()
    {
        var printed = await Printed("""
            On Error Resume Next
            mnuNew.Visible = False
            mnuSep1.Visible = False
            mnuLevel(0).Visible = False
            mnuLevel(1).Visible = False
            Debug.Print Err.Number: Err.Clear
            mnuLevel(2).Visible = False
            Debug.Print Err.Number & "|" & Err.Description: Err.Clear
            mnuSep1.Visible = True
            mnuLevel(2).Visible = False
            Debug.Print Err.Number: Err.Clear
            mnuSep1.Visible = False
            Debug.Print Err.Number: Err.Clear
            mnuGame.Visible = False
            Debug.Print Err.Number: Err.Clear
            """);

        printed.Should().Equal("0", "387|'Visible' property can't be set on this control", "0", "387", "0");
    }

    [AvaloniaFact]
    public async Task Load_and_Unload_of_a_menu_array_answer_as_vb6_exe_did()
    {
        // The first probe's order: the levels ticked so that element 0 is not, then the loads and unloads.
        var printed = await Printed("""
            On Error Resume Next
            Dim i As Integer
            For i = 0 To 2
                mnuLevel(i).Checked = (i = 2)
            Next i
            Load mnuLevel(3)
            Debug.Print Err.Number & " " & mnuLevel.Count & " " & mnuLevel.UBound: Err.Clear
            Debug.Print mnuLevel(3).Caption & "|" & mnuLevel(3).Checked & "|" & mnuLevel(3).Enabled & "|" & mnuLevel(3).Visible
            Load mnuLevel(7)
            Debug.Print Err.Number & " " & mnuLevel.Count & " " & mnuLevel.UBound: Err.Clear
            Unload mnuLevel(3)
            Debug.Print Err.Number & " " & mnuLevel.Count & " " & mnuLevel.UBound: Err.Clear
            Unload mnuLevel(0)
            Debug.Print Err.Number & "|" & Err.Description: Err.Clear
            Load mnuNew
            Debug.Print Err.Number & "|" & Err.Description: Err.Clear
            mnuLevel(5).Checked = True
            Debug.Print Err.Number: Err.Clear
            """);

        printed.Should().Equal(
            "0 4 3",
            "&Beginner|False|True|True",
            "0 5 7",
            "0 4 7",
            "362|Can't unload controls created at design time",
            "361|Can't load or unload this object",
            // vb6.exe's text names the element, "Control array element '5' doesn't exist", and HexIDE's doesn't.
            "340");
    }

    [AvaloniaFact]
    public async Task A_loaded_menu_copies_the_first_element_as_it_stands_Visible_included()
    {
        // The second probe's order. A loaded control starts hidden whatever element 0 is; a loaded menu doesn't.
        var printed = await Printed("""
            mnuLevel(0).Checked = True
            Load mnuLevel(3)
            Debug.Print mnuLevel(3).Checked
            mnuLevel(0).Checked = False
            mnuLevel(0).Enabled = False
            Load mnuLevel(4)
            Debug.Print mnuLevel(4).Enabled
            mnuLevel(0).Enabled = True
            mnuLevel(0).Caption = "Zero"
            mnuLevel(0).Visible = False
            Load mnuLevel(5)
            Debug.Print mnuLevel(5).Visible & " " & mnuLevel(5).Caption
            """);

        printed.Should().Equal("True", "False", "False Zero");
    }

    // ── on the menu itself ─────────────────────────────────────────────────────────────────────────

    private sealed class RecordingRoot : Window, IModuleExecutionRoot
    {
        public List<(string Name, IReadOnlyList<Vb6Value>? Args)> Calls { get; } = new();
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) => Calls.Add((name, args));
    }

    private static (Menu Bar, RecordingRoot Root, ModuleExecutionContext Ctx, ExecutionEnvironment Env) Hosted()
    {
        var (content, _, ctx, env) = Spawn(Form);
        var root = new RecordingRoot { Width = 400, Height = 300 };
        MergeAppResources(root);
        root.Content = content;
        root.Show();
        Dispatcher.UIThread.RunJobs();
        return (((DockPanel)content).Children.OfType<Menu>().Single(), root, ctx, env);
    }

    private static MenuItem Item(ItemsControl parent, string header) =>
        parent.Items.OfType<MenuItem>().First(i => (i.Header as string) == header);

    [AvaloniaFact]
    public void Choosing_an_element_of_a_menu_array_passes_its_Index()
    {
        var (bar, root, _, _) = Hosted();

        Item(Item(bar, "&Game"), "&Intermediate").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        root.Calls.Should().ContainSingle();
        root.Calls[0].Name.Should().Be("mnuLevel_Click");
        root.Calls[0].Args!.Single().Should().Be(new Vb6Value(1));
    }

    [AvaloniaFact]
    public void Choosing_a_menu_does_not_tick_or_untick_it_and_code_does()
    {
        var (bar, _, ctx, env) = Hosted();
        var game = Item(bar, "&Game");

        // VB6 leaves Checked to the program, which usually sets it in the item's Click.
        Item(game, "&Beginner").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Item(game, "&Intermediate").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Item(game, "&Beginner").IsChecked.Should().BeTrue();
        Item(game, "&Intermediate").IsChecked.Should().BeFalse();

        // And a tick set from code is drawn, on an item the .frm did not tick.
        ContainerRuntimeHarness.Run(ctx, env, "mnuLevel(1).Checked = True\r\n").GetAwaiter().GetResult();
        Item(game, "&Intermediate").IsChecked.Should().BeTrue();
        Item(game, "&Intermediate").ToggleType.Should().Be(MenuItemToggleType.CheckBox);
    }

    [AvaloniaFact]
    public async Task A_loaded_element_goes_into_the_menu_after_the_elements_below_it_and_is_chosen_with_its_Index()
    {
        var (bar, root, ctx, env) = Hosted();
        var game = Item(bar, "&Game");

        await ContainerRuntimeHarness.Run(ctx, env, "Load mnuLevel(3)\r\nmnuLevel(3).Caption = \"&Custom\"\r\n");
        Dispatcher.UIThread.RunJobs();

        game.Items.OfType<Control>().Select(c => c is MenuItem m ? m.Header as string : "-")
            .Should().Equal("&New", "-", "&Beginner", "&Intermediate", "&Expert", "&Custom");
        Item(game, "&Custom").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        root.Calls.Should().ContainSingle().Which.Args!.Single().Should().Be(new Vb6Value(3));

        await ContainerRuntimeHarness.Run(ctx, env, "Unload mnuLevel(3)\r\n");
        game.Items.OfType<MenuItem>().Select(m => m.Header as string).Should().NotContain("&Custom");
    }
}
