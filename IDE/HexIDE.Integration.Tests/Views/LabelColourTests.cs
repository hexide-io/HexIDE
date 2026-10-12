using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A Label's colours paint when a program sets them, and a Label's or TextBox's colours from the form read back as
/// VB6's Long (#795, #789). Each expectation is <c>vb6.exe</c> output, recorded under "A Label's colours" in
/// <c>docs/vb6-fidelity-oracle.md</c>: read-backs logged by the program, and what painted read with
/// <c>GetPixel</c> in an interactive session. Here the painting is read from a real Skia frame of the hosted form.
/// </summary>
public class LabelColourTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           BackColor       =   &H00C0FFFF&
           Caption         =   "Form1"
           Begin VB.TextBox Text2
              Left            =   4080
              Top             =   960
              Width           =   1200
              Height          =   375
           End
           Begin VB.TextBox Text1
              BackColor       =   &H0080FFFF&
              Left            =   4080
              Top             =   120
              Width           =   1200
              Height          =   375
           End
           Begin VB.Label Arr
              Height          =   600
              Index           =   1
              Left            =   2760
              Top             =   960
              Width           =   1200
           End
           Begin VB.Label Arr
              Height          =   600
              Index           =   0
              Left            =   1440
              Top             =   960
              Width           =   1200
           End
           Begin VB.Label LblPlain
              Height          =   600
              Left            =   120
              Top             =   960
              Width           =   1200
           End
           Begin VB.Label LblTrans
              BackColor       =   &H000000FF&
              BackStyle       =   0  'Transparent
              Height          =   600
              Left            =   1440
              Top             =   120
              Width           =   1200
           End
           Begin VB.Label Label1
              BackColor       =   &H000000FF&
              Height          =   600
              Left            =   120
              Top             =   120
              Width           =   1200
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static readonly Color Red = Color.FromRgb(255, 0, 0);
    private static readonly Color Green = Color.FromRgb(0, 255, 0);
    private static readonly Color Blue = Color.FromRgb(0, 0, 255);

    private static async Task<List<Vb6Value>> Run(string code)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        return await ContainerRuntimeHarness.Run(ctx, env, code + "\r\n");
    }

    /// <summary>The colour drawn just inside a control's bottom-left corner, clear of any caption and border.</summary>
    private static Color PaintedNearBottomLeft(Window window, Control control) =>
        PaintedAt(window, control.TranslatePoint(new Point(6, control.Bounds.Height - 6), window)
                          ?? throw new InvalidOperationException("the control is not in the window"));

    /// <summary>The colour drawn at <paramref name="point"/> of a real Skia frame of the window.</summary>
    private static Color PaintedAt(Window window, Point point)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame was rendered");
        using var buffer = frame.Lock();
        var offset = (int)point.Y * buffer.RowBytes + (int)point.X * 4;
        byte At(int i) => Marshal.ReadByte(buffer.Address, offset + i);
        return buffer.Format == Avalonia.Platform.PixelFormat.Bgra8888
            ? Color.FromRgb(At(2), At(1), At(0))
            : Color.FromRgb(At(0), At(1), At(2));
    }

    /// <summary>The form's own surface, beside the controls: what a transparent label lets through.</summary>
    private static Color FormSurface(Window window, Canvas canvas) =>
        PaintedAt(window, canvas.TranslatePoint(new Point(2, canvas.Bounds.Height - 2), window)
                          ?? throw new InvalidOperationException("the form is not in the window"));

    private static VBLabel Label(Canvas canvas, string name, int? index = null)
    {
        foreach (var child in canvas.Children)
            if (child is VBLabel label && ContainerRuntimeHarness.NameOf(label) == name
                && (index is null || VBProps.GetIndex(label) == index))
                return label;
        throw new InvalidOperationException($"no label {name}({index})");
    }

    // ── read back as the Long VB6 reads ───────────────────────────────────────────────────────────

    [AvaloniaTheory]
    [InlineData("Label1.BackColor", 255L)]                       // from the form
    [InlineData("LblPlain.BackColor", -2147483633L)]             // the default, &H8000000F
    [InlineData("LblPlain.ForeColor", -2147483630L)]             // &H80000012
    [InlineData("Text1.BackColor", 0x80FFFFL)]                   // from the form
    [InlineData("Text2.BackColor", -2147483643L)]                // a text box's default, &H80000005
    [InlineData("Text2.ForeColor", -2147483640L)]                // &H80000008
    public async Task A_colour_from_the_form_reads_back_as_its_Long(string property, long expected)
    {
        var debug = await Run($"Debug.Print {property}");

        debug[0].Should().Be(new Vb6Value(expected));
    }

    [AvaloniaFact]
    public async Task A_colour_compares_and_adds_as_the_number_it_is()
    {
        // Measured: `Text1.BackColor = vbRed` is True and `BackColor + 1` is 256 after BackColor = &HFF.
        var debug = await Run("Debug.Print (Label1.BackColor = vbRed)\r\nDebug.Print Label1.BackColor + 1");

        debug[0].Value.Should().Be(true);
        debug[1].Should().Be(new Vb6Value(256L));
    }

    // ── what paints ───────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task A_Label_paints_its_form_colour_and_a_transparent_one_shows_the_form()
    {
        var (canvas, window, _, _) = ContainerRuntimeHarness.Laid(Form);

        PaintedNearBottomLeft(window, Label(canvas, "Label1")).Should().Be(Red);
        PaintedNearBottomLeft(window, Label(canvas, "LblTrans")).Should().Be(FormSurface(window, canvas),
            "a transparent label paints nothing of its own, red BackColor or not");
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task A_BackColor_set_at_run_time_paints_and_reads_back()
    {
        var (canvas, window, ctx, env) = ContainerRuntimeHarness.Laid(Form);

        var debug = await ContainerRuntimeHarness.Run(ctx, env, "Label1.BackColor = vbBlue\r\nDebug.Print Label1.BackColor\r\n");

        debug[0].Should().Be(new Vb6Value(16711680L));
        PaintedNearBottomLeft(window, Label(canvas, "Label1")).Should().Be(Blue,
            "VBLabel paints its own BackColor, which the assignment used to miss for the attached one");
    }

    [AvaloniaFact]
    public async Task A_control_array_element_paints_alone()
    {
        // The Battleship grid's case: labels in a control array, each toggled by index.
        var (canvas, window, ctx, env) = ContainerRuntimeHarness.Laid(Form);

        var debug = await ContainerRuntimeHarness.Run(ctx, env,
            "Arr(1).BackColor = vbGreen\r\nDebug.Print Arr(1).BackColor\r\nDebug.Print Arr(0).BackColor\r\n");

        debug[0].Should().Be(new Vb6Value(65280L));
        debug[1].Should().Be(new Vb6Value(-2147483633L));
        PaintedNearBottomLeft(window, Label(canvas, "Arr", 1)).Should().Be(Green);
        PaintedNearBottomLeft(window, Label(canvas, "Arr", 0)).Should().NotBe(Green);
    }

    [AvaloniaFact]
    public async Task A_transparent_Label_keeps_its_BackColor_when_one_is_set_at_run_time()
    {
        // Measured: VB6 drops the assignment without an error, so the old colour reads back, and is the one shown
        // if the label is later made opaque.
        var (canvas, window, ctx, env) = ContainerRuntimeHarness.Laid(Form);

        var debug = await ContainerRuntimeHarness.Run(ctx, env, "LblTrans.BackColor = vbBlue\r\nDebug.Print LblTrans.BackColor\r\n");

        debug[0].Should().Be(new Vb6Value(255L));
        Label(canvas, "LblTrans").BackColor.Should().Be(VBColor.FromOle(255));
        PaintedNearBottomLeft(window, Label(canvas, "LblTrans")).Should().Be(FormSurface(window, canvas));
    }

    [AvaloniaFact]
    public async Task A_ForeColor_set_at_run_time_reaches_the_text()
    {
        var (canvas, _, ctx, env) = ContainerRuntimeHarness.Laid(Form);

        await ContainerRuntimeHarness.Run(ctx, env, "LblPlain.ForeColor = vbWhite\r\n");

        var label = Label(canvas, "LblPlain");
        label.Foreground.Should().BeAssignableTo<ISolidColorBrush>().Which.Color.Should().Be(Colors.White);
    }

    [AvaloniaFact]
    public async Task A_TextBox_paints_its_form_colour()
    {
        var (canvas, window, _, _) = ContainerRuntimeHarness.Laid(Form);

        var text1 = ContainerRuntimeHarness.Child(canvas, "Text1");
        PaintedNearBottomLeft(window, text1).Should().Be(Color.FromRgb(255, 255, 128));   // &H0080FFFF&
        await Task.CompletedTask;
    }
}
