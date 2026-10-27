using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Controls;

/// <summary>
/// A ProgressBar drawn as VB6 draws it (#1082), pixel for pixel, against VB6's own: corpus/inbox-controls/look holds two
/// forms of ProgressBars and the client area of each as it ran under vb6.exe on Windows 10, captured from the screen.
/// </summary>
/// <remarks>
/// The comparison is by the system colour each pixel is, not by its value: VB6's capture has Windows 10's colours, and
/// HexIDE draws in its theme's. Windows 10 gives each of the seven a different value, so each of VB6's pixels names one.
/// </remarks>
public class ProgressBarDrawingTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    // Windows 10's values for the seven system colours a ProgressBar draws in, as VB6's captures hold them.
    private static readonly Dictionary<(byte R, byte G, byte B), VbSystemColor> Windows10 = new()
    {
        [(240, 240, 240)] = VbSystemColor.Btnface,
        [(160, 160, 160)] = VbSystemColor.Btnshadow,
        [(105, 105, 105)] = VbSystemColor._3DDKSHADOW,
        [(227, 227, 227)] = VbSystemColor._3DLIGHT,
        [(255, 255, 255)] = VbSystemColor.Btnhighlight,
        [(0, 120, 215)] = VbSystemColor.Highlight,
        [(100, 100, 100)] = VbSystemColor.Windowframe,
    };

    private static string LookDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Join(dir.FullName, "corpus", "inbox-controls", "look")))
            dir = dir.Parent;
        dir.Should().NotBeNull("corpus/inbox-controls/look ships with the repository");
        return Path.Join(dir!.FullName, "corpus", "inbox-controls", "look");
    }

    // VB6's capture, as the system colour of each pixel.
    private static VbSystemColor[,] Captured(string png)
    {
        using var bitmap = new Bitmap(png);
        var size = bitmap.PixelSize;
        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(default, size), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }
        var rgba = bitmap.Format == PixelFormats.Rgba8888;
        var roles = new VbSystemColor[size.Width, size.Height];
        for (var y = 0; y < size.Height; y++)
            for (var x = 0; x < size.Width; x++)
            {
                var i = y * stride + x * 4;
                var (r, g, b) = rgba ? (pixels[i], pixels[i + 1], pixels[i + 2]) : (pixels[i + 2], pixels[i + 1], pixels[i]);
                Windows10.TryGetValue((r, g, b), out var role).Should().BeTrue($"({x},{y}) is #{r:X2}{g:X2}{b:X2}, none of the seven");
                roles[x, y] = role;
            }
        return roles;
    }

    // The Values the form's Form_Load sets, `pbQ.Value = 25: pbThird.Value = 1`, which is what VB6 ran.
    private static Dictionary<string, float> Values(string frm) =>
        Regex.Matches(frm, @"(\w+)\.Value = ([0-9.]+)")
            .ToDictionary(m => m.Groups[1].Value, m => float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));

    [AvaloniaTheory]
    [InlineData("look")]
    [InlineData("look2")]
    public void Each_bar_is_VB6s_pixel_for_pixel(string look)
    {
        var dir = LookDirectory();
        var frm = File.ReadAllText(Path.Join(dir, look + ".frm"));
        var captured = Captured(Path.Join(dir, look + ".png"));
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), frm, new NullSink())!;
        var (_, canvas, _, _) = ContainerRuntimeHarness.Spawn(frm);
        var values = Values(frm);

        var bars = form.Components.Where(c => c.BaseClass is ProgressBarComponentClass).ToList();
        bars.Should().NotBeEmpty();
        var report = new StringBuilder();
        foreach (var component in bars)
        {
            var name = component.GetPropertyOrDefault(VBProperties.NameProperty)!;
            var (left, top) = ((int)component.GetPropertyOrDefault(VBProperties.LeftProperty), (int)component.GetPropertyOrDefault(VBProperties.TopProperty));
            var (width, height) = ((int)component.GetPropertyOrDefault(VBProperties.WidthProperty), (int)component.GetPropertyOrDefault(VBProperties.HeightProperty));
            var bar = ContainerRuntimeHarness.Child(canvas, name).Should().BeOfType<VBProgressBar>().Subject;
            bar.VbValue = values.GetValueOrDefault(name);

            var drawn = new VbSystemColor[width, height];
            foreach (var (colour, x, y, w, h) in bar.Paint(width, height))
                for (var dy = 0; dy < h; dy++)
                    for (var dx = 0; dx < w; dx++)
                        drawn[x + dx, y + dy] = colour;

            var wrong = new List<string>();
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    if (drawn[x, y] != captured[left + x, top + y])
                        wrong.Add($"({x},{y}) {drawn[x, y]} where VB6 has {captured[left + x, top + y]}");
            if (wrong.Count > 0)
                report.AppendLine($"{name}: {wrong.Count} px wrong, first {string.Join("; ", wrong.Take(4))}");
        }

        report.ToString().Should().BeEmpty();
    }

    // VB6's designer draws a ProgressBar full, whatever its Min and Max (measured): Value isn't saved, so it has none to draw.
    // A running one starts empty.
    [AvaloniaFact]
    public void The_designer_draws_a_bar_full_and_a_run_starts_it_empty()
    {
        var instance = new ComponentInstance(ProgressBarComponentClass.Instance, "pb")
            .SetProperty(VBProperties.WidthProperty, 200)
            .SetProperty(VBProperties.HeightProperty, 20);

        var designed = ProgressBarComponentClass.Instance.InstantiateForDesigner(instance).Should().BeOfType<VBProgressBar>().Subject;
        var running = ProgressBarComponentClass.Instance.Instantiate(instance).Should().BeOfType<VBProgressBar>().Subject;

        designed.ShowFull.Should().BeTrue();
        running.ShowFull.Should().BeFalse();
        // The 3-D frame and the gap leave a fill area from x 3 to 196; full, its blocks run to 1 px short of its end.
        designed.Paint(200, 20).Where(r => r.Colour == VbSystemColor.Highlight).Max(r => r.X + r.Width).Should().Be(196);
        running.Paint(200, 20).Should().NotContain(r => r.Colour == VbSystemColor.Highlight);
    }
}
