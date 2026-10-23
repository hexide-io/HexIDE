using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// GotFocus and LostFocus on every focusable control, and a Form or UserControl saved with Enabled = False (#1101).
/// Every expectation was measured on vb6.exe, native and p-code agreeing, with a compiled probe on the oracle guest's
/// console: the oracle's "GotFocus and LostFocus on each control".
/// </summary>
public class FocusEventTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static string Control(string type, string name, int top, string extra = "", string inside = "") =>
        $"   Begin VB.{type} {name} \r\n"
      + extra
      + "      Height          =   315\r\n"
      + "      Left            =   120\r\n"
      + $"      Top             =   {top}\r\n"
      + "      Width           =   1200\r\n"
      + inside
      + "   End\r\n";

    private static readonly string Controls =
        Control("TextBox", "Text1", 120, "      Text            =   \"Text1\"\r\n")
      + Control("CheckBox", "Check1", 480, "      Caption         =   \"Check1\"\r\n")
      + Control("OptionButton", "Option1", 840, "      Caption         =   \"Option1\"\r\n")
      + Control("OptionButton", "Option2", 1200, "      Caption         =   \"Option2\"\r\n")
      + Control("ListBox", "List1", 1560)
      + Control("ComboBox", "Combo1", 1920, "      Text            =   \"Combo1\"\r\n")
      + Control("ComboBox", "Combo2", 2280, "      Style           =   2  'Dropdown List\r\n")
      + Control("ComboBox", "Combo3", 2640, "      Style           =   1  'Simple Combo\r\n      Text            =   \"Combo3\"\r\n")
      + Control("HScrollBar", "HScroll1", 3000)
      + Control("VScrollBar", "VScroll1", 3360)
      + Control("PictureBox", "Picture1", 3720)
      + Control("PictureBox", "Picture2", 4080, inside:
            "      Begin VB.TextBox Text2 \r\n"
          + "         Height          =   315\r\n"
          + "         Left            =   60\r\n"
          + "         Text            =   \"Text2\"\r\n"
          + "         Top             =   0\r\n"
          + "         Width           =   1000\r\n"
          + "      End\r\n");

    private static readonly string[] Focusable =
        ["Text1", "Text2", "Check1", "Option1", "Option2", "List1", "Combo1", "Combo2", "Combo3", "HScroll1", "VScroll1", "Picture1", "Picture2"];

    // Each control's GotFocus and LostFocus, and the option buttons' and the ListBox's Click, log their names.
    private static readonly string Logged = string.Concat(Focusable.Select(name =>
            $"Private Sub {name}_GotFocus()\r\n    Debug.Print \"{name}_GotFocus\"\r\nEnd Sub\r\n"
          + $"Private Sub {name}_LostFocus()\r\n    Debug.Print \"{name}_LostFocus\"\r\nEnd Sub\r\n"))
      + string.Concat(new[] { "Option1", "Option2", "List1" }.Select(name =>
            $"Private Sub {name}_Click()\r\n    Debug.Print \"{name}_Click\"\r\nEnd Sub\r\n"))
      + string.Concat(Focusable.Select(name => $"Public Sub Focus_{name}()\r\n    {name}.SetFocus\r\nEnd Sub\r\n"));

    private sealed class Run : IDisposable
    {
        public required CancellationTokenSource Cancel { get; init; }
        public required List<string> Printed { get; init; }
        public required List<Exception> Errors { get; init; }
        public required Window Window { get; init; }
        public required Action Detach { get; init; }

        public T Named<T>(string name) where T : Control =>
            Window.GetLogicalDescendants().OfType<T>().Single(c => VBProps.GetName(c) == name);

        /// <summary>Move the focus as SetFocus does, from the program, and wait for its events.</summary>
        public void SetFocus(string name)
        {
            ((VBFormRuntime)Window).ExecuteSub("Focus_" + name);
            Pump();
        }

        public void Dispose()
        {
            Cancel.Cancel();
            Pump();
            Window.Close();
            Detach();
        }
    }

    private static Run Start(string form, ProjectDefinition? project = null,
        IReadOnlyDictionary<string, ComponentBaseClass>? userControls = null)
    {
        project ??= new ProjectDefinition(VBProjectType.EXE, "P");
        var form1 = new FormDeserializer().Deserialize(project, form.ReplaceLineEndings("\r\n"), new NullSink(),
            extraComponents: userControls)!;
        project.AddForm(form1);

        var printed = new List<string>();
        var errors = new List<Exception>();
        Action<string> print = printed.Add;
        void OnFaulted(VBWindowContext _, Exception e) => errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => errors.Add(e);
        VBDebugConsole.Output += print;
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        var cancel = new CancellationTokenSource();
        _ = VBLoader.RunForm(form1, cancel.Token, out var window);
        Pump();
        return new Run
        {
            Cancel = cancel,
            Printed = printed,
            Errors = errors,
            Window = window,
            Detach = () =>
            {
                VBDebugConsole.Output -= print;
                VBWindowContext.Faulted -= OnFaulted;
                VBWindowContext.RunTimeError -= OnRunTimeError;
            },
        };
    }

    private static Run Start() => Start(
        "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"Form1\"\r\n   ClientHeight    =   4800\r\n"
      + "   ClientWidth     =   4000\r\n" + Controls + "End\r\nAttribute VB_Name = \"Form1\"\r\n" + Logged);

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }

    // Measured: each raises GotFocus as it takes the focus and LostFocus as it gives it up, after the handler that called
    // SetFocus, the old control's first.
    [AvaloniaTheory]
    [InlineData("Check1")]
    [InlineData("List1")]
    [InlineData("Combo1")]
    [InlineData("Combo2")]
    [InlineData("Combo3")]
    [InlineData("HScroll1")]
    [InlineData("VScroll1")]
    [InlineData("Picture1")]
    [InlineData("Picture2")]
    public void Each_focusable_control_raises_GotFocus_and_LostFocus(string name)
    {
        using var run = Start();
        run.SetFocus("Text1");
        run.Printed.Clear();

        run.SetFocus(name);
        run.SetFocus("Text1");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Text1_LostFocus", $"{name}_GotFocus", $"{name}_LostFocus", "Text1_GotFocus");
    }

    // Measured: an option button that takes the focus is selected, and raises Click after its GotFocus. Windows checks an
    // automatic radio button as it is focused.
    [AvaloniaFact]
    public void An_option_button_taking_the_focus_is_selected_after_its_GotFocus()
    {
        using var run = Start();
        run.SetFocus("Text1");
        run.Printed.Clear();

        run.SetFocus("Option1");
        run.SetFocus("Option2");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal(
            "Text1_LostFocus", "Option1_GotFocus", "Option1_Click",
            "Option1_LostFocus", "Option2_GotFocus", "Option2_Click");
        run.Named<VBOptionButton>("Option1").Value.Should().BeFalse();
        run.Named<VBOptionButton>("Option2").Value.Should().BeTrue();
    }

    // Measured: VB6's Tab key goes to the selected option of a group, so it changes nothing there (#1583, where
    // OptionTabStopTests replays the Tab steps).
    [AvaloniaFact]
    public void The_Tab_key_does_not_change_which_option_is_selected()
    {
        using var run = Start();
        run.Named<VBOptionButton>("Option2").Value = true;
        run.SetFocus("Check1");
        run.Printed.Clear();

        for (var i = 0; i < 2; i++)
        {
            run.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Pump();
        }

        run.Errors.Should().BeEmpty();
        run.Printed.Should().NotContain(line => line.EndsWith("_Click"));
        run.Named<VBOptionButton>("Option1").Value.Should().BeFalse();
        run.Named<VBOptionButton>("Option2").Value.Should().BeTrue();
    }

    // Measured: a ListBox's selection moved by code or by a key raises its Click and no focus event. Here the focus moves
    // to an item, as a click on one moves it, and that is not the ListBox losing it.
    [AvaloniaFact]
    public void The_focus_moving_inside_a_control_raises_nothing()
    {
        using var run = Start();
        var list = run.Named<VBListBox>("List1");
        list.Items.Add("a");
        list.Items.Add("b");
        run.Window.UpdateLayout();
        run.SetFocus("List1");
        run.Printed.Clear();

        list.ContainerFromIndex(0)!.Focus();
        Pump();
        list.Focus();
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void A_ComboBox_whose_text_box_takes_the_focus_raises_one_GotFocus()
    {
        using var run = Start();
        run.SetFocus("Text1");
        run.Printed.Clear();

        var combo = run.Named<VBComboBox>("Combo1");
        var box = combo.GetVisualDescendants().OfType<TextBox>().First();
        box.Focus();
        Pump();
        run.SetFocus("Text1");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Text1_LostFocus", "Combo1_GotFocus", "Combo1_LostFocus", "Text1_GotFocus");
    }

    // Measured, with Tab: a PictureBox gives the focus up to a control inside it, as to any other.
    [AvaloniaFact]
    public void A_PictureBox_loses_the_focus_to_a_control_inside_it()
    {
        using var run = Start();
        run.SetFocus("Picture2");
        run.Printed.Clear();

        run.SetFocus("Text2");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Picture2_LostFocus", "Text2_GotFocus");
    }

    // Measured: a form saved disabled reads Enabled False, and its window is disabled, loaded or shown. Shown, it does not
    // become the active form, and the control that had the focus keeps it: Form1's raised no LostFocus, where showing an
    // enabled form took the focus from it.
    [AvaloniaFact]
    public void A_form_saved_disabled_runs_disabled()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddForm(new FormDeserializer().Deserialize(project,
            "VERSION 5.00\r\nBegin VB.Form Form2 \r\n   Caption         =   \"Form2\"\r\n   ClientHeight    =   1200\r\n"
          + "   ClientWidth     =   2400\r\n   Enabled         =   0   'False\r\n" + Control("TextBox", "Text1", 120)
          + "End\r\nAttribute VB_Name = \"Form2\"\r\n"
          + "Private Sub Form_Activate()\r\n    Debug.Print \"Form2_Activate\"\r\nEnd Sub\r\n", new NullSink())!);
        Window? form2 = null;
        using var watch = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) =>
        {
            if (w.Title == "Form2")
                form2 = w;
        });

        using var run = Start(
            "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"Form1\"\r\n   ClientHeight    =   1200\r\n"
          + "   ClientWidth     =   2400\r\n" + Control("TextBox", "Text1", 120)
          + "End\r\nAttribute VB_Name = \"Form1\"\r\n"
          + "Private Sub Text1_LostFocus()\r\n    Debug.Print \"Text1_LostFocus\"\r\nEnd Sub\r\n"
          + "Public Sub Probe()\r\n    Load Form2\r\n    Debug.Print \"loaded \" & Form2.Enabled\r\n    Form2.Show\r\n"
          + "    DoEvents\r\n    Debug.Print \"shown \" & Form2.Enabled\r\nEnd Sub\r\n", project);
        run.Named<VBTextBox>("Text1").Focus();
        Pump();
        ((VBFormRuntime)run.Window).ExecuteSub("Probe");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("loaded False", "shown False");
        form2!.IsEnabled.Should().BeFalse();
        form2.GetLogicalDescendants().OfType<VBTextBox>().Single().IsEffectivelyEnabled.Should().BeFalse();
        form2.Close();
    }

    // Measured: a UserControl whose .ctl saves Enabled = False is disabled where it is placed, and so is what it holds.
    [AvaloniaFact]
    public void A_UserControl_saved_disabled_runs_disabled()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var ctl = new FormDeserializer().Deserialize(project,
            "VERSION 5.00\r\nBegin VB.UserControl Gauge \r\n   ClientHeight    =   600\r\n   ClientLeft      =   0\r\n"
          + "   ClientTop       =   0\r\n   ClientWidth     =   1200\r\n   Enabled         =   0   'False\r\n"
          + "   ScaleHeight     =   600\r\n   ScaleWidth      =   1200\r\n" + Control("TextBox", "Inner", 0)
          + "End\r\nAttribute VB_Name = \"Gauge\"\r\n", new NullSink())!;
        var registry = new Dictionary<string, ComponentBaseClass>(StringComparer.OrdinalIgnoreCase);
        registry["P.Gauge"] = UserControlComponentClass.ForModule("P.Gauge", ctl, registry);

        using var run = Start(
            "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"Form1\"\r\n   ClientHeight    =   1200\r\n"
          + "   ClientWidth     =   2400\r\n   Begin P.Gauge Gauge1 \r\n      Height          =   600\r\n"
          + "      Left            =   120\r\n      Top             =   120\r\n      Width           =   1200\r\n   End\r\n"
          + "End\r\nAttribute VB_Name = \"Form1\"\r\n", project, registry);

        run.Errors.Should().BeEmpty();
        var gauge = run.Named<Control>("Gauge1");
        gauge.IsEnabled.Should().BeFalse();
        gauge.GetLogicalDescendants().OfType<VBTextBox>().Single().IsEffectivelyEnabled.Should().BeFalse();
    }
}
