using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

public class SimpleComboTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    internal static string ProbeForm()
    {
        string Combo(string name, int left, int height, int tab) =>
            $"   Begin VB.ComboBox {name} \r\n      Height          =   {height}\r\n      Left            =   {left}\r\n      Style           =   1  'Simple Combo\r\n"
          + $"      TabIndex        =   {tab}\r\n      Text            =   \"{name}\"\r\n      Top             =   240\r\n      Width           =   2000\r\n   End\r\n";
        var events = string.Concat(
            from c in new[] { "cboS", "cboT", "cboU" }
            from e in new[] { "Click", "DblClick", "Change", "GotFocus", "DropDown" }
            select $"Private Sub {c}_{e}()\r\n    Debug.Print \"  {c}_{e} ListIndex \" & {c}.ListIndex & \" Text \" & {c}.Text\r\nEnd Sub\r\n");
        return "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"SimpleCombo\"\r\n   ClientHeight    =   2400\r\n   ClientLeft      =   60\r\n"
             + "   ClientTop       =   450\r\n   ClientWidth     =   7200\r\n   ScaleHeight     =   2400\r\n   ScaleWidth      =   7200\r\n"
             + Combo("cboU", 4800, 900, 2) + Combo("cboT", 2520, 400, 1) + Combo("cboS", 240, 1500, 0)
             + "End\r\nAttribute VB_Name = \"Form1\"\r\n"
             + "Private Sub Form_Load()\r\n    Dim i As Integer\r\n    Dim fruit\r\n"
             + "    fruit = Array(\"apple\", \"banana\", \"cherry\", \"date\", \"elder\", \"fig\", \"grape\", \"honey\")\r\n"
             + "    For i = 0 To 7: cboS.AddItem fruit(i): Next\r\n    For i = 0 To 2: cboT.AddItem fruit(i): Next\r\n"
             + "    For i = 0 To 1: cboU.AddItem fruit(i): Next\r\nEnd Sub\r\n" + events;
    }

    /// <summary>A running form, kept open so input can be sent to it; disposing closes it.</summary>
    private sealed class Running : IDisposable
    {
        public required VBFormRuntime Window { get; init; }
        public List<string> Printed { get; } = [];
        public List<Exception> Errors { get; } = [];
        public Action? Detach { get; set; }

        public VBComboBox Combo(string name) =>
            Window.GetVisualDescendants().OfType<VBComboBox>().First(c => VBProps.GetName(c) == name);

        public void Dispose()
        {
            Window.Close();
            Detach?.Invoke();
        }
    }

    private static Running Start()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, ProbeForm(), new NullSink())!;
        project.AddForm(form);
        VBLoader.RunForm(form, CancellationToken.None, out var window, null);
        var run = new Running { Window = window };
        void OnFaulted(VBWindowContext _, Exception e) => run.Errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => run.Errors.Add(e);
        VBDebugConsole.Output += run.Printed.Add;
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        run.Detach = () =>
        {
            VBDebugConsole.Output -= run.Printed.Add;
            VBWindowContext.Faulted -= OnFaulted;
            VBWindowContext.RunTimeError -= OnRunTimeError;
        };
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        // The probe's form gave cboS, TabIndex 0, the focus as it opened.
        run.Combo("cboS").Focus();
        Dispatcher.UIThread.RunJobs();
        run.Printed.Clear();
        return run;
    }

    // Presses and releases the real pointer at a point of a visual, as the probe's mouse_event did.
    private static void ClickAt(Visual target, double x, double y, int times = 1)
    {
        var top = TopLevel.GetTopLevel(target)!;
        var point = target.TranslatePoint(new Point(x, y), top)!.Value;
        for (var i = 0; i < times; i++)
        {
            top.MouseDown(point, MouseButton.Left);
            top.MouseUp(point, MouseButton.Left);
        }
        Dispatcher.UIThread.RunJobs();
    }

    // An item, in its middle, or a few pixels above it, so that a click there is a new click rather than a double click's.
    private static void ClickItem(Running run, string combo, int index, int times = 1, bool higher = false)
    {
        var item = run.Combo(combo).ContainerFromIndex(index)!;
        ClickAt(item, 20, item.Bounds.Height / 2 - (higher ? 5 : 0), times);
    }

    // Below the items, at the foot of the list.
    private static void ClickBelowItems(Running run, string combo)
    {
        var list = run.Combo(combo).GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_List");
        ClickAt(list, 20, list.Bounds.Height - 3);
    }

    private static void Replay(Running run, params (string Step, Action Act, string[] Lines)[] steps)
    {
        foreach (var (step, act, lines) in steps)
        {
            run.Printed.Clear();
            act();
            run.Errors.Should().BeEmpty(step);
            run.Printed.Should().Equal(lines, step);
        }
    }

    // The first probe, every step, with each line vb6.exe logged after it: a click on an item picks it, a double click is
    // one Click and then DblClick, a click below the items picks the last of them, and none of it raises Change.
    [AvaloniaFact]
    public void A_click_on_the_list_picks_an_item_as_vb6_exe_s_does()
    {
        using var run = Start();

        Replay(run,
            ("click cboS item 1", () => ClickItem(run, "cboS", 1), ["  cboS_Click ListIndex 1 Text banana"]),
            ("double-click cboS item 2", () => ClickItem(run, "cboS", 2, times: 2),
                ["  cboS_Click ListIndex 2 Text cherry", "  cboS_DblClick ListIndex 2 Text cherry"]),
            ("click cboU below its items", () => ClickBelowItems(run, "cboU"),
                ["  cboU_GotFocus ListIndex 1 Text cboU", "  cboU_Click ListIndex 1 Text banana"]),
            ("click cboU item 0", () => ClickItem(run, "cboU", 0), ["  cboU_Click ListIndex 0 Text apple"]));
    }

    // The second probe, every step: a click on an item already selected, or below the items when the last is selected,
    // raises Click again, and a double click on a combo without the focus takes the focus first.
    [AvaloniaFact]
    public void A_click_on_the_selected_item_raises_Click_again_as_vb6_exe_s_does()
    {
        using var run = Start();

        Replay(run,
            ("click cboU item 1", () => ClickItem(run, "cboU", 1),
                ["  cboU_GotFocus ListIndex 1 Text cboU", "  cboU_Click ListIndex 1 Text banana"]),
            ("click cboU below its items", () => ClickBelowItems(run, "cboU"), ["  cboU_Click ListIndex 1 Text banana"]),
            ("click cboU item 0", () => ClickItem(run, "cboU", 0), ["  cboU_Click ListIndex 0 Text apple"]),
            ("click cboU below its items", () => ClickBelowItems(run, "cboU"), ["  cboU_Click ListIndex 1 Text banana"]),
            ("click cboS item 3", () => ClickItem(run, "cboS", 3),
                ["  cboS_GotFocus ListIndex 3 Text cboS", "  cboS_Click ListIndex 3 Text date"]),
            ("click cboS item 3 again", () => ClickItem(run, "cboS", 3, higher: true), ["  cboS_Click ListIndex 3 Text date"]),
            ("double-click cboU item 1", () => ClickItem(run, "cboU", 1, times: 2),
            [
                "  cboU_GotFocus ListIndex 1 Text banana", "  cboU_Click ListIndex 1 Text banana",
                "  cboU_DblClick ListIndex 1 Text banana",
            ]));
    }

    // The third probe: F4 and Alt+Down, which drop a Dropdown Combo's list, raised nothing on a Simple Combo, whose list
    // is always shown.
    [AvaloniaFact]
    public void F4_and_Alt_Down_raise_nothing_as_vb6_exe_s_do()
    {
        using var run = Start();

        foreach (var (step, mods) in new[] { ("F4", RawInputModifiers.None), ("Alt+Down", RawInputModifiers.Alt) })
        {
            var (key, physical) = step == "F4" ? (Key.F4, PhysicalKey.F4) : (Key.Down, PhysicalKey.ArrowDown);
            run.Window.KeyPress(key, mods, physical, null);
            run.Window.KeyRelease(key, mods, physical, null);
            Dispatcher.UIThread.RunJobs();

            run.Errors.Should().BeEmpty(step);
            run.Printed.Should().BeEmpty(step);
            run.Combo("cboS").IsDropDownOpen.Should().BeFalse(step);
        }
    }

    private static Border ListOf(VBComboBox combo) =>
        combo.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_List");

    // vb6.exe drew a Simple Combo as its edit box with its list under it, touching, and no button. The list scrolled when
    // its items didn't fit, as cboS's eight did, and didn't where they did, as cboU's two did.
    [AvaloniaFact]
    public void A_Simple_Combo_shows_its_list_under_its_edit_box_and_no_button()
    {
        using var run = Start();
        var cboS = run.Combo("cboS");
        var cboU = run.Combo("cboU");

        foreach (var combo in new[] { cboS, cboU })
        {
            combo.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Should().BeEmpty("it has no button");
            var edit = combo.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "PART_EditableTextBox");
            edit.Bounds.Height.Should().Be(21, "vb6.exe drew the edit box 21 pixels high at its default font");
            var list = ListOf(combo);
            list.IsEffectivelyVisible.Should().BeTrue();
            list.TranslatePoint(default, combo)!.Value.Y.Should().Be(21, "the list starts where the edit box ends");
            (list.Bounds.Height + 21).Should().Be(combo.Bounds.Height, "the control's Height is the two together");
            combo.IsDropDownOpen.Should().BeFalse();
        }
        ScrollerOf(cboS).Extent.Height.Should().BeGreaterThan(ScrollerOf(cboS).Viewport.Height, "eight items don't fit");
        ScrollerOf(cboU).Extent.Height.Should().BeLessThanOrEqualTo(ScrollerOf(cboU).Viewport.Height, "two items do");
    }

    private static ScrollViewer ScrollerOf(VBComboBox combo) => ListOf(combo).GetVisualDescendants().OfType<ScrollViewer>().First();

    // vb6.exe's designer drew a Simple Combo as its edit box, holding its Text, over its empty list, and no button.
    [AvaloniaFact]
    public void The_designer_draws_a_Simple_Combo_as_its_edit_box_over_an_empty_list()
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), ProbeForm(), new NullSink())!;
        var instance = form.Components.Single(c => c.GetPropertyOrDefault(HexIDE.Runtime.Components.VBProperties.NameProperty) == "cboS");
        var combo = (VBComboBox)((HexIDE.Runtime.Components.ComponentBaseClass)instance.BaseClass).Instantiate(instance);
        var window = new Window { Width = 240, Height = 160, Content = combo };
        combo.Width = 134;
        combo.Height = 100;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        combo.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "PART_EditableTextBox").Text.Should().Be("cboS");
        combo.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Should().BeEmpty();
        ListOf(combo).Bounds.Height.Should().Be(79);
        combo.ItemCount.Should().Be(0);
        window.Close();
    }
}
