using System.IO;
using System.Threading.Tasks;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.Components;
using Avalonia;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// Measurements: https://github.com/hexide-io/HexIDE/issues/4849 and
// docs/vb6-fidelity-oracle/in-box-controls-the-imagelist.md, Drawing.
public class ListImageDrawTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption = "Form1"
           ClientHeight = 3195
           ClientWidth = 4680
           Begin MSComctlLib.ImageList Images
              Left = 0
              Top = 0
           End
           Begin VB.PictureBox Picture1
              Left = 2160
              Top = 1200
              Width = 2415
              Height = 1815
           End
        End
        Attribute VB_Name = "Form1"

        """;

    private static void Check(string code, params string[] expected)
    {
        var run = RunLoad(Form, code);
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal(expected);
    }

    private sealed record Run(List<string> Printed, List<Exception> Errors);

    /// <summary>Run Form1 as F5 does, with <paramref name="load"/> as its Load, then close it.</summary>
    private static Run RunLoad(string frm, string load, Action<Window>? inspect = null, string procedures = "")
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var code = "Private Sub Form_Load()\r\n" + load.ReplaceLineEndings("\r\n") + "\r\nEnd Sub\r\n"
            + "Sub S(ByVal what As String, ByVal v As Variant)\r\n    Debug.Print what & \" = \" & v & \" \" & TypeName(v)\r\nEnd Sub\r\n"
            + procedures.ReplaceLineEndings("\r\n");
        var form = new FormDeserializer().Deserialize(project, frm + code, new NullSink())!;
        project.AddForm(form);

        var run = new Run([], []);
        void OnFaulted(VBWindowContext _, Exception e) => run.Errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => run.Errors.Add(e);
        void OnCompileError(VBWindowContext _, VBCompileErrorException e) => run.Errors.Add(e);
        VBDebugConsole.Output += run.Printed.Add;
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        VBWindowContext.CompileError += OnCompileError;
        try
        {
            VBLoader.RunForm(form, CancellationToken.None, out var window);
            try
            {
                Dispatcher.UIThread.RunJobs();
                inspect?.Invoke(window);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            VBDebugConsole.Output -= run.Printed.Add;
            VBWindowContext.Faulted -= OnFaulted;
            VBWindowContext.RunTimeError -= OnRunTimeError;
            VBWindowContext.CompileError -= OnCompileError;
        }
        return run;
    }


    [AvaloniaTheory]
    [InlineData("Me.")]
    [InlineData("Picture1.")]
    public void Handles_follow_the_measured_lifetime(string target) =>
        Check($"""
            Dim h As Long, a As Long, b As Long
            h = {target}hDC
            Debug.Print TypeName({target}hDC)
            Debug.Print h <> 0
            Debug.Print h = {target}hDC
            Debug.Print Me.hDC <> Picture1.hDC
            {target}AutoRedraw = True
            a = {target}hDC
            Debug.Print a <> h
            Debug.Print a = {target}hDC
            {target}Cls
            b = {target}hDC
            Debug.Print b <> a
            {target}Width = {target}Width + 300
            Debug.Print b <> {target}hDC
            {target}AutoRedraw = False
            Debug.Print h = {target}hDC
            {target}AutoRedraw = True
            Debug.Print b <> {target}hDC
            """, "Long", "True", "True", "True", "True", "True", "True", "True", "True", "True");

    [AvaloniaFact]
    public void Never_shown_form_keeps_window_handle_through_show_hide()
    {
        long shownHandle = 0;
        var run = RunLoad(Form, "Debug.Print CStr(hDC)", window =>
        {
            // Load ran before Show; the form's module slot survives showing and hiding.
            RuntimeHandle(window).Should().NotBe(0);
            var h = shownHandle = RuntimeHandle(window);
            window.Hide();
            RuntimeHandle(window).Should().Be(h);
            window.Show();
            RuntimeHandle(window).Should().Be(h);
        });
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal(shownHandle.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static long RuntimeHandle(Control target)
    {
        HexIDE.Runtime.AvaloniaInterop.RuntimeDrawing.TryGet(target, "hDC", out var value).Should().BeTrue();
        value.Type.Should().Be(Vb6Value.ValueType.Long);
        return Convert.ToInt64(value.Value);
    }

    private static ImageListPictureObject Picture(int size = 16)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0x4d42);
        writer.Write(54 + size * size * 3);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(size);
        writer.Write(size);
        writer.Write((ushort)1);
        writer.Write((ushort)24);
        for (var i = 0; i < 6; i++) writer.Write(0);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            writer.Write((byte)(x < size / 2 ? 255 : 0));
            writer.Write((byte)0);
            writer.Write((byte)255);
        }
        var payload = stream.ToArray();
        using var record = new MemoryStream();
        using var framing = new BinaryWriter(record);
        framing.Write(payload.Length + 24);
        framing.Write(new Guid("0BE35204-8F91-11CE-9DE3-00AA004BB851").ToByteArray());
        framing.Write(0x746c);
        framing.Write(payload.Length);
        framing.Write(payload);
        return new ImageListPictureObject(record.ToArray());
    }

    private static async Task<List<Vb6Value>> Draw(string code, int size = 16, int pictureSize = 16)
    {
        var (_, _, context, environment) = ContainerRuntimeHarness.Spawn(Form);
        context.TryGetVariable(environment, "Images", out var list).Should().BeTrue();
        var images = (VBImageList)list.Value!;
        await images.Images.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new Vb6Value(Picture(pictureSize))]);
        images.ImageWidth = images.ImageHeight = size;
        images.TrySetProperty("MaskColor", new Vb6Value(0xff00ffL));
        return await ContainerRuntimeHarness.Run(context, environment,
            "Picture1.AutoRedraw = True\nPicture1.BackColor = vbYellow\nPicture1.Cls\n" + code);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Styles_draw_mask_and_image_pixels(int style)
    {
        var output = await Draw($"""
            Picture1.PSet (0, 0), vbBlue
            images.ListImages(1).Draw Picture1.hDC, 0, 0, {style}
            Debug.Print Picture1.Point(0, 0)
            Debug.Print Picture1.Point(120, 0)
            Debug.Print Picture1.Point(135, 0)
            """);
        // HexIDE's runtime palette resolves Window to white and Highlight to navy (the guest used &HD77800).
        output.Select(v => Convert.ToInt64(v.Value)).Should().Equal(
            style == 1 ? 0xff0000 : 0xffffff,
            style >= 2 ? 0x800000 : 255, 255);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Coordinates_are_twips_even_in_pixel_scale_and_parity_is_image_relative(int scale)
    {
        var output = await Draw($"""
            Picture1.ScaleMode = {scale}
            images.ListImages(1).Draw Picture1.hDC, 15, 15, 2
            Picture1.ScaleMode = 3
            Debug.Print Picture1.Point(9, 1)
            Debug.Print Picture1.Point(10, 1)
            Debug.Print Picture1.Point(11, 1)
            Debug.Print Picture1.Point(1, 1)
            Debug.Print Picture1.Point(9, 0)
            """);
        output.Select(v => Convert.ToInt64(v.Value)).Should().Equal(0x800000, 255, 0x800000, 0xffffff, 0xffff);
    }

    [AvaloniaTheory]
    [InlineData(16, 32)]
    [InlineData(32, 16)]
    public async Task Defaults_and_list_size_and_current_mask_are_used(int pictureSize, int size)
    {
        var output = await Draw($"""
            images.ListImages(1).Draw Picture1.hDC
            Debug.Print Picture1.Point({(size - 1) * 15}, {(size - 1) * 15})
            Debug.Print Picture1.Point({size * 15}, {size * 15})
            images.BackColor = vbBlue
            images.ListImages(1).Draw Picture1.hDC
            Debug.Print Picture1.Point(0, 0)
            images.UseMaskColor = False
            images.ListImages(1).Draw Picture1.hDC
            Debug.Print Picture1.Point(0, 0)
            images.UseMaskColor = True
            images.MaskColor = vbBlue
            images.ListImages(1).Draw Picture1.hDC
            Debug.Print Picture1.Point(0, 0)
            """, size, pictureSize);
        output.Select(v => Convert.ToInt64(v.Value)).Should().Equal(255, 0xffff, 0xff0000, 0xff00ff, 0xff00ff);
    }

    [AvaloniaTheory]
    [InlineData("h = 0")]
    [InlineData("h = Picture1.hDC\nPicture1.Cls")]
    [InlineData("h = Picture1.hDC\nPicture1.Width = Picture1.Width + 300")]
    [InlineData("h = Picture1.hDC\nPicture1.AutoRedraw = False")]
    public async Task Zero_and_stale_handles_are_noops(string prepare)
    {
        var output = await Draw(prepare + """
            
            images.ListImages(1).Draw h
            Debug.Print Picture1.Point(120, 0)
            """);
        output.Select(v => Convert.ToInt64(v.Value)).Should().Equal(0xffff);
    }

    [AvaloniaTheory]
    [InlineData("images.ListImages(1).Draw 12345", 4849)]
    [InlineData("images.ListImages(1).Draw Picture1.hDC, 0, 0, 4", 4915)]
    [InlineData("images.ListImages(1).Draw Picture1.hDC, 0.5", 4915)]
    [InlineData("images.ListImages(1).Draw \"12345\"", 4915)]
    [InlineData("Picture1.hDC = 5", 4915)]
    public async Task Unknown_handles_and_unmeasured_members_refuse(string statement, int expectedIssue)
    {
        Func<Task> draw = () => Draw(statement);
        await draw.Should().ThrowAsync<NotImplementedException>().WithMessage($"*#{expectedIssue}*");
    }

    [AvaloniaFact]
    public async Task Window_handle_draws_through_the_same_surface()
    {
        var output = await Draw("""
            Picture1.AutoRedraw = False
            images.ListImages(1).Draw Picture1.hDC
            Debug.Print Picture1.Point(120, 0)
            """);
        output.Select(v => Convert.ToInt64(v.Value)).Should().Equal(255);
    }
}
