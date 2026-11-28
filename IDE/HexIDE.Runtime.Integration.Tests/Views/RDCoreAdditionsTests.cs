using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Imaging;

namespace HexIDE.Runtime.Integration.Tests.Views;

/// <summary>
/// RDCore's additions to VB6, which a run takes when its project targets RDCore (#4967): PNG in <c>LoadPicture</c>, and an
/// Image's, Label's or PictureBox's <c>Opacity</c> (#4982). A VB6 project keeps VB6's behaviour. Every RDCore expectation was measured under RDC by the
/// rdcore session's own probe project, and every VB6 one under vb6.exe (the 481 is #4597's). Each test runs its program
/// through <see cref="VBLoader.RunForm"/>, as F5 does.
/// </summary>
public class RDCoreAdditionsTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    // A 20-by-10 red PNG of our own, generated with Pillow: Image.new("RGB", (20, 10), "red").save(stream, "PNG").
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAABQAAAAKCAIAAAA7N+mxAAAAFUlEQVR4nGP8z0A+YKJA76jmEaIZAHjzARO1LQhGAAAAAElFTkSuQmCC";

    private const string Designer = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           ClientHeight    =   3000
           ClientWidth     =   4000
           Begin VB.PictureBox Picture1
              Height          =   1000
              Left            =   120
              ScaleHeight     =   940
              ScaleWidth      =   1140
              TabIndex        =   0
              Top             =   1500
              Width           =   1200
           End
           Begin VB.Image imgArray
              Height          =   600
              Index           =   0
              Left            =   1500
              Opacity         =   0.5
              Top             =   120
              Width           =   1200
           End
           Begin VB.Image Image1
              Height          =   600
              Left            =   120
              Top             =   120
              Width           =   1200
           End
        End
        Attribute VB_Name = "Form1"
        """;

    // A solid 4-by-4 PNG in the colour of the RDC drawing probe, (220, 40, 40), generated with Pillow the same way.
    private const string RedPng = "iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAIAAAAmkwkpAAAAE0lEQVR4nGO8o6HBAANMcBZeDgA8UAE0eQqYiQAAAABJRU5ErkJggg==";

    // The RDC drawing probe's layout: a white form, and three 64-by-64 pixel Images with Stretch on.
    private const string DrawingDesigner = """
        VERSION 5.00
        Begin VB.Form Form1
           BackColor       =   &H00FFFFFF&
           Caption         =   "Form1"
           ClientHeight    =   1500
           ClientWidth     =   4000
           Begin VB.Image Image3
              Height          =   960
              Left            =   2640
              Stretch         =   -1  'True
              Top             =   240
              Width           =   960
           End
           Begin VB.Image Image2
              Height          =   960
              Left            =   1440
              Stretch         =   -1  'True
              Top             =   240
              Width           =   960
           End
           Begin VB.Image Image1
              Height          =   960
              Left            =   240
              Stretch         =   -1  'True
              Top             =   240
              Width           =   960
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private sealed class PngFile : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        public PngFile(string base64 = Png)
        {
            Directory.CreateDirectory(folder);
            // The extension says nothing: LoadPicture goes by content.
            FilePath = Path.Combine(folder, "picture.dat");
            File.WriteAllBytes(FilePath, Convert.FromBase64String(base64));
        }

        public string FilePath { get; }
        public string Literal => "\"" + FilePath.Replace("\"", "\"\"") + "\"";
        public void Dispose() => Directory.Delete(folder, true);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadPicture_reads_a_png_only_for_an_RDCore_target(bool rdcore)
    {
        using var png = new PngFile();
        var (printed, errors) = Run(rdcore ? ProjectTarget.RDCore : ProjectTarget.None, $"""
            Dim p As Object
            On Error Resume Next
            Set p = LoadPicture({png.Literal})
            Debug.Print Err.Number
            Debug.Print TypeName(p)
            If Err.Number = 0 Then
                Debug.Print p.Type
                Debug.Print p.Width
                Debug.Print p.Height
            End If
            """);

        errors.Should().BeEmpty();
        // VB6 refuses PNG with 481 (#4597). RDC loads it as a bitmap, Type 1, sized in HIMETRIC at 96 DPI (measured).
        printed.Should().Equal(rdcore
            ? ["0", "Picture", "1", "529", "265"]
            : ["481", "Nothing"]);
    }

    // RDC on a PNG cut short (measured headless on win-x64, #4967, issuecomment-7022278634): with its signature and a
    // whole IHDR, 33 bytes or more, it is a Type 1 picture of the size IHDR declares, with no error; cut into IHDR, or
    // the signature alone, it is 481.
    [AvaloniaTheory]
    [InlineData(40, "0", "Picture", "1", "529", "265")]
    [InlineData(33, "0", "Picture", "1", "529", "265")]
    [InlineData(20, "481", "Nothing")]
    [InlineData(8, "481", "Nothing")]
    public void An_RDCore_LoadPicture_of_a_png_cut_short_reads_it_by_its_header(int length, params string[] expected)
    {
        using var png = new PngFile(Convert.ToBase64String(Convert.FromBase64String(Png)[..length]));
        var (printed, errors) = Run(ProjectTarget.RDCore, $"""
            Dim p As Object
            On Error Resume Next
            Set p = LoadPicture({png.Literal})
            Debug.Print Err.Number
            Debug.Print TypeName(p)
            If Err.Number = 0 Then
                Debug.Print p.Type
                Debug.Print p.Width
                Debug.Print p.Height
            End If
            """);

        errors.Should().BeEmpty();
        printed.Should().Equal(expected);
    }

    [AvaloniaFact]
    public void An_RDCore_unstretched_Image_takes_its_pngs_size()
    {
        // RDC's 14-by-10 PNG row, measured by the rdcore session: #4610, issuecomment-7026728482 (cited on #5010).
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(14, 10));
        using var stream = new System.IO.MemoryStream();
        bitmap.Save(stream, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        using var png = new PngFile(Convert.ToBase64String(stream.ToArray()));
        var (printed, errors) = Run(ProjectTarget.RDCore, $"""
            Set Image1.Picture = LoadPicture({png.Literal})
            Debug.Print Image1.Width
            Debug.Print Image1.Height
            """, designer: Designer.Replace("Width           =   1200", "Width           =   330")
                .Replace("Height          =   600", "Height          =   330"));
        errors.Should().BeEmpty();
        printed.Should().Equal("210", "150");
    }

    [AvaloniaFact]
    public void An_RDCore_png_shows_in_an_Image_and_a_PictureBox_and_reads_back()
    {
        using var png = new PngFile();
        VBImage? image = null;
        VBPictureBox? box = null;
        var (printed, errors) = Run(ProjectTarget.RDCore, $"""
            Set Image1.Picture = LoadPicture({png.Literal})
            Set Picture1.Picture = LoadPicture({png.Literal})
            Debug.Print Image1.Picture.Width
            Debug.Print Picture1.Picture.Height
            Debug.Print Image1.Opacity
            """, inspect: window =>
            {
                image = Control<VBImage>(window, "Image1");
                box = Control<VBPictureBox>(window, "Picture1");
            });

        errors.Should().BeEmpty();
        // Loading a picture leaves Opacity at 1 (measured under RDC).
        printed.Should().Equal("529", "265", "1");
        image!.Picture.Should().Equal(Convert.FromBase64String(Png));
        box!.Picture.Should().BeOfType<Bitmap>().Which.PixelSize.Should().Be(new PixelSize(20, 10));
    }

    [AvaloniaFact]
    public void An_RDCore_Image_takes_Opacity_from_0_to_1_and_keeps_its_value_past_380()
    {
        VBImage? image = null;
        var (printed, errors) = Run(ProjectTarget.RDCore, """
            Debug.Print TypeName(Image1.Opacity)
            Debug.Print Image1.Opacity
            Image1.Opacity = "0.25"
            Debug.Print Image1.Opacity
            Image1.Opacity = 0.333333333333
            Debug.Print Image1.Opacity
            On Error Resume Next
            Image1.Opacity = 1.5
            Debug.Print Err.Number
            Debug.Print Image1.Opacity
            Err.Clear
            Image1.Opacity = -0.1
            Debug.Print Err.Number
            Err.Clear
            Image1.Opacity = 1.0000001
            Debug.Print Err.Number
            Debug.Print Image1.Opacity
            Err.Clear
            Image1.Opacity = 0
            Debug.Print Image1.Opacity
            Image1.Opacity = 1
            Debug.Print Image1.Opacity
            Dim late As Object
            Set late = Image1
            late.Opacity = 0.5
            Debug.Print late.Opacity
            """, inspect: window => image = Control<VBImage>(window, "Image1"));

        errors.Should().BeEmpty();
        // All measured under RDC: a Double, 1 when new, "0.25" coerced, 380 outside 0 to 1 with the old value kept.
        printed.Should().Equal("Double", "1", "0.25", "0.333333333333", "380", "0.333333333333", "380", "380",
            "0.333333333333", "0", "1", "0.5");
        // It is drawn as the Image's own opacity, which the next test checks against RDC's drawing.
        image!.Opacity.Should().Be(0.5);
    }

    // RDC's drawing, measured by the rdcore session in a shown RDC window on Linux (#4967, issuecomment-7021974640): a
    // solid (220, 40, 40) picture on a white form draws white at Opacity 0, (238, 148, 148) at 0.5 and itself at 1. That's
    // the picture blended over what is behind it with Opacity as its alpha.
    [AvaloniaFact]
    public void An_RDCore_Images_Opacity_blends_its_picture_over_the_form_as_RDC_draws_it()
    {
        using var png = new PngFile(RedPng);
        var drawn = new Color[3];
        var (_, errors) = Run(ProjectTarget.RDCore, $"""
            Dim p As StdPicture
            Set p = LoadPicture({png.Literal})
            Set Image1.Picture = p
            Set Image2.Picture = p
            Set Image3.Picture = p
            Image1.Opacity = 0
            Image2.Opacity = 0.5
            Image3.Opacity = 1
            """, designer: DrawingDesigner, inspect: window =>
            {
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
                using var buffer = frame.Lock();
                for (var i = 0; i < drawn.Length; i++)
                {
                    var image = Control<VBImage>(window, $"Image{i + 1}");
                    var centre = image.TranslatePoint(new Point(image.Bounds.Width / 2, image.Bounds.Height / 2), window)!.Value;
                    var offset = (int)(centre.Y * window.RenderScaling) * buffer.RowBytes + (int)(centre.X * window.RenderScaling) * 4;
                    byte At(int k) => Marshal.ReadByte(buffer.Address, offset + k);
                    drawn[i] = buffer.Format == Avalonia.Platform.PixelFormat.Bgra8888
                        ? Color.FromRgb(At(2), At(1), At(0)) : Color.FromRgb(At(0), At(1), At(2));
                }
            });

        errors.Should().BeEmpty();
        drawn.Should().Equal(Color.FromRgb(255, 255, 255), Color.FromRgb(238, 148, 148), Color.FromRgb(220, 40, 40));
    }

    [AvaloniaFact]
    public void An_RDCore_form_files_Opacity_is_read_and_a_Loaded_element_and_an_added_Image_have_theirs()
    {
        var (printed, errors) = Run(ProjectTarget.RDCore, """
            Debug.Print imgArray(0).Opacity
            Load imgArray(1)
            Debug.Print imgArray(1).Opacity
            Dim added As Object
            Set added = Controls.Add("VB.Image", "imgAdded")
            Debug.Print added.Opacity
            added.Opacity = 0.75
            Debug.Print added.Opacity
            """);

        errors.Should().BeEmpty();
        // The form file's 0.5 and Controls.Add's 1 are measured under RDC. A Load copies the form file's, as it does every
        // other property here.
        printed.Should().Equal("0.5", "0.5", "1", "0.75");
    }

    // RDC ignores an Image's form-file Opacity outside 0 to 1, with no error at load, and keeps 1 (measured headless on
    // win-x64, #4967, issuecomment-7022278634). A Load copies the 1.
    [AvaloniaTheory]
    [InlineData("2")]
    [InlineData("-0.5")]
    public void An_RDCore_form_files_Opacity_outside_0_to_1_is_ignored(string written)
    {
        var (printed, errors) = Run(ProjectTarget.RDCore, """
            Debug.Print imgArray(0).Opacity
            Load imgArray(1)
            Debug.Print imgArray(1).Opacity
            """, designer: Designer.Replace("Opacity         =   0.5", "Opacity         =   " + written));

        errors.Should().BeEmpty();
        printed.Should().Equal("1", "1");
    }

    [AvaloniaTheory]
    [InlineData("Debug.Print Image1.{0}")]
    [InlineData("Image1.{0} = 0.5")]
    public void A_VB6_Image_refuses_Opacity_as_it_does_a_member_it_lacks(string use)
    {
        var opacity = Run(ProjectTarget.None, string.Format(use, "Opacity") + Environment.NewLine + "Debug.Print \"after\"");
        var lacking = Run(ProjectTarget.None, string.Format(use, "NoSuchMember") + Environment.NewLine + "Debug.Print \"after\"");

        // Whatever a VB6 project does with a member an Image doesn't have, it does with Opacity: nothing is RDCore's here.
        opacity.Printed.Should().BeEmpty();
        lacking.Printed.Should().BeEmpty();
        Describe(opacity.Errors.Should().ContainSingle().Subject)
            .Should().Be(Describe(lacking.Errors.Should().ContainSingle().Subject));
    }

    // RDC measurements: #4982, issuecomment-7022244344 and -7024628433, RDCore 6fd4c80a.
    [AvaloniaTheory]
    [InlineData("Label")]
    [InlineData("PictureBox")]
    public void RDCore_Opacity_has_the_measured_type_range_and_error(string kind)
    {
        var (printed, errors) = Run(ProjectTarget.RDCore, """
            Debug.Print TypeName(Test1.Opacity)
            Debug.Print Test1.Opacity
            Test1.Opacity = 0.5
            Debug.Print Test1.Opacity
            Test1.Opacity = 0.75
            Debug.Print Test1.Opacity
            On Error Resume Next
            Test1.Opacity = 2
            Debug.Print Err.Number
            Debug.Print Err.Description
            Debug.Print Test1.Opacity
            Err.Clear
            Test1.Opacity = -1
            Debug.Print Err.Number
            Debug.Print Test1.Opacity
            """, designer: OpacityDesigner(kind));
        errors.Should().BeEmpty();
        printed.Should().Equal("Double", "1", "0.5", "0.75", "380", "Invalid property value", "0.75", "380", "0.75");
    }

    [AvaloniaTheory]
    [InlineData("Label", "0.5", "0.5")]
    [InlineData("Label", "2", "1")]
    [InlineData("Label", "-0.5", "1")]
    [InlineData("PictureBox", "0.5", "0.5")]
    [InlineData("PictureBox", "2", "1")]
    [InlineData("PictureBox", "-0.5", "1")]
    public void RDCore_Opacity_reads_valid_form_file_values_and_ignores_invalid_ones(string kind, string written, string expected)
    {
        var (printed, errors) = Run(ProjectTarget.RDCore, "Debug.Print Test1.Opacity", designer: OpacityDesigner(kind, written));
        errors.Should().BeEmpty();
        printed.Should().Equal(expected);
    }

    [AvaloniaTheory]
    [InlineData("Label")]
    [InlineData("PictureBox")]
    public void RDCore_Opacity_blends_an_opaque_background_as_RDC_draws_it(string kind)
    {
        var drawn = default(Color);
        var (_, errors) = Run(ProjectTarget.RDCore, "Test1.Opacity = 0.5",
            designer: OpacityDesigner(kind), inspect: window => drawn = CentrePixel(window));
        errors.Should().BeEmpty();
        drawn.Should().Be(Color.FromRgb(238, 148, 148));
    }

    [AvaloniaFact]
    public void RDCore_PictureBox_Visible_keeps_Opacity_and_restores_its_drawing()
    {
        var drawn = new List<Color>();
        var (printed, errors) = Run(ProjectTarget.RDCore, """
            Test1.Opacity = 0.5
            Debug.Print Test1.Opacity
            Test1.Visible = False
            Debug.Print Test1.Opacity
            Debug.Print Test1.Visible
            """, designer: OpacityDesigner("PictureBox"), activation: true, inspect: window =>
            {
                drawn.Add(CentrePixel(window));
                window.Context.ExecuteSub("ShowAgain");
                Pump();
                drawn.Add(CentrePixel(window));
            }, extraCode: """
            Private Sub ShowAgain()
                Test1.Visible = True
                Debug.Print Test1.Opacity
                Debug.Print Test1.Visible
            End Sub
            """);
        errors.Should().BeEmpty();
        printed.Should().Equal("0.5", "0.5", "False", "0.5", "True");
        drawn.Should().Equal(Colors.White, Color.FromRgb(238, 148, 148));
    }

    [AvaloniaFact]
    public void RDCore_PictureBox_CallByName_reaches_the_same_Opacity()
    {
        var (printed, errors) = Run(ProjectTarget.RDCore, """
            Debug.Print TypeName(CallByName(Test1, "Opacity", VbGet))
            Debug.Print CallByName(Test1, "Opacity", VbGet)
            Test1.Opacity = 0.5
            Debug.Print CallByName(Test1, "Opacity", VbGet)
            CallByName Test1, "Opacity", VbLet, 0.25
            Debug.Print Test1.Opacity
            On Error Resume Next
            CallByName Test1, "Opacity", VbLet, 2
            Debug.Print Err.Number
            Debug.Print Test1.Opacity
            Debug.Print CallByName(Test1, "Opacity", VbGet)
            Err.Clear
            CallByName Test1, "Opacity", VbLet, "0.75"
            Debug.Print Test1.Opacity
            CallByName Test1, "Opacity", VbSet, 0.5
            Debug.Print Err.Number
            Debug.Print Test1.Opacity
            Debug.Print CallByName(Test1, "opacity", VbGet)
            """, designer: OpacityDesigner("PictureBox"));
        errors.Should().BeEmpty();
        printed.Should().Equal("Double", "1", "0.5", "0.25", "380", "0.25", "0.25", "0.75", "438", "0.75", "0.75");
    }

    [AvaloniaTheory]
    [InlineData("Label", false)]
    [InlineData("PictureBox", false)]
    [InlineData("Label", true)]
    [InlineData("PictureBox", true)]
    public void Other_targets_refuse_Opacity_as_a_member_they_lack(string kind, bool vba)
    {
        foreach (var use in new[] { "Debug.Print Test1.{0}", "Test1.{0} = 0.5" })
        {
            var opacity = Run(ProjectTarget.None, string.Format(use, "Opacity"), designer: OpacityDesigner(kind, "0.5"), vba: vba);
            var lacking = Run(ProjectTarget.None, string.Format(use, "NoSuchMember"), designer: OpacityDesigner(kind), vba: vba);
            opacity.Printed.Should().BeEmpty();
            Describe(opacity.Errors.Should().ContainSingle().Subject)
                .Should().Be(Describe(lacking.Errors.Should().ContainSingle().Subject));
        }
        var (_, errors) = Run(ProjectTarget.None, "", designer: OpacityDesigner(kind, "0.5"), vba: vba,
            inspect: window => Control<Avalonia.Controls.Control>(window, "Test1").Opacity.Should().Be(1));
        errors.Should().BeEmpty();
    }

    private static string OpacityDesigner(string kind, string? opacity = null) => $$"""
        VERSION 5.00
        Begin VB.Form Form1
           BackColor       =   &H00FFFFFF&
           ClientHeight    =   1500
           ClientWidth     =   4000
           Begin VB.{{kind}} Test1
              BackColor       =   &H002828DC&
              BackStyle       =   1
              BorderStyle     =   0
              Height          =   960
              Left            =   240
              Top             =   240
              Width           =   960
              {{(opacity is null ? "" : "Opacity         =   " + opacity)}}
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static Color CentrePixel(VBFormRuntime window)
    {
        Pump();
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
        using var buffer = frame.Lock();
        var control = Control<Avalonia.Controls.Control>(window, "Test1");
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        var offset = (int)(centre.Y * window.RenderScaling) * buffer.RowBytes + (int)(centre.X * window.RenderScaling) * 4;
        byte At(int k) => Marshal.ReadByte(buffer.Address, offset + k);
        return buffer.Format == Avalonia.Platform.PixelFormat.Bgra8888
            ? Color.FromRgb(At(2), At(1), At(0)) : Color.FromRgb(At(0), At(1), At(2));
    }

    private static string Describe(Exception error) =>
        error.GetType().Name + (error is VBRunTimeException runTime ? " " + runTime.Error.ErrNo : "");

    [AvaloniaFact]
    public void A_VB6_form_files_Opacity_line_changes_nothing()
    {
        VBImage? arrayed = null;
        var (printed, errors) = Run(ProjectTarget.None, "Debug.Print imgArray(0).Width",
            inspect: window => arrayed = Control<VBImage>(window, "imgArray"));

        errors.Should().BeEmpty();
        printed.Should().Equal("1200");
        arrayed!.Opacity.Should().Be(1);
    }

    [AvaloniaFact]
    public void Png_is_decoded_only_where_a_caller_asks()
    {
        var bytes = Convert.FromBase64String(Png);

        FrxImageHelper.DetectKind(bytes).Should().Be(FrxImageHelper.FrxBlobKind.Png);
        // The designer's own Load Picture refuses PNG, as VB6's does: it asks without png.
        FrxImageHelper.TryDecodeToAvaloniaBitmap(bytes).Should().BeNull();
        using (var decoded = FrxImageHelper.TryDecodeToAvaloniaBitmap(bytes, png: true))
            decoded!.PixelSize.Should().Be(new PixelSize(20, 10));
        // A Panel's picture and VB6's LoadPicture still refuse it with 481.
        var vb6 = () => PanelPicture.FromFile(bytes, loadPicture: true);
        vb6.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(481);
        var panel = () => PanelPicture.FromFile(bytes);
        panel.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(481);
    }

    private static T Control<T>(VBFormRuntime window, string name) where T : Avalonia.Controls.Control =>
        window.GetLogicalDescendants().OfType<T>().Single(control => VBProps.GetName(control) == name);

    /// <summary>Run <paramref name="load"/> as Form_Load (or Form_Activate) in the selected project.</summary>
    private static (List<string> Printed, List<Exception> Errors) Run(ProjectTarget target, string load,
        Action<VBFormRuntime>? inspect = null, string designer = Designer, bool activation = false,
        string extraCode = "", bool vba = false)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P") { Target = target };
        if (vba) project.VbaBitness = VbaBitness.Bit64;
        var code = "\r\nPrivate Sub Form_" + (activation ? "Activate" : "Load") + "()\r\n" + load + "\r\nEnd Sub\r\n" + extraCode;
        var form1 = new FormDeserializer().Deserialize(project, (designer + code).ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form1);

        var printed = new List<string>();
        var errors = new List<Exception>();
        void Print(string line) => printed.Add(line.Trim());
        void OnFaulted(VBWindowContext _, Exception e) => errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => errors.Add(e);
        void OnCompileError(VBWindowContext _, VBCompileErrorException e) => errors.Add(e);
        VBDebugConsole.Output += Print;
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        VBWindowContext.CompileError += OnCompileError;
        var cancel = new CancellationTokenSource();
        try
        {
            _ = VBLoader.RunForm(form1, cancel.Token, out var window);
            Pump();
            inspect?.Invoke(window);
        }
        finally
        {
            cancel.Cancel();
            Pump();
            VBDebugConsole.Output -= Print;
            VBWindowContext.Faulted -= OnFaulted;
            VBWindowContext.RunTimeError -= OnRunTimeError;
            VBWindowContext.CompileError -= OnCompileError;
        }
        return (printed, errors);
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }
}
