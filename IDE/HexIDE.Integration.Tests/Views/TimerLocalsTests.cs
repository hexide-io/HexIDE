using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The Locals window lists a Timer, a control array and a form's <c>Me</c> as VB6's does (#2329). hexide-int measured VB6's
/// rows on #2289 (SP6, 6.00.9782): a form with Timer1 (Enabled False, Interval 1000, Tag t1) and Timer2, element 0 of a
/// control array, paused in Form_Load. Its Type column writes declared and actual types, as <c>Timer/Timer</c>, where
/// HexIDE's writes one: a Timer row's declared type, or an object's class (#2700).
/// </summary>
public class TimerLocalsTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Form =
        "VERSION 5.00\r\n"
      + "Begin VB.Form Form1 \r\n"
      + "   Caption         =   \"Form1\"\r\n"
      + "   ClientHeight    =   3000\r\n"
      + "   ClientWidth     =   4000\r\n"
      + "   Begin VB.Timer Timer1 \r\n"
      + "      Enabled         =   0   'False\r\n"
      + "      Interval        =   1000\r\n"
      + "      Left            =   120\r\n"
      + "      Tag             =   \"t1\"\r\n"
      + "      Top             =   120\r\n"
      + "   End\r\n"
      + "   Begin VB.Timer Timer2 \r\n"
      + "      Index           =   0\r\n"
      + "      Interval        =   250\r\n"
      + "      Left            =   600\r\n"
      + "      Top             =   120\r\n"
      + "   End\r\n"
      + "End\r\n"
      + "Attribute VB_Name = \"Form1\"\r\n"
      + "Private Sub Form_Load()\r\n"
      + "End Sub\r\n";

    private static (DebugNode Me, IReadOnlyList<DebugNode> Rows, VBFormRuntime Window) LoadForm()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, Form, new NullSink())!;
        project.AddForm(form);
        VBLoader.RunForm(form, CancellationToken.None, out var window);
        Dispatcher.UIThread.RunJobs();
        var instance = window.Context.FormInstance!;
        var me = DebugInspector.FormNode("Me", "Form1", instance, window, instance.InstanceEnv, window.Context.ExecutionContext, "Form1", 0);
        return (me, me.Expand(), window);
    }

    private static (string Name, string Value, string Type)[] Read(IEnumerable<DebugNode> rows) =>
        [.. rows.Select(r => (r.Name, r.Value, r.TypeName))];

    [AvaloniaFact]
    public void A_Timer_lists_VB6s_seven_rows_with_each_members_declared_type()
    {
        var (_, rows, window) = LoadForm();
        try
        {
            var timer1 = rows.Single(r => r.Name == "Timer1").Expand();

            Read(timer1).Should().Equal(
                ("[_Default]", "False", "Boolean"),
                ("Enabled", "False", "Boolean"),
                ("Index", "<Object not an array>", "Integer"),
                ("Interval", "1000", "Long"),
                ("Name", "\"Timer1\"", "String"),
                ("Parent", "", "Form1"),
                ("Tag", "\"t1\"", "String"));
            var parent = timer1.Single(r => r.Name == "Parent");
            parent.HasChildren.Should().BeTrue("VB6's Parent row opens to the form, under Me too");
            parent.Repeats.Should().BeTrue("under Me, the form it opens is already open above it");
            parent.Expand().First().Name.Should().Be("[_Default]", "it opens as Me does");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_control_array_lists_its_elements_as_Item_1_on_whatever_their_Index()
    {
        var (_, rows, window) = LoadForm();
        try
        {
            var array = rows.Single(r => r.Name == "Timer2");
            var items = array.Expand();

            items.Select(r => r.Name).Should().Equal("Item 1");
            Read(items[0].Expand()).Should().Equal(
                ("[_Default]", "True", "Boolean"),
                ("Enabled", "True", "Boolean"),
                ("Index", "0", "Integer"),
                ("Interval", "250", "Long"),
                ("Name", "\"Timer2\"", "String"),
                ("Parent", "", "Form1"),
                ("Tag", "\"\"", "String"));
        }
        finally { window.Close(); }
    }

    // hexide-int measured the order with five controls on #2329 (the oracle's #2698): Me's members and controls are one
    // list sorted by name, case ignored, after [_Default]; the Item rows follow, one per control, in the file's order.
    [AvaloniaFact]
    public void Me_lists_its_default_member_then_members_and_controls_sorted_then_an_Item_row_per_control()
    {
        var (_, rows, window) = LoadForm();
        try
        {
            Read(rows.Take(1)).Should().Equal(
                ("[_Default]", "<Wrong number of arguments or invalid property assignment>", "Object"));
            var sorted = rows.Skip(1).TakeWhile(r => !r.Name.StartsWith("Item ", System.StringComparison.Ordinal)).ToList();
            sorted.Select(r => r.Name).Should().BeInAscendingOrder(System.StringComparer.OrdinalIgnoreCase)
                .And.Contain("Timer1").And.Contain("Timer2").And.Contain("Caption");
            rows.Skip(1 + sorted.Count).Select(r => (r.Name, r.TypeName))
                .Should().Equal(("Item 1", "Timer"), ("Item 2", "Timer"));
        }
        finally { window.Close(); }
    }
}
