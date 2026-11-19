using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Integration.Tests;
using HexIDE.Integration.Tests.Views;

namespace HexIDE.Runtime.Integration.Tests.Views;

// VB6 SP6 native and p-code measurements in #4971 (oracle PR #4973).
public class FontDecorationTests : WindowTestFixture
{
    private const string Sample = "iiiiiiiiiiiiiiii";

    public static IEnumerable<object[]> Designs()
    {
        foreach (var control in new[] { "Label", "CommandButton", "CheckBox", "OptionButton", "Frame", "TextBox", "ListBox", "ComboBox" })
        foreach (var line in new[] { TextDecorationLocation.Underline, TextDecorationLocation.Strikethrough })
        foreach (var scaling in new[] { 1d, 1.5 })
            yield return new object[] { control, line, scaling };
    }

    [AvaloniaTheory]
    [MemberData(nameof(Designs))]
    public async Task A_saved_font_draws_its_line(string kind, TextDecorationLocation line, double scaling)
    {
        var plain = await Capture(kind, scaling);
        var decorated = await Capture(kind, scaling, line.ToString());

        CheckAddedLine(plain, decorated);
        decorated.Lines.Should().Equal(line);
    }

    [AvaloniaTheory]
    [InlineData(1d)]
    [InlineData(1.5)]
    public async Task A_program_can_add_and_clear_both_lines(double scaling)
    {
        var run = ContainerRuntimeHarness.Laid(Form("Label", null));
        using var window = new WindowHandle(run.window);
        run.window.SetRenderScaling(scaling);
        var label = ContainerRuntimeHarness.Child(run.canvas, "Subject");
        var plain = Paint(run.window, label);
        await ContainerRuntimeHarness.Run(run.ctx, run.env, "Subject.FontStrikethru = True\r\n");
        var struck = Paint(run.window, label);
        CheckAddedLine(plain, struck);
        struck.Lines.Should().Equal(TextDecorationLocation.Strikethrough);

        await ContainerRuntimeHarness.Run(run.ctx, run.env, "Subject.FontUnderline = True\r\n");
        var both = Paint(run.window, label);
        CheckAddedLine(struck, both);
        both.Lines.Should().BeEquivalentTo(new[] { TextDecorationLocation.Strikethrough, TextDecorationLocation.Underline });

        await ContainerRuntimeHarness.Run(run.ctx, run.env, "Subject.FontStrikethru = False\r\nSubject.FontUnderline = False\r\n");
        var cleared = Paint(run.window, label);
        cleared.Rows.Should().Equal(plain.Rows);
        cleared.Lines.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(1d)]
    [InlineData(1.5)]
    public async Task A_plain_font_has_no_line(double scaling)
    {
        var plain = await Capture("Label", scaling);
        plain.Rows.Should().OnlyContain(run => run < plain.Width * 0.8);
        plain.Lines.Should().BeEmpty();
    }

    private static string Form(string kind, string? decoration)
    {
        var text = kind switch
        {
            "TextBox" or "ComboBox" => $"Text = \"{Sample}\"",
            "ListBox" => "",
            _ => $"Caption = \"{Sample}\"",
        };
        return $$"""
            VERSION 5.00
            Begin VB.Form Form1
               Begin VB.{{kind}} Subject
                  Left = 300
                  Top = 300
                  Width = 2800
                  Height = {{(kind == "ComboBox" ? 420 : 870)}}
                  ForeColor = &H000000FF&
                  {{text}}
                  BeginProperty Font
                     Name = "MS Sans Serif"
                     Size = 12
                     Charset = 0
                     Weight = 400
                     Underline = {{(decoration == "Underline" ? -1 : 0)}}
                     Italic = 0
                     Strikethrough = {{(decoration == "Strikethrough" ? -1 : 0)}}
                  EndProperty
               End
            End
            Attribute VB_Name = "Form1"
            """;
    }

    private static async Task<Ink> Capture(string kind, double scaling, string? decoration = null)
    {
        var (canvas, window, ctx, env) = ContainerRuntimeHarness.Laid(Form(kind, decoration));
        using var lifetime = new WindowHandle(window);
        window.SetRenderScaling(scaling);
        if (kind == "ListBox")
            await ContainerRuntimeHarness.Run(ctx, env, $"Subject.AddItem \"{Sample}\"\r\n");

        return Paint(window, ContainerRuntimeHarness.Child(canvas, "Subject"));
    }

    private sealed record Ink(int[] Rows, int Width, TextDecorationLocation[] Lines);

    private static void CheckAddedLine(Ink before, Ink after)
    {
        var minimum = before.Width * 0.8;
        before.Rows.Zip(after.Rows).Should().Contain(row => row.First < minimum && row.Second >= minimum);
        // Ink excludes the end bearings of the glyphs; the decoration uses their full advance width.
        after.Width.Should().BeLessThanOrEqualTo((int)Math.Ceiling(before.Width * 1.15));
    }

    private static Ink Paint(Window window, Control control)
    {
        window.UpdateLayout();
        var context = ExecutionContext.Capture();
        Dispatcher.UIThread.RunJobs();
        ExecutionContext.Restore(context!);
        var text = control.GetVisualDescendants().OfType<Control>().First(child =>
            child is TextBlock { Text: Sample } or TextPresenter { Text: Sample });
        var decorations = text.GetValue(TextBlock.TextDecorationsProperty);
        var scaling = window.RenderScaling;
        var origin = control.TranslatePoint(default, window)!.Value;
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame");
        using var buffer = frame.Lock();
        var runs = new List<int>();
        int minX = buffer.Size.Width, maxX = -1;
        for (var y = (int)Math.Floor(origin.Y * scaling); y < Math.Ceiling((origin.Y + control.Bounds.Height) * scaling); y++)
        {
            int current = 0, longest = 0;
            for (var x = (int)Math.Floor(origin.X * scaling); x < Math.Ceiling((origin.X + control.Bounds.Width) * scaling); x++)
            {
                var offset = y * buffer.RowBytes + x * 4;
                var red = Marshal.ReadByte(buffer.Address, offset + (buffer.Format == PixelFormat.Bgra8888 ? 2 : 0));
                var green = Marshal.ReadByte(buffer.Address, offset + 1);
                if (red - green > 20)
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    longest = Math.Max(longest, ++current);
                }
                else
                    current = 0;
            }
            runs.Add(longest);
        }
        var textWidth = maxX - minX + 1;
        textWidth.Should().BeGreaterThan(0, "the sample should paint in its red ForeColor");
        return new Ink(runs.ToArray(), textWidth, decorations?.Select(line => line.Location).ToArray() ?? []);
    }

    private sealed class WindowHandle(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }
}
