using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The order the designer draws a form's controls in and writes their blocks in, against what VB6's designer does (#1481).
/// </summary>
/// <remarks>
/// <para>
/// Measured in VB6's IDE (docs/vb6-fidelity-oracle.md, "Where VB6's designer puts a control's block"): the first block in
/// a container is the control drawn on top, in the designer and at run time. Each container's blocks run windowed
/// controls first, then windowless ones (Label, Shape, Line, Image), which are drawn under every windowed control. A new
/// or pasted control goes first in its plane's run, and Bring to Front and Send to Back move a block to the start or the
/// end of its plane's run, with what it holds. TabIndex is not touched.
/// </para>
/// <para>
/// HexIDE drew the last block on top and wrote a new control's block last, so a control added in HexIDE was the bottom
/// one when VB6 opened the file, and Bring to Front and Send to Back each saved the opposite of what they showed.
/// </para>
/// <para>
/// A form is written here as a spec, its blocks in file order with a container's contents in brackets. Each expected
/// spec below is the order VB6 wrote for the same steps, with HexIDE's names where they differ from VB6's (#540).
/// </para>
/// </remarks>
public class BeginOrderTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private sealed record Node(string Name, List<Node> Children);

    private static List<Node> Parse(string spec)
    {
        var at = 0;
        List<Node> Siblings()
        {
            var nodes = new List<Node>();
            while (at < spec.Length && spec[at] != ')')
            {
                var start = at;
                while (at < spec.Length && spec[at] is not ('(' or ')' or ','))
                    at++;
                var node = new Node(spec[start..at], []);
                if (at < spec.Length && spec[at] == '(')
                {
                    at++;
                    node.Children.AddRange(Siblings());
                    at++;
                }
                nodes.Add(node);
                if (at < spec.Length && spec[at] == ',')
                    at++;
            }
            return nodes;
        }
        return Siblings();
    }

    private static string Spec(IEnumerable<Node> nodes) =>
        string.Join(",", nodes.Select(n => n.Children.Count == 0 ? n.Name : $"{n.Name}({Spec(n.Children)})"));

    private static readonly (string Prefix, string VbType)[] Classes =
    [
        ("Picture", "VB.PictureBox"), ("Frame", "VB.Frame"), ("Command", "VB.CommandButton"), ("Label", "VB.Label"),
        ("Line", "VB.Line"), ("Timer", "VB.Timer"), ("mnu", "VB.Menu"),
    ];

    // Every control at its own place, so a copy can be told from its source's sibling by where it is.
    private static string Frm(string spec)
    {
        var text = new StringBuilder(
            "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"Form1\"\r\n" +
            "   ClientHeight    =   4500\r\n   ClientWidth     =   6000\r\n");
        var n = 0;
        void Write(IEnumerable<Node> nodes, string indent)
        {
            foreach (var node in nodes)
            {
                n++;
                var vbType = Classes.First(c => node.Name.StartsWith(c.Prefix, System.StringComparison.Ordinal)).VbType;
                text.Append($"{indent}Begin {vbType} {node.Name} \r\n");
                var p = indent + "   ";
                switch (vbType)
                {
                    case "VB.Menu":
                        text.Append($"{p}Caption         =   \"{node.Name}\"\r\n");
                        break;
                    case "VB.Line":
                        text.Append($"{p}X1              =   {120 * n}\r\n{p}X2              =   {120 * n + 600}\r\n");
                        text.Append($"{p}Y1              =   {120 * n}\r\n{p}Y2              =   {120 * n}\r\n");
                        break;
                    case "VB.Timer":
                        text.Append($"{p}Left            =   {120 * n}\r\n{p}Top             =   {120 * n}\r\n");
                        break;
                    default:
                        if (vbType is not "VB.PictureBox")
                            text.Append($"{p}Caption         =   \"{node.Name}\"\r\n");
                        text.Append($"{p}Height          =   495\r\n{p}Left            =   {120 * n}\r\n");
                        text.Append($"{p}TabIndex        =   {n}\r\n{p}Top             =   {60 * n}\r\n{p}Width           =   1215\r\n");
                        break;
                }
                Write(node.Children, p);
                text.Append($"{indent}End\r\n");
            }
        }
        Write(Parse(spec), "   ");
        return text.Append("End\r\nAttribute VB_Name = \"Form1\"\r\n").ToString();
    }

    private static (FormEditViewModel Vm, FormDefinition Form) Open(string spec, IWindowManager? windows = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, Frm(spec), new NullSink())!;
        project.AddForm(form);
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IComponentRegistry>(), Substitute.For<ILocalizationService>());
        var vm = new FormEditViewModel(toolbox, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!,
            windows!, Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
        return (vm, form);
    }

    // The blocks as the saved .frm holds them, as a spec.
    private static string Saved(FormEditViewModel vm, FormDefinition form)
    {
        vm.CommitLayout();
        var (text, _) = new FormSerializer().Serialize(form, "Form1.frm");
        var root = new Node("", []);
        var open = new Stack<(int Indent, Node Node)>();
        foreach (var line in text.Split("\r\n"))
        {
            var match = Regex.Match(line, @"^( *)Begin \S+ (\S+)");
            if (!match.Success)
                continue;
            var node = new Node(match.Groups[2].Value, []);
            while (open.Count > 0 && open.Peek().Indent >= match.Groups[1].Length)
                open.Pop();
            (open.Count > 0 ? open.Peek().Node.Children : root.Children).Add(node);
            open.Push((match.Groups[1].Length, node));
        }
        return Spec(root.Children.Single().Children);
    }

    // The order the canvas draws in, the last on top.
    private static string Drawn(FormEditViewModel vm) => string.Join(",", vm.Components.Select(c => c.Name));

    private static ComponentInstanceViewModel Find(FormEditViewModel vm, string name) =>
        vm.AllComponents.Single(c => c.Name == name);

    private static int TabIndexOf(FormEditViewModel vm, string name) =>
        Find(vm, name).Instance.GetPropertyOrDefault(VBProperties.TabIndexProperty);

    private static string Add(FormEditViewModel vm, ComponentBaseClass type)
    {
        vm.SpawnControlAt(type, new Rect(20, 20, 50, 30));
        return vm.SelectedComponent!.Name;
    }

    private static List<ComponentInstanceViewModel> CopyAndPaste(FormEditViewModel vm, string name)
    {
        var before = vm.AllComponents.ToList();
        var source = Find(vm, name);
        vm.SelectedComponent = source;
        vm.SetSelectedComponents([source]);
        vm.CopySelectedControls();
        vm.PasteControls();
        return vm.AllComponents.Except(before).ToList();
    }

    // ── load and save ───────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void The_canvas_draws_the_first_block_in_each_container_on_top_and_a_container_under_what_it_holds()
    {
        var (vm, _) = Open("Picture1,Picture2,Frame1(Picture3,Picture4)");

        Drawn(vm).Should().Be("Frame1,Picture4,Picture3,Picture2,Picture1",
            "VB6 draws Picture1 over Picture2 and Picture3 over Picture4, and Frame1 under both of its own");
    }

    [AvaloniaTheory]
    [InlineData("Picture1,Picture2,Frame1(Picture3,Picture4)")]
    [InlineData("Frame2(Picture6,Picture5,Command3),Command1,Picture2,Picture1,Frame1(Command2,Picture4,Picture3),Label2,Label1")]
    [InlineData("Command1,Line1,Line2,Timer1")]
    [InlineData("Label1,Command1,Label2")]
    [InlineData("Command1,Label1,mnuFile(mnuOpen,mnuExit),mnuHelp")]
    public void A_loaded_form_is_saved_in_the_order_it_was_read(string spec)
    {
        var (vm, form) = Open(spec);

        vm.HasComponentsNotInModel.Should().BeFalse("nothing has changed, so the form is not dirty");
        Saved(vm, form).Should().Be(spec);
    }

    // ── Bring to Front and Send to Back ──────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Bring_to_Front_writes_the_block_first_and_Send_to_Back_last_and_neither_moves_the_TabIndex()
    {
        var (vm, form) = Open("Picture1,Picture2,Frame1(Picture3,Picture4)");

        vm.BringToFront(Find(vm, "Picture2"));
        Saved(vm, form).Should().Be("Picture2,Picture1,Frame1(Picture3,Picture4)");

        vm.SendToBack(Find(vm, "Picture3"));
        Saved(vm, form).Should().Be("Picture2,Picture1,Frame1(Picture4,Picture3)");

        (TabIndexOf(vm, "Picture1"), TabIndexOf(vm, "Picture2"), TabIndexOf(vm, "Picture3")).Should().Be((1, 2, 4));
    }

    [AvaloniaFact]
    public void A_windowed_control_sent_to_back_stays_over_the_windowless_ones()
    {
        var (vm, form) = Open("Command1,Picture2,Frame1(Picture3),Label2,Label1");

        vm.SendToBack(Find(vm, "Command1"));

        Saved(vm, form).Should().Be("Picture2,Frame1(Picture3),Command1,Label2,Label1");
    }

    [AvaloniaFact]
    public void A_Label_moves_within_the_windowless_controls_and_never_over_a_windowed_one()
    {
        var (vm, form) = Open("Command1,Picture1,Label2,Label1");

        vm.SendToBack(Find(vm, "Label2"));
        Saved(vm, form).Should().Be("Command1,Picture1,Label1,Label2");

        vm.BringToFront(Find(vm, "Label2"));
        Saved(vm, form).Should().Be("Command1,Picture1,Label2,Label1");
        Drawn(vm).Should().Be("Label1,Label2,Picture1,Command1", "a Label is drawn under every windowed control");
    }

    [AvaloniaFact]
    public void A_container_brought_to_front_takes_what_it_holds_and_an_undo_puts_both_back()
    {
        var (vm, form) = Open("Picture1,Frame1(Picture3,Picture4)");

        vm.BringToFront(Find(vm, "Frame1"));

        Saved(vm, form).Should().Be("Frame1(Picture3,Picture4),Picture1");
        Drawn(vm).Should().Be("Picture1,Frame1,Picture4,Picture3", "the Frame is still drawn under its own contents");

        vm.UndoStack.Undo();

        Drawn(vm).Should().Be("Frame1,Picture4,Picture3,Picture1");
        Saved(vm, form).Should().Be("Picture1,Frame1(Picture3,Picture4)");
    }

    // ── a control added ──────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void A_control_added_is_written_first()
    {
        var (vm, form) = Open("Picture2,Picture1,Frame1(Picture4,Picture3)");

        var added = Add(vm, CommandButtonComponentClass.Instance);

        Saved(vm, form).Should().Be($"{added},Picture2,Picture1,Frame1(Picture4,Picture3)");
    }

    [AvaloniaFact]
    public void A_Label_added_is_written_first_among_the_windowless_controls_after_every_windowed_one()
    {
        var (vm, form) = Open("Command1,Picture1,Label1");

        var added = Add(vm, LabelComponentClass.Instance);

        Saved(vm, form).Should().Be($"Command1,Picture1,{added},Label1");
    }

    [AvaloniaFact]
    public void An_undone_and_redone_add_puts_the_control_back_where_it_was()
    {
        var (vm, form) = Open("Command1,Picture1,Label1");
        var added = Add(vm, LabelComponentClass.Instance);
        var drawn = Drawn(vm);

        vm.UndoStack.Undo();
        vm.UndoStack.Redo();

        Drawn(vm).Should().Be(drawn);
        Saved(vm, form).Should().Be($"Command1,Picture1,{added},Label1");
    }

    // ── a control pasted ─────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void A_pasted_control_is_written_first_and_a_pasted_Label_first_among_the_Labels()
    {
        var (vm, form) = Open("Command1,Picture1,Label1");

        var picture = CopyAndPaste(vm, "Picture1").Single().Name;
        var label = CopyAndPaste(vm, "Label1").Single().Name;

        Saved(vm, form).Should().Be($"{picture},Command1,Picture1,{label},Label1");
    }

    // VB6 pastes a container's contents one at a time, each on top, so its copy of this Frame holds them the other way up
    // (measured). HexIDE keeps the source's stacking, as a copy should; names and TabIndexes follow the source's order in
    // both.
    [AvaloniaFact]
    public void A_pasted_container_holds_its_contents_stacked_as_its_source_does()
    {
        var (vm, form) = Open("Picture1,Frame1(Command2,Picture4,Picture3)");

        var copies = CopyAndPaste(vm, "Frame1");

        var frame = copies.Single(c => c.Instance.BaseClass is FrameComponentClass);
        // A copy keeps its source's place in the Frame, so where it is says which control it copies.
        string CopyOf(string source) => copies.Single(c =>
            c.Instance.Container == frame.Instance && c.Left == Find(vm, source).Left && c.Top == Find(vm, source).Top).Name;
        var (command, picture4, picture3) = (CopyOf("Command2"), CopyOf("Picture4"), CopyOf("Picture3"));

        Saved(vm, form).Should().Be($"{frame.Name}({command},{picture4},{picture3}),Picture1,Frame1(Command2,Picture4,Picture3)");
        Drawn(vm).Should().EndWith($"{frame.Name},{picture3},{picture4},{command}");
        new[] { TabIndexOf(vm, command), TabIndexOf(vm, picture4), TabIndexOf(vm, picture3) }.Should().BeInAscendingOrder();
    }

    [AvaloniaFact]
    public void An_undone_and_redone_paste_puts_every_control_back_where_it_was()
    {
        var (vm, form) = Open("Picture1,Frame1(Command2,Picture4,Picture3)");
        CopyAndPaste(vm, "Frame1");
        var (drawn, saved) = (Drawn(vm), Saved(vm, form));

        vm.UndoStack.Undo();
        Saved(vm, form).Should().Be("Picture1,Frame1(Command2,Picture4,Picture3)");
        vm.UndoStack.Redo();

        Drawn(vm).Should().Be(drawn);
        Saved(vm, form).Should().Be(saved);
    }

    // ── menus ────────────────────────────────────────────────────────────────────────────────────────────

    // VB6's files put every menu after every control: no corpus file has a control after a menu. A menu made in the menu
    // editor used to sort with the first control drawn, so it could be written among them.
    [AvaloniaFact]
    public async Task A_menu_made_in_the_menu_editor_is_written_after_every_control()
    {
        var windows = Substitute.For<IWindowManager>();
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(call =>
        {
            ((MenuEditorViewModel)call[0]).FlatMenu.Add(new MenuViewModel { Name = "mnuFile", Caption = "File" });
            return Task.FromResult(true);
        });
        var (vm, form) = Open("Command1,Picture1,Label1", windows);

        await vm.EditMenu();

        Saved(vm, form).Should().Be("Command1,Picture1,Label1,mnuFile");
    }

    // ── Lines ────────────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void A_Line_brought_to_front_is_drawn_over_the_other_Line()
    {
        var (vm, form) = Open("Line1,Line2");
        Drawn(vm).Should().Be("Line2,Line1", "the first Line is drawn over the second");

        vm.BringToFront(Find(vm, "Line2"));

        Saved(vm, form).Should().Be("Line2,Line1");
        Drawn(vm).Should().Be("Line1,Line2");
    }
}
