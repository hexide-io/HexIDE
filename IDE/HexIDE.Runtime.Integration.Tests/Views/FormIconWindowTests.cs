using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Threading;
using HexIDE.Runtime.Interpreter;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

/// <summary>
/// The icon a running form's window is made with (#1594): the form's own, none for (None), and the runner's default for a
/// form with no Icon line. What Windows is told and draws, big icon and small, was checked live against vb6.exe's
/// windows; here, the icon each window is given.
/// </summary>
public class FormIconWindowTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    // A one-pixel icon: the ICONDIR, its one entry, then a 32-bit DIB with its AND mask.
    private static readonly byte[] OnePixelIcon =
    [
        0, 0, 1, 0, 1, 0,
        1, 1, 0, 0, 1, 0, 32, 0, 48, 0, 0, 0, 22, 0, 0, 0,
        40, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 1, 0, 32, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 255, 255,
        0, 0, 0, 0,
    ];

    private static byte[] Record(byte[] image) =>
        [.. BitConverter.GetBytes(image.Length + 8), 0x6C, 0x74, 0, 0, .. BitConverter.GetBytes(image.Length), .. image];

    private static Window Window(byte[]? icon)
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   ClientHeight    =   1500\r\n   ClientWidth     =   2520\r\nEnd\r\n"
            + "Attribute VB_Name = \"Form1\"\r\n", new NullSink())!;
        var root = form.Components.First(c => c.BaseClass is FormComponentClass);
        if (icon is not null)
            root.SetProperty(VBProperties.IconProperty, icon);
        return FormComponentClass.Instance.InstantiateWindow(root);
    }

    [AvaloniaFact]
    public void A_form_with_its_own_Icon_runs_with_it() =>
        Window(Record(OnePixelIcon)).Icon.Should().NotBeNull();

    [AvaloniaFact]
    public void A_form_whose_Icon_is_None_takes_no_default()
    {
        var runner = new WindowIcon(new MemoryStream(OnePixelIcon));
        VBLoader.DefaultFormIcon = runner;
        try
        {
            Window(Record([])).Icon.Should().BeNull("(None) is no icon at all, not the runner's");
        }
        finally
        {
            VBLoader.DefaultFormIcon = null;
        }
    }

    [AvaloniaFact]
    public void A_form_with_no_Icon_line_runs_with_the_runners_default()
    {
        var runner = new WindowIcon(new MemoryStream(OnePixelIcon));
        VBLoader.DefaultFormIcon = runner;
        try
        {
            Window(null).Icon.Should().BeSameAs(runner);
        }
        finally
        {
            VBLoader.DefaultFormIcon = null;
        }
    }

    [AvaloniaFact]
    public void Without_a_runners_default_a_form_with_no_Icon_line_keeps_Avalonias() =>
        Window(null).Icon.Should().BeNull("Avalonia then gives it the executable's icon, the IDE's on F5");

    // VB6 SP6, native and p-code: https://github.com/hexide-io/HexIDE/issues/4603 (latest measuring comment; PR #4613).
    [AvaloniaTheory]
    [InlineData("Set ", "Me.Icon")]
    [InlineData("", "Me.Icon")]
    [InlineData("Set ", "Icon")]
    [InlineData("", "Icon")]
    [InlineData("Set ", "Form2.Icon")]
    [InlineData("", "Form2.Icon")]
    public void Runtime_Icon_keeps_the_picture_reference_with_Set_or_Let(string assignment, string target)
    {
        var run = RunIcon($"""
            Dim p As Picture
            Set p = LoadPicture("$ICON$")
            {assignment}{target} = p
            Debug.Print ({target} Is p) & "|" & {target}.Type
            """);
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("True|3");
    }

    [AvaloniaTheory]
    [InlineData("Me.Icon")]
    [InlineData("Form2.Icon")]
    public void Runtime_Icon_rejects_a_bitmap_with_trappable_380_and_keeps_its_picture(string target)
    {
        var run = RunIcon($"""
            Dim p As Picture
            Set p = LoadPicture("$ICON$")
            Set {target} = p
            On Error Resume Next
            Set {target} = LoadPicture("$BITMAP$")
            Debug.Print Err.Number & "|" & Err.Description
            Debug.Print ({target} Is p) & "|" & {target}.Type
            """);
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("380|Invalid property value", "True|3");
    }

    [AvaloniaTheory]
    [InlineData("Set ", "Me.Icon")]
    [InlineData("", "Me.Icon")]
    [InlineData("Set ", "Form2.Icon")]
    [InlineData("", "Form2.Icon")]
    public void Runtime_Nothing_becomes_an_empty_Picture(string assignment, string target)
    {
        var run = RunIcon($"""
            Set {target} = LoadPicture("$ICON$")
            {assignment}{target} = Nothing
            Debug.Print TypeName({target}) & "|" & {target}.Type & "|" & ({target} Is Nothing)
            """);
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Picture|0|False");
    }

    [AvaloniaTheory]
    [InlineData(2, "default")]
    [InlineData(2, "none")]
    [InlineData(2, "own")]
    [InlineData(3, "default")]
    [InlineData(3, "none")]
    [InlineData(3, "own")]
    public void Runtime_window_follows_icons_and_Nothing_but_empty_pictures_leave_it_alone(int border, string initial)
    {
        var run = RunIcon("""
            Debug.Print "before"
            Set Form2.Icon = LoadPicture("$ICON$")
            Debug.Print "icon"
            Dim p As Picture
            Set p = LoadPicture()
            Set Form2.Icon = p
            Debug.Print (Form2.Icon Is p) & "|" & Form2.Icon.Type
            Set Form2.Icon = Nothing
            Debug.Print "nothing"
            Set Form2.Icon = LoadPicture()
            Debug.Print "empty"
            Set Form2.Icon = LoadPicture("$ICON$")
            Debug.Print "again"
            """, border, initial);
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("before", "icon", "True|0", "nothing", "empty", "again");
        run.Icons[1].Should().NotBeNull().And.NotBeSameAs(run.Icons[0]);
        run.Icons[2].Should().BeSameAs(run.Icons[1]);
        run.Icons[3].Should().BeNull("a saved (None) form has no Window.Icon, for both border styles");
        run.Icons[4].Should().BeNull();
        run.Icons[5].Should().NotBeNull();
    }

    [AvaloniaTheory]
    [InlineData("Me.Icon")]
    [InlineData("Form2.Icon")]
    public void Runtime_read_before_assignment_returns_an_icon_picture(string target)
    {
        // vb6.exe SP6 native/p-code, #3924, 2026-11-20: Set from the form's Icon gives Picture.Type 3.
        var run = RunIcon($"Dim p As Picture\nSet p = {target}\nDebug.Print p.Type");
        run.Errors.Should().BeEmpty();
        run.Printed.Select(value => value.Trim()).Should().Equal("3");
    }

    private sealed record IconRun(List<string> Printed, List<Exception> Errors, List<WindowIcon?> Icons);

    private static IconRun RunIcon(string body, int border = 2, string initial = "default")
    {
        var folder = Path.Combine(Path.GetTempPath(), "hexide-icon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var iconPath = Path.Combine(folder, "test.ico");
        var bitmapPath = Path.Combine(folder, "test.bmp");
        File.WriteAllBytes(iconPath, OnePixelIcon);
        // A 32-by-32, 24-bit BMP, the bitmap dimensions measured in #4603.
        using (var writer = new BinaryWriter(File.Create(bitmapPath)))
        {
            writer.Write((ushort)0x4d42); writer.Write(54 + 32 * 32 * 3); writer.Write(0); writer.Write(54);
            writer.Write(40); writer.Write(32); writer.Write(32); writer.Write((ushort)1); writer.Write((ushort)24);
            writer.Write(new byte[24 + 32 * 32 * 3]);
        }
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        FormDefinition AddForm(string name, string code)
        {
            var form = new FormDeserializer().Deserialize(project,
                ($"VERSION 5.00\nBegin VB.Form {name}\n   Caption = \"{name}\"\n   BorderStyle = {border}\n   ClientWidth = 3000\n   ClientHeight = 1500\nEnd\n"
                + $"Attribute VB_Name = \"{name}\"\n" + code).ReplaceLineEndings("\r\n"), new NullSink())!;
            if (initial != "default")
                form.Components.First(c => c.BaseClass is FormComponentClass)
                    .SetProperty(VBProperties.IconProperty, Record(initial == "own" ? OnePixelIcon : []));
            project.AddForm(form);
            return form;
        }
        AddForm("Form2", "");
        var form1 = AddForm("Form1", "Private Sub Form_Load()\nForm2.Show\n"
            + body.Replace("$ICON$", iconPath).Replace("$BITMAP$", bitmapPath) + "\nEnd Sub\n");
        var result = new IconRun([], [], []);
        var windows = new List<Window>();
        using var opening = Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
        void Printed(string text)
        {
            result.Printed.Add(text);
            result.Icons.Add(windows.Last(w => w.Title == "Form2").Icon);
        }
        void Faulted(VBWindowContext _, Exception e) => result.Errors.Add(e);
        void RuntimeError(VBWindowContext _, VBRunTimeException e) => result.Errors.Add(e);
        void CompileError(VBWindowContext _, VBCompileErrorException e) => result.Errors.Add(e);
        VBDebugConsole.Output += Printed;
        VBWindowContext.Faulted += Faulted;
        VBWindowContext.RunTimeError += RuntimeError;
        VBWindowContext.CompileError += CompileError;
        Window? root = null;
        try
        {
            VBLoader.RunForm(form1, CancellationToken.None, out var window);
            root = window;
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            foreach (var window in windows.AsEnumerable().Reverse()) window.Close();
            root?.Close();
            VBDebugConsole.Output -= Printed;
            VBWindowContext.Faulted -= Faulted;
            VBWindowContext.RunTimeError -= RuntimeError;
            VBWindowContext.CompileError -= CompileError;
            Directory.Delete(folder, true);
        }
        return result;
    }

}
