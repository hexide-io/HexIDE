using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using HexIDE.Integration.Tests;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Runtime.Integration.Tests.Views;

// #922; docs/vb6-fidelity-oracle/autosize-on-a-label-and-a-picturebox.md and measuring PR #4984.
public class LabelAutoSizeTests : WindowTestFixture
{
    private static string Form(string properties = "", string family = "MS Sans Serif", double size = 8.25, int weight = 400) => $$"""
        VERSION 5.00
        Begin VB.Form Form1
           ScaleMode = 1
           Begin VB.Label Label1
              AutoSize = -1
              Caption = "Hello"
              Left = 600
              Top = 1500
              Width = 3000
              Height = 1000
              BeginProperty Font
                 Name = "{{family}}"
                 Size = {{size.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
                 Weight = {{weight}}
                 Charset = 0
                 Italic = 0
              EndProperty
              {{properties}}
           End
        End
        Attribute VB_Name = "Form1"
        """;

    [AvaloniaTheory]
    [InlineData("MS Sans Serif", 8.25, 400)]
    [InlineData("Arial", 9.75, 400)]
    [InlineData("MS Sans Serif", 18, 400)]
    [InlineData("Tahoma", 8.25, 400)]
    [InlineData("MS Sans Serif", 8.25, 700)]
    public void Measured_caption_fixture_loads_requested_font(string family, double size, int weight)
    {
        var (_, canvas, _, _) = ContainerRuntimeHarness.Spawn(Form(family: family, size: size, weight: weight));
        var font = AttachedProperties.GetFont(canvas.Children.OfType<VBLabel>().Single());
        font.Should().NotBeNull();
        font!.Value.FontFamilyName.Should().Be(family);
        font.Value.Size.Should().Be(size);
        font.Value.Weight.Should().Be(weight);
    }

    // Preserve HexIDE's existing behaviour: identical font assignments have not been measured in VB6.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Identical_font_assignment_keeps_saved_size(bool shared)
    {
        var (canvas, _, ctx, env) = ContainerRuntimeHarness.Laid(Form());
        var label = canvas.Children.OfType<VBLabel>().Single();
        var prefix = shared ? "Dim f As Object\r\nSet f = Label1.Font\r\n" : "";
        await ContainerRuntimeHarness.Run(ctx, env, prefix + "Label1.FontSize = 8.25");
        label.Width.Should().Be(200);
        label.Height.Should().BeApproximately(1000.0 / 15, .001);
    }

    [AvaloniaFact]
    public async Task Unmeasured_line_break_only_caption_refuses()
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form());
        Func<Task> run = () => ContainerRuntimeHarness.Run(ctx, env, "Label1.Caption = vbLf");
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*only these line breaks*#922*");
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.5)]
    public async Task Saved_size_and_identical_caption_hold(double scaling)
    {
        var (canvas, window, ctx, env) = ContainerRuntimeHarness.Laid(Form());
        window.SetRenderScaling(scaling);
        var label = canvas.Children.OfType<VBLabel>().Single();
        label.Width.Should().Be(200);
        label.Height.Should().BeApproximately(1000.0 / 15, .001);
        await ContainerRuntimeHarness.Run(ctx, env, "Label1.Caption = \"Hello\"");
        label.Width.Should().Be(200);
        label.Height.Should().BeApproximately(1000.0 / 15, .001);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.5)]
    public async Task Runtime_triggers_and_explicit_dimensions(double scaling)
    {
        var (canvas, window, ctx, env) = ContainerRuntimeHarness.Laid(Form());
        window.SetRenderScaling(scaling);
        var label = canvas.Children.OfType<VBLabel>().Single();
        label.Width.Should().Be(200);
        label.Height.Should().BeApproximately(1000.0 / 15, .001);
        await ContainerRuntimeHarness.Run(ctx, env, "Label1.Caption = \"Hello\"");
        label.Width.Should().Be(200);
        await ContainerRuntimeHarness.Run(ctx, env, "Label1.Caption = \"Hi\"");
        label.Width.Should().NotBe(200);
        label.Height.Should().NotBeApproximately(1000.0 / 15, .001);
        Canvas.GetLeft(label).Should().Be(40);
        await ContainerRuntimeHarness.Run(ctx, env, "Label1.Width = 5000\r\nLabel1.Height = 1000");
        label.Width.Should().BeApproximately(5000.0 / 15, .001);
        label.Height.Should().BeApproximately(1000.0 / 15, .001);
        await ContainerRuntimeHarness.Run(ctx, env, "Label1.Caption = \"Hi\"");
        label.Width.Should().BeApproximately(5000.0 / 15, .001);
        label.Height.Should().BeApproximately(1000.0 / 15, .001);
        await ContainerRuntimeHarness.Run(ctx, env, "Label1.FontSize = 18");
        label.Width.Should().NotBeApproximately(5000.0 / 15, .001);
        label.Height.Should().NotBeApproximately(1000.0 / 15, .001);
        var width = label.Width;
        var height = label.Height;
        await ContainerRuntimeHarness.Run(ctx, env, "Label1.AutoSize = False\r\nLabel1.Caption = \"A much longer caption\"");
        label.Width.Should().Be(width);
        label.Height.Should().Be(height);
        await ContainerRuntimeHarness.Run(ctx, env, "Label1.AutoSize = True");
        label.Width.Should().NotBe(width);
        await ContainerRuntimeHarness.Run(ctx, env, "Label1.Width = 5000\r\nLabel1.Caption = \"x\"");
        label.Width.Should().NotBeApproximately(5000.0 / 15, .001);
    }

    [AvaloniaTheory]
    [InlineData("\"x\"", 75, 195, "", "MS Sans Serif", 8.25, 400)]
    [InlineData("\"i\"", 30, 195, "", "MS Sans Serif", 8.25, 400)]
    [InlineData("\"Hello\"", 360, 195, "", "MS Sans Serif", 8.25, 400)]
    [InlineData("\"Hello, world, a longer caption\"", 2055, 195, "", "MS Sans Serif", 8.25, 400)]
    [InlineData("\"\"", 45, 195, "", "MS Sans Serif", 8.25, 400)]
    [InlineData("\"two\" & vbCrLf & \"lines\"", 315, 390, "", "MS Sans Serif", 8.25, 400)]
    [InlineData("\"a\" & vbLf & \"b\"", 90, 390, "", "MS Sans Serif", 8.25, 400)]
    [InlineData("vbCrLf", 15, 390, "", "MS Sans Serif", 8.25, 400)]
    [InlineData("\"&File\"", 240, 195, "", "MS Sans Serif", 8.25, 400)]
    [InlineData("\"&File\"", 330, 195, "UseMnemonic = 0", "MS Sans Serif", 8.25, 400)]
    [InlineData("\"a&&b\"", 270, 195, "", "MS Sans Serif", 8.25, 400)]
    [InlineData("\"a\" & vbTab & \"b\"", 225, 195, "", "MS Sans Serif", 8.25, 400)]
    [InlineData("\"Hello\"", 420, 255, "BorderStyle = 1", "MS Sans Serif", 8.25, 400)]
    [InlineData("\"Hello\"", 390, 225, "BorderStyle = 1\nAppearance = 0", "MS Sans Serif", 8.25, 400)]
    [InlineData("\"Hello\"", 435, 240, "", "Arial", 9.75, 400)]
    [InlineData("\"a\" & vbTab & \"b\"", 360, 240, "", "Arial", 9.75, 400)]
    [InlineData("\"Hello\"", 855, 435, "", "MS Sans Serif", 18, 400)]
    [InlineData("\"Hello\"", 345, 195, "", "Tahoma", 8.25, 400)]
    [InlineData("\"Hello\"", 450, null, "", "MS Sans Serif", 8.25, 700)]
    public async Task Windows_measured_caption(string caption, double width, int? height, string properties, string family, double size, int weight)
    {
        if (!OperatingSystem.IsWindows()) throw Xunit.Sdk.SkipException.ForSkip("Requires Windows GDI and the measured fonts");
        foreach (var scaling in new[] { 1.0, 1.5 })
        {
            var (_, window, ctx, env) = ContainerRuntimeHarness.Laid(Form(properties, family, size, weight));
            window.SetRenderScaling(scaling);
            var debug = await ContainerRuntimeHarness.Run(ctx, env,
                $"Label1.Caption = \"zz\"\r\nLabel1.Caption = {caption}\r\nDebug.Print Label1.Width\r\nDebug.Print Label1.Height");
            Convert.ToDouble(debug[0].Value).Should().Be(width);
            if (height is { } measuredHeight)
                Convert.ToDouble(debug[1].Value).Should().Be(measuredHeight);
            window.Close();
        }
    }
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.5)]
    public async Task Windows_sequence_and_italic(double scaling)
    {
        if (!OperatingSystem.IsWindows()) throw Xunit.Sdk.SkipException.ForSkip("Requires Windows GDI and the measured fonts");
        var (_, window, ctx, env) = ContainerRuntimeHarness.Laid(Form());
        window.SetRenderScaling(scaling);
        var debug = await ContainerRuntimeHarness.Run(ctx, env, """
            Label1.Caption = "Hello, world, a longer caption"
            Debug.Print Label1.Width
            Label1.Width = 3000
            Label1.Left = 600
            Label1.Alignment = 2
            Label1.Caption = "x"
            Debug.Print Label1.Width
            Label1.Alignment = 1
            Label1.Caption = "Hi"
            Debug.Print Label1.Width
            Label1.Caption = "two" & vbCrLf & "lines"
            Debug.Print Label1.Width
            Debug.Print Label1.Height
            Label1.Caption = "Hello"
            Label1.FontSize = 18
            Debug.Print Label1.Width
            Debug.Print Label1.Height
            Label1.Width = 5000
            Debug.Print Label1.Width
            """);
        debug.Select(v => Convert.ToDouble(v.Value)).Should().Equal(2055, 90, 150, 315, 390, 855, 435, 5000);
        var (_, italicWindow, italicCtx, italicEnv) = ContainerRuntimeHarness.Laid(Form());
        italicWindow.SetRenderScaling(scaling);
        var italic = await ContainerRuntimeHarness.Run(italicCtx, italicEnv,
            "Label1.FontItalic = True\r\nDebug.Print Label1.Width");
        Convert.ToDouble(italic.Single().Value).Should().Be(450);
        var (_, emptyWindow, emptyCtx, emptyEnv) = ContainerRuntimeHarness.Laid(Form());
        emptyWindow.SetRenderScaling(scaling);
        var empty = await ContainerRuntimeHarness.Run(emptyCtx, emptyEnv, """
            Label1.Caption = "x"
            Debug.Print Label1.Width
            Label1.Caption = ""
            Debug.Print Label1.Width
            Debug.Print Label1.Height
            """);
        empty.Select(v => Convert.ToDouble(v.Value)).Should().Equal(75, 45, 195);
    }

    [AvaloniaTheory]
    [InlineData(1, 0, 600, 1005, 600, 1995, 600, 1995)]
    [InlineData(1.5, 0, 600, 1005, 600, 1995, 600, 1995)]
    [InlineData(1, 2, 600, 1005, 90, 2025, 90, 2025)]
    [InlineData(1.5, 2, 600, 1005, 90, 2025, 90, 2025)]
    public async Task Windows_wrapping_and_center_divergence(double scaling, int alignment,
        double left1, double width1, double left2, double width2, double left3, double width3)
    {
        if (!OperatingSystem.IsWindows()) throw Xunit.Sdk.SkipException.ForSkip("Requires Windows GDI and the measured fonts");
        var (_, window, ctx, env) = ContainerRuntimeHarness.Laid(Form($"WordWrap = -1\nWidth = 1000\nHeight = 195\nAlignment = {alignment}"));
        window.SetRenderScaling(scaling);
        var debug = await ContainerRuntimeHarness.Run(ctx, env, """
            Label1.Caption = "The quick brown fox jumps over the lazy dog"
            Debug.Print Label1.Left
            Debug.Print Label1.Width
            Debug.Print Label1.Height
            Label1.Caption = "Antidisestablishmentarianism"
            Debug.Print Label1.Left
            Debug.Print Label1.Width
            Debug.Print Label1.Height
            Label1.Caption = "Hi"
            Debug.Print Label1.Left
            Debug.Print Label1.Width
            Debug.Print Label1.Height
            """);
        debug.Select(v => Convert.ToDouble(v.Value)).Should().Equal(left1, width1, 780, left2, width2, 195, left3, width3, 195);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.5)]
    public async Task Windows_enable_disable_and_alignment_edges(double scaling)
    {
        if (!OperatingSystem.IsWindows()) throw Xunit.Sdk.SkipException.ForSkip("Requires Windows GDI and the measured fonts");
        var (_, window, ctx, env) = ContainerRuntimeHarness.Laid(Form("AutoSize = 0\nTop = 1400"));
        window.SetRenderScaling(scaling);
        var debug = await ContainerRuntimeHarness.Run(ctx, env, """
            Label1.AutoSize = True
            Debug.Print Label1.Width
            Debug.Print Label1.Height
            Label1.AutoSize = False
            Label1.Caption = "Hello, world, a longer caption"
            Debug.Print Label1.Width
            Label1.AutoSize = True
            Debug.Print Label1.Left
            Debug.Print Label1.Top
            Debug.Print Label1.Width
            Label1.Caption = "x"
            Label1.Alignment = 2
            Label1.Left = 600
            Label1.Width = 3000
            Label1.Caption = "Hello, world, a longer caption"
            Debug.Print Label1.Left
            Debug.Print Label1.Width
            Label1.Caption = "x"
            Label1.Alignment = 1
            Label1.Left = 600
            Label1.Width = 3000
            Label1.Caption = "Hello, world, a longer caption"
            Debug.Print Label1.Left
            Debug.Print Label1.Width
            """);
        debug.Select(v => Convert.ToDouble(v.Value)).Should().Equal(360, 195, 360, 600, 1395, 2055, 1065, 2070, 1545, 2055);
    }

}
