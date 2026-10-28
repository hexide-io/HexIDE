using System;
using System.Linq;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Integration.Tests.Views;

public class MonthViewInputTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelChange_Cancel_is_ByRef_and_DateClick_reads_the_final_selection(bool cancel)
    {
        const string form = """
            VERSION 5.00
            Begin VB.Form Form1
               Begin MSComCtl2.MonthView MonthView1
                  Left = 0
                  Top = 0
                  Height = 2370
                  Width = 2700
                  CurrentDate = 37046
                  MultiSelect = -1
               End
            End
            Attribute VB_Name = "Form1"
            """;
        var window = new VBFormRuntime { Width = 300, Height = 250 };
        var definition = new HexIDE.Runtime.Serialization.FormDeserializer().Deserialize(
            new HexIDE.Runtime.ProjectElements.ProjectDefinition(HexIDE.Runtime.ProjectElements.VBProjectType.EXE, "P"), form, new Sink())!;
        window.Content = VBLoader.SpawnComponents(definition, window.Context.ExecutionContext, window.Context.RootEnv);
        window.Context.SetCode($"""
            Private seen As Integer
            Private Sub MonthView1_MouseDown(Button As Integer, Shift As Integer, x As Single, y As Single)
                If seen <> 0 Then Err.Raise 5
                seen = 1
            End Sub
            Private Sub MonthView1_MouseUp(Button As Integer, Shift As Integer, x As Single, y As Single)
                If seen <> 2 Then Err.Raise 5
                seen = 3
            End Sub
            Private Sub MonthView1_Click()
                If seen <> 3 Then Err.Raise 5
                seen = 4
            End Sub
            Private Sub MonthView1_SelChange(StartDate As Date, EndDate As Date, Cancel As Integer)
                If seen <> 1 Then Err.Raise 5
                If MonthView1.Value <> #6/26/2001# Then Err.Raise 5
                If MonthView1.SelStart <> StartDate Then Err.Raise 5
                If EndDate <> #6/26/2001# Then Err.Raise 5
                seen = 2
                Cancel = { (cancel ? "True" : "False") }
            End Sub
            Private Sub MonthView1_DateClick(DateClicked As Date)
                If seen <> 4 Then Err.Raise 5
                If DateClicked <> MonthView1.SelStart Then Err.Raise 5
                If DateClicked <> #{(cancel ? "6/4/2001" : "6/26/2001")}# Then Err.Raise 5
                seen = 5
            End Sub
            """, "Form1");
        Exception? fault = null;
        void Report(VBWindowContext context, Exception error) { if (ReferenceEquals(context, window.Context)) fault = error; }
        VBWindowContext.Faulted += Report;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var calendar = ((Avalonia.Controls.DockPanel)window.Content).Children.OfType<Canvas>().Single().Children.OfType<VBMonthView>().Single();
            calendar.TrySetProperty("Value", new Vb6Value(VbDate.FromDateTime(new DateTime(2001, 6, 4))));
            calendar.TrySetProperty("SelEnd", new Vb6Value(VbDate.FromDateTime(new DateTime(2001, 6, 10))));
            var point = calendar.TranslatePoint(new Point((30 % 7 + .5) * calendar.Bounds.Width / 7,
                (2 + 30 / 7 + .5) * calendar.Bounds.Height / 8), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            fault.Should().BeNull();
            window.Context.InstanceEnv.TryGetVariableLocation("seen", out var pressedSeen).Should().BeTrue();
            window.Context.ExecutionContext.State[pressedSeen].Value.Should().Be(2);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            fault.Should().BeNull();
            window.Context.InstanceEnv.TryGetVariableLocation("seen", out var seen).Should().BeTrue();
            window.Context.ExecutionContext.State[seen].Value.Should().Be(5);
            calendar.TryGetProperty("SelStart", out var start);
            calendar.TryGetProperty("SelEnd", out var end);
            start.Should().Be(new Vb6Value(VbDate.FromDateTime(new DateTime(2001, 6, cancel ? 4 : 26))));
            end.Should().Be(new Vb6Value(VbDate.FromDateTime(new DateTime(2001, 6, cancel ? 10 : 26))));
            calendar.TryGetProperty("Value", out var value);
            value.Should().Be(start);
        }
        finally { VBWindowContext.Faulted -= Report; window.Close(); }
    }

    private sealed class Sink : HexIDE.Runtime.Serialization.IDeserializeErrorSink { public void LogError(string _) { } }

    [AvaloniaTheory]
    [InlineData("TimedValue")]
    [InlineData("TimedBound")]
    [InlineData("OutsideRange")]
    [InlineData("MissingDay")]
    [InlineData("MultiSelect")]
    [InlineData("MonthRows")]
    public void Unmeasured_keyboard_edges_refuse_without_changing_selection(string scenario)
    {
        var calendar = new VBMonthView { Width = 180, Height = 158 };
        var date = scenario == "MissingDay" ? new DateTime(2001, 1, 31) : new DateTime(2001, 6, 15);
        if (scenario == "TimedValue") date = date.AddHours(4);
        calendar.TrySetProperty("Value", new Vb6Value(VbDate.FromDateTime(date)));
        if (scenario == "TimedBound") calendar.TrySetProperty("MinDate", new Vb6Value(VbDate.FromDateTime(new DateTime(2001, 6, 1, 4, 0, 0))));
        if (scenario == "OutsideRange") calendar.TrySetProperty("MaxDate", new Vb6Value(VbDate.FromDateTime(new DateTime(2001, 6, 30))));
        if (scenario == "MultiSelect") calendar.TrySetProperty("MultiSelect", new Vb6Value(true));
        if (scenario == "MonthRows") calendar.TrySetProperty("MonthRows", new Vb6Value(12));
        var window = new Window { Content = calendar, Width = 300, Height = 250 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            calendar.Focus();
            Action press = () => window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
            press.Should().Throw<NotImplementedException>();
            calendar.TryGetProperty("Value", out var after);
            after.Should().Be(new Vb6Value(VbDate.FromDateTime(date)));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(Key.Right, KeyModifiers.None, 6, 16, false)]
    [InlineData(Key.Down, KeyModifiers.None, 6, 22, false)]
    [InlineData(Key.PageDown, KeyModifiers.None, 7, 15, true)]
    [InlineData(Key.PageUp, KeyModifiers.Control, 6, 15, true)]
    [InlineData(Key.Home, KeyModifiers.None, 6, 1, false)]
    [InlineData(Key.End, KeyModifiers.None, 6, 30, false)]
    [InlineData(Key.K, KeyModifiers.None, 6, 15, false)]
    [InlineData(Key.Escape, KeyModifiers.None, 6, 15, false)]
    [InlineData(Key.Right, KeyModifiers.Shift, 6, 15, false)]
    public void Measured_keys_raise_events_around_the_selection(Key key, KeyModifiers modifiers, int month, int day, bool bold)
    {
        var calendar = new VBMonthView { Width = 180, Height = 158 };
        calendar.TrySetProperty("Value", new Vb6Value(VbDate.FromDateTime(new DateTime(2001, 6, 15))));
        if (modifiers == KeyModifiers.Shift) calendar.TrySetProperty("MultiSelect", new Vb6Value(true));
        VBProps.SetName(calendar, "MonthView1");
        var recorder = new ContainerRuntimeHarness.RecordingRoot { Child = calendar };
        var window = new Window { Content = recorder, Width = 300, Height = 250 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            calendar.Focus();
            recorder.Calls.Clear();
            // Control is the measured VB6 modifier here, rather than a host command shortcut.
            window.KeyPress(key, (RawInputModifiers)modifiers, PhysicalKey.None, null);
            var expected = new List<string> { "KeyDown" };
            if (bold) expected.Add("GetDayBold");
            if (key is Key.K or Key.Escape) expected.Add("KeyPress");
            else if (modifiers != KeyModifiers.Shift) expected.Add("SelChange");
            recorder.Calls.Select(c => c.Name).Should().Equal(expected.Select(n => "MonthView1_" + n));
            window.KeyRelease(key, (RawInputModifiers)modifiers, PhysicalKey.None, null);
            expected.Add("KeyUp");
            recorder.Calls.Select(c => c.Name).Should().Equal(expected.Select(n => "MonthView1_" + n));
            var target = new Vb6Value(VbDate.FromDateTime(new DateTime(key == Key.PageUp ? 2000 : 2001, month, day)));
            foreach (var name in new[] { "Value", "SelStart", "SelEnd" })
            {
                calendar.TryGetProperty(name, out var value);
                value.Should().Be(target);
            }
            foreach (var call in recorder.Calls.Where(c => c.Name.EndsWith("SelChange", StringComparison.Ordinal)))
                call.Args!.Take(2).Should().Equal(target, target);
            if (key is Key.K or Key.Escape)
                recorder.Calls[1].Args![0].Value.Should().Be(key == Key.K ? 107 : 27);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(20, false)]
    [InlineData(22, true)]
    [InlineData(32, false)]
    public void Left_date_clicks_raise_the_measured_events(int day, bool twice)
    {
        var calendar = new VBMonthView { Width = 180, Height = 158 };
        calendar.TrySetProperty("Value", new Vb6Value(VbDate.FromDateTime(new DateTime(2001, 6, 15))));
        VBProps.SetName(calendar, "MonthView1");
        var first = new TextBox();
        var recorder = new ContainerRuntimeHarness.RecordingRoot
        {
            Child = new StackPanel { Children = { first, calendar } },
        };
        var window = new Window { Content = recorder, Width = 300, Height = 300 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            first.Focus();
            recorder.Calls.Clear();
            var date = new DateTime(2001, 6, 1).AddDays(day - 1);
            var index = day + 4; // June 1 is Friday, in the grid starting May 27.
            var point = calendar.TranslatePoint(new Point((index % 7 + .5) * 180 / 7,
                (2 + index / 7 + .5) * 158 / 8), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            var expected = new List<string>
                { "MouseDown", "GotFocus", "MouseUp", "Click" };
            if (day > 30) expected.Add("GetDayBold");
            expected.AddRange(["SelChange", "DateClick"]);
            if (twice)
            {
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                expected.AddRange(["DblClick", "DateDblClick", "MouseUp", "SelChange", "DateClick"]);
            }
            recorder.Calls.Select(c => c.Name).Should().Equal(expected.Select(n => "MonthView1_" + n));
            calendar.TryGetProperty("Value", out var value);
            value.Should().Be(new Vb6Value(VbDate.FromDateTime(date)));
            foreach (var call in recorder.Calls.Where(c => c.Name.EndsWith("DateClick", StringComparison.Ordinal)
                || c.Name.EndsWith("DateDblClick", StringComparison.Ordinal)))
                call.Args![0].Should().Be(value);
            var selection = recorder.Calls.Last(c => c.Name == "MonthView1_SelChange").Args!;
            selection.Take(2).Should().Equal(value, value);
            selection[2].Value.Should().Be(0);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Month_buttons_select_at_the_press_before_GetDayBold_and_never_DateClick(bool next)
    {
        var calendar = new VBMonthView { Width = 180, Height = 158 };
        calendar.TrySetProperty("Value", new Vb6Value(VbDate.FromDateTime(new DateTime(2001, 6, 15))));
        VBProps.SetName(calendar, "MonthView1");
        var recorder = new ContainerRuntimeHarness.RecordingRoot { Child = calendar };
        var window = new Window { Content = recorder, Width = 300, Height = 250 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            calendar.Focus();
            recorder.Calls.Clear();
            var point = calendar.TranslatePoint(new Point(next ? calendar.Bounds.Width - 5 : 5, 5), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            recorder.Calls.Select(c => c.Name).Should().Equal("MonthView1_MouseDown", "MonthView1_SelChange", "MonthView1_GetDayBold");
            var target = new DateTime(2001, next ? 7 : 5, 15);
            calendar.TryGetProperty("Value", out var value);
            value.Should().Be(new Vb6Value(VbDate.FromDateTime(target)));
            var bold = recorder.Calls.Last().Args!;
            bold[0].Should().Be(new Vb6Value(VbDate.FromDateTime(next ? new DateTime(2001, 7, 1) : new DateTime(2001, 4, 29))));
            bold[1].Value.Should().Be(42);
            var states = (VBArray)bold[2].Value!;
            states.LowerBound(1).Should().Be(0);
            states.UpperBound(1).Should().Be(587);
            window.MouseUp(point, MouseButton.Left);
            recorder.Calls.Select(c => c.Name).Should().Equal("MonthView1_MouseDown", "MonthView1_SelChange", "MonthView1_GetDayBold", "MonthView1_MouseUp", "MonthView1_Click");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("MissingDay")]
    [InlineData("TimedValue")]
    [InlineData("OutsideRange")]
    [InlineData("ScrollRate")]
    [InlineData("MonthRows")]
    [InlineData("MultiSelect")]
    public void Pending_navigation_cases_refuse_instead_of_guessing(string scenario)
    {
        var calendar = new VBMonthView { Width = 180, Height = 158 };
        var date = scenario == "MissingDay" ? new DateTime(2001, 1, 31) : new DateTime(2001, 6, 15);
        if (scenario == "TimedValue") date = date.AddHours(4);
        calendar.TrySetProperty("Value", new Vb6Value(VbDate.FromDateTime(date)));
        if (scenario == "OutsideRange") calendar.TrySetProperty("MaxDate", new Vb6Value(VbDate.FromDateTime(new DateTime(2001, 6, 30))));
        if (scenario == "ScrollRate") calendar.TrySetProperty("ScrollRate", new Vb6Value(3));
        if (scenario == "MonthRows")
        {
            calendar.TrySetProperty("MonthRows", new Vb6Value(3));
            calendar.TrySetProperty("MonthColumns", new Vb6Value(4));
        }
        if (scenario == "MultiSelect") calendar.TrySetProperty("MultiSelect", new Vb6Value(true));
        var window = new Window { Content = calendar, Width = 300, Height = 250 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var point = calendar.TranslatePoint(new Point(calendar.Bounds.Width - 5, 5), window)!.Value;
            Action press = () => window.MouseDown(point, MouseButton.Left);
            press.Should().Throw<NotImplementedException>();
            calendar.TryGetProperty("Value", out var after);
            after.Should().Be(new Vb6Value(VbDate.FromDateTime(date)));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_pending_input_fault_is_reported_by_the_runtime_window()
    {
        var calendar = new VBMonthView { Width = 180, Height = 158 };
        calendar.TrySetProperty("MultiSelect", new Vb6Value(true));
        var window = new VBFormRuntime { Content = calendar, Width = 300, Height = 250 };
        Exception? fault = null;
        void Report(VBWindowContext context, Exception error)
        {
            if (ReferenceEquals(context, window.Context)) fault = error;
        }
        VBWindowContext.Faulted += Report;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var point = calendar.TranslatePoint(new Point(calendar.Bounds.Width - 5, 5), window)!.Value;
            Action press = () => window.MouseDown(point, MouseButton.Left);
            press.Should().NotThrow();
            fault.Should().BeOfType<NotImplementedException>();
        }
        finally
        {
            VBWindowContext.Faulted -= Report;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Right_click_raises_mouse_events_then_Click_without_changing_the_selection()
    {
        var calendar = new VBMonthView { Width = 180, Height = 158 };
        VBProps.SetName(calendar, "MonthView1");
        var recorder = new ContainerRuntimeHarness.RecordingRoot { Child = calendar };
        var window = new Window { Content = recorder, Width = 300, Height = 250 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            calendar.Focus();
            recorder.Calls.Clear();
            calendar.TryGetProperty("Value", out var before);
            var point = calendar.TranslatePoint(new Point(50, 80), window)!.Value;
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            recorder.Calls.Select(c => c.Name).Should().Equal("MonthView1_MouseDown", "MonthView1_MouseUp", "MonthView1_Click");
            recorder.Calls.Take(2).Select(c => c.Args![0].Value).Should().Equal(2, 2);
            calendar.TryGetProperty("Value", out var after);
            after.Should().Be(before);
            calendar.TryGetProperty("SelStart", out var start);
            calendar.TryGetProperty("SelEnd", out var end);
            start.Should().Be(before);
            end.Should().Be(before);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Tab_enters_the_calendar_and_leaves_it_with_only_the_focus_events()
    {
        var calendar = new VBMonthView { Width = 180, Height = 158 };
        VBProps.SetName(calendar, "MonthView1");
        var first = new TextBox();
        var last = new TextBox();
        var recorder = new ContainerRuntimeHarness.RecordingRoot
        {
            Child = new StackPanel { Children = { first, calendar, last } },
        };
        var window = new Window { Content = recorder, Width = 300, Height = 300 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            first.Focus();
            recorder.Calls.Clear();
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            calendar.IsFocused.Should().BeTrue();
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            last.IsFocused.Should().BeTrue();
            recorder.Calls.Select(c => c.Name).Should().Equal("MonthView1_GotFocus", "MonthView1_LostFocus");
        }
        finally { window.Close(); }
    }
}
