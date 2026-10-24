using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// An option group is one Tab stop (#1583). Each case replays the Tab and Shift+Tab steps of vb6.exe's probes on the
/// probe's own form and code, and every line is the one vb6.exe logged, native and p-code agreeing, on the oracle guest's
/// console (the oracle's "Tab and the arrow keys among option buttons"). Option1 to Option3 are on the form, Option2
/// selected; Option4 and Option5 are in Frame1, with none selected; TextBoxes stand between. Each GotFocus and Click logs
/// itself with every option's Value, a digit for one selected.
/// </summary>
public class OptionTabStopTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    internal const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "TabProbe"
           ClientHeight    =   4200
           ClientLeft      =   60
           ClientTop       =   345
           ClientWidth     =   4800
           ScaleHeight     =   4200
           ScaleWidth      =   4800
           Begin VB.TextBox Text3
              Height          =   315
              Left            =   2640
              TabIndex        =   8
              Text            =   "Text3"
              Top             =   2760
              Width           =   1215
           End
           Begin VB.Frame Frame1
              Caption         =   "Frame1"
              Height          =   1215
              Left            =   2640
              TabIndex        =   5
              Top             =   1320
              Width           =   1815
              Begin VB.OptionButton Option5
                 Caption         =   "Option5"
                 Height          =   255
                 Left            =   120
                 TabIndex        =   7
                 Top             =   720
                 Width           =   1215
              End
              Begin VB.OptionButton Option4
                 Caption         =   "Option4"
                 Height          =   255
                 Left            =   120
                 TabIndex        =   6
                 Top             =   360
                 Width           =   1215
              End
           End
           Begin VB.TextBox Text2
              Height          =   315
              Left            =   2640
              TabIndex        =   4
              Text            =   "Text2"
              Top             =   720
              Width           =   1215
           End
           Begin VB.OptionButton Option3
              Caption         =   "Option3"
              Height          =   255
              Left            =   240
              TabIndex        =   3
              Top             =   1680
              Width           =   1215
           End
           Begin VB.OptionButton Option2
              Caption         =   "Option2"
              Height          =   255
              Left            =   240
              TabIndex        =   2
              Top             =   1320
              Value           =   -1  'True
              Width           =   1215
           End
           Begin VB.OptionButton Option1
              Caption         =   "Option1"
              Height          =   255
              Left            =   240
              TabIndex        =   1
              Top             =   960
              Width           =   1215
           End
           Begin VB.TextBox Text1
              Height          =   315
              Left            =   240
              TabIndex        =   0
              Text            =   "Text1"
              Top             =   240
              Width           =   1215
           End
        End
        Attribute VB_Name = "Form1"
        Private Function Values() As String
            Values = IIf(Option1.Value, "1", "-") & IIf(Option2.Value, "2", "-") & IIf(Option3.Value, "3", "-") & " " _
                & IIf(Option4.Value, "4", "-") & IIf(Option5.Value, "5", "-")
        End Function
        Private Sub Option1_GotFocus(): Debug.Print "  Option1_GotFocus " & Values: End Sub
        Private Sub Option2_GotFocus(): Debug.Print "  Option2_GotFocus " & Values: End Sub
        Private Sub Option3_GotFocus(): Debug.Print "  Option3_GotFocus " & Values: End Sub
        Private Sub Option4_GotFocus(): Debug.Print "  Option4_GotFocus " & Values: End Sub
        Private Sub Option5_GotFocus(): Debug.Print "  Option5_GotFocus " & Values: End Sub
        Private Sub Option1_Click(): Debug.Print "  Option1_Click " & Values: End Sub
        Private Sub Option2_Click(): Debug.Print "  Option2_Click " & Values: End Sub
        Private Sub Option3_Click(): Debug.Print "  Option3_Click " & Values: End Sub
        Private Sub Option4_Click(): Debug.Print "  Option4_Click " & Values: End Sub
        Private Sub Option5_Click(): Debug.Print "  Option5_Click " & Values: End Sub
        Private Sub Text1_GotFocus(): Debug.Print "  Text1_GotFocus": End Sub
        Private Sub Text2_GotFocus(): Debug.Print "  Text2_GotFocus": End Sub
        Private Sub Text3_GotFocus(): Debug.Print "  Text3_GotFocus": End Sub
        Public Sub Focus_Text1(): Text1.SetFocus: End Sub
        Public Sub Focus_Text3(): Text3.SetFocus: End Sub
        Public Sub Focus_Option3(): Option3.SetFocus: End Sub
        Public Sub Focus_Option5(): Option5.SetFocus: End Sub
        Public Sub Focus_Option1(): Option1.SetFocus: End Sub
        Public Sub Focus_Option2(): Option2.SetFocus: End Sub

        """;

    internal sealed class Run : IDisposable
    {
        public required CancellationTokenSource Cancel { get; init; }
        public required List<string> Printed { get; init; }
        public required List<Exception> Errors { get; init; }
        public required Window Window { get; init; }
        public required Action Detach { get; init; }

        public void SetFocus(string name)
        {
            ((VBFormRuntime)Window).ExecuteSub("Focus_" + name);
            Pump();
        }

        public void Tab(bool shift = false)
        {
            Window.KeyPressQwerty(PhysicalKey.Tab, shift ? RawInputModifiers.Shift : RawInputModifiers.None);
            Pump();
        }

        public void Press(PhysicalKey key)
        {
            Window.KeyPressQwerty(key, RawInputModifiers.None);
            Pump();
        }

        public void Call(string sub)
        {
            ((VBFormRuntime)Window).ExecuteSub(sub);
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

    internal static Run Start(string source = Form)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source.ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form);
        var printed = new List<string>();
        var errors = new List<Exception>();
        Action<string> print = printed.Add;
        void OnFaulted(VBWindowContext _, Exception e) => errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => errors.Add(e);
        VBDebugConsole.Output += print;
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        var cancel = new CancellationTokenSource();
        _ = VBLoader.RunForm(form, cancel.Token, out var window);
        Pump();
        return new Run
        {
            Cancel = cancel, Printed = printed, Errors = errors, Window = window,
            Detach = () =>
            {
                VBDebugConsole.Output -= print;
                VBWindowContext.Faulted -= OnFaulted;
                VBWindowContext.RunTimeError -= OnRunTimeError;
            },
        };
    }

    internal static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Each step, and the lines vb6.exe logged after it.</summary>
    internal static void Replay(Run run, params (Action Step, string[] Logged)[] steps)
    {
        foreach (var (step, logged) in steps)
        {
            run.Printed.Clear();
            step();
            run.Errors.Should().BeEmpty();
            run.Printed.Should().Equal(logged);
        }
    }

    // The first probe's steps 1 to 15: a group with a selected option is one stop, at that option, and Tab selects
    // nothing there; a group with none takes the first option Tab reaches, and selects it, raising Click after GotFocus.
    [AvaloniaFact]
    public void Tab_and_Shift_Tab_stop_once_in_each_group_as_vb6_exe_s_do()
    {
        using var run = Start();
        run.SetFocus("Text1");

        Replay(run,
            (() => run.Tab(), ["  Option2_GotFocus -2- --"]),
            (() => run.Tab(), ["  Text2_GotFocus"]),
            (() => run.Tab(), ["  Option4_GotFocus -2- 4-", "  Option4_Click -2- 4-"]),
            (() => run.Tab(), ["  Text3_GotFocus"]),
            (() => run.Tab(), ["  Text1_GotFocus"]),
            (() => run.Tab(), ["  Option2_GotFocus -2- 4-"]),
            (() => run.Tab(), ["  Text2_GotFocus"]),
            (() => run.Tab(shift: true), ["  Option2_GotFocus -2- 4-"]),
            (() => run.Tab(shift: true), ["  Text1_GotFocus"]),
            (() => run.Tab(shift: true), ["  Text3_GotFocus"]),
            (() => run.Tab(shift: true), ["  Option4_GotFocus -2- 4-"]),
            (() => run.Tab(shift: true), ["  Text2_GotFocus"]),
            (() => run.Tab(shift: true), ["  Option2_GotFocus -2- 4-"]),
            (() => run.Tab(shift: true), ["  Text1_GotFocus"]));
    }

    // The second probe's steps 1 to 4: Shift+Tab into a group with none selected takes its last option, and selects it.
    [AvaloniaFact]
    public void Shift_Tab_into_a_group_with_none_selected_takes_its_last_option()
    {
        using var run = Start();
        run.SetFocus("Text3");

        Replay(run,
            (() => run.Tab(shift: true), ["  Option5_GotFocus -2- -5", "  Option5_Click -2- -5"]),
            (() => run.Tab(shift: true), ["  Text2_GotFocus"]),
            (() => run.Tab(shift: true), ["  Option2_GotFocus -2- -5"]));
    }

    // An option's own TabStop False still keeps the Tab key off it, as it did before the group rule. Not measured with an
    // option group in vb6.exe: this pins that the rule adds to TabStop and does not replace it.
    [AvaloniaFact]
    public void An_option_s_own_TabStop_False_still_keeps_Tab_off_it()
    {
        using var run = Start();
        var option4 = OptionNamed(run, "Option4");
        KeyboardNavigation.SetIsTabStop(option4, false);
        Pump();
        run.SetFocus("Text1");

        Replay(run,
            (() => run.Tab(), ["  Option2_GotFocus -2- --"]),
            (() => run.Tab(), ["  Text2_GotFocus"]),
            (() => run.Tab(), ["  Option5_GotFocus -2- -5", "  Option5_Click -2- -5"]));
        KeyboardNavigation.GetIsTabStop(option4).Should().BeFalse("its group has a selected option now, and it is not that one");
    }

    private static VBOptionButton OptionNamed(Run run, string name) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(run.Window).OfType<VBOptionButton>()
            .Single(o => VBProps.GetName(o) == name);

    // The second probe's steps 7 and 10: SetFocus selects an option, but raises no Click on one selected already. Its
    // focus was at Text1 before step 7.
    [AvaloniaFact]
    public void SetFocus_on_the_selected_option_raises_no_Click()
    {
        using var run = Start();
        run.SetFocus("Text3");
        run.Tab(shift: true);
        run.SetFocus("Text1");

        Replay(run,
            (() => run.SetFocus("Option5"), ["  Option5_GotFocus -2- -5"]),
            (() => run.SetFocus("Option3"), ["  Option3_GotFocus --3 -5", "  Option3_Click --3 -5"]));
    }
}
