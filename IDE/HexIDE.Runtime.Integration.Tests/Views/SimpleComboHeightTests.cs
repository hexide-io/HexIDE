using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// VB6 SP6 native/p-code measurements: docs/vb6-fidelity-oracle/a-simple-combos-height-by-font.md (#1655).
public class SimpleComboHeightTests
{
    private sealed class Sink : IDeserializeErrorSink { public void LogError(string error) => throw new Exception(error); }

    private static string Source(string font, double size, int height, int style, bool bold, string code) =>
        ($"VERSION 5.00\nBegin VB.Form Form1\n   Begin VB.ComboBox Combo1\n"
        + $"      Height = {height}\n      Width = 2000\n      Style = {style}\n"
        + (font == "MS Sans Serif" && size == 8.25 && !bold ? "" :
            FormattableString.Invariant($"      BeginProperty Font\n         Name = \"{font}\"\n         Size = {size}\n         Weight = {(bold ? 700 : 400)}\n         Charset = 0\n         Italic = 0\n         Underline = 0\n         Strikethrough = 0\n      EndProperty\n"))
        + "   End\nEnd\nAttribute VB_Name = \"Form1\"\nPrivate Sub Form_Load()\n"
        + code + "\nEnd Sub\n").ReplaceLineEndings("\r\n");

    private static void Run(string font, double size, int height, int style, bool bold, string code,
        Action<VBComboBox, List<string>> check)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, Source(font, size, height, style, bold, code), new Sink())!;
        project.AddForm(form);
        var printed = new List<string>();
        VBDebugConsole.Output += printed.Add;
        try
        {
            VBLoader.RunForm(form, CancellationToken.None, out var window);
            try
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                check(window.GetVisualDescendants().OfType<VBComboBox>().Single(), printed);
            }
            finally { window.Close(); }
        }
        finally { VBDebugConsole.Output -= printed.Add; }
    }

    [AvaloniaTheory]
    [InlineData("MS Sans Serif", 8.25, 400, 315)]
    [InlineData("MS Sans Serif", 8.25, 900, 765)]
    [InlineData("MS Sans Serif", 8.25, 1500, 1350)]
    [InlineData("MS Sans Serif", 8.25, 2000, 1935)]
    [InlineData("MS Sans Serif", 9.75, 400, 360)]
    [InlineData("MS Sans Serif", 9.75, 900, 900)]
    [InlineData("MS Sans Serif", 9.75, 1500, 1380)]
    [InlineData("MS Sans Serif", 9.75, 2000, 1860)]
    [InlineData("Arial", 8.25, 400, 330)]
    [InlineData("Arial", 8.25, 900, 810)]
    [InlineData("Arial", 8.25, 1500, 1440)]
    [InlineData("Arial", 8.25, 2000, 1860)]
    [InlineData("Arial", 12, 400, 390)]
    [InlineData("Arial", 12, 900, 720)]
    [InlineData("Arial", 12, 1500, 1260)]
    [InlineData("Arial", 12, 2000, 1800)]
    [InlineData("Tahoma", 8.25, 400, 315)]
    [InlineData("Tahoma", 8.25, 900, 765)]
    [InlineData("Tahoma", 8.25, 1500, 1350)]
    [InlineData("Tahoma", 8.25, 2000, 1935)]
    [InlineData("Courier New", 9.75, 400, 360)]
    [InlineData("Courier New", 9.75, 900, 900)]
    [InlineData("Courier New", 9.75, 1500, 1380)]
    [InlineData("Courier New", 9.75, 2000, 1860)]
    [InlineData("Times New Roman", 14.25, 400, 435)]
    [InlineData("Times New Roman", 14.25, 900, 810)]
    [InlineData("Times New Roman", 14.25, 1500, 1440)]
    [InlineData("Times New Roman", 14.25, 2000, 1755)]
    public void Load_reads_the_snapped_height(string font, double size, int saved, int expected)
    {
        Run(font, size, saved, 1, false, "Debug.Print Combo1.Height", (combo, printed) =>
        {
            printed.Should().Equal($" {expected} ");
            combo.Height.Should().Be(expected / 15d);
        });
    }

    [AvaloniaTheory]
    [InlineData("MS Sans Serif", 8.25, 21)]
    [InlineData("MS Sans Serif", 9.75, 24)]
    [InlineData("Arial", 8.25, 22)]
    [InlineData("Arial", 12, 26)]
    [InlineData("Tahoma", 8.25, 21)]
    [InlineData("Courier New", 9.75, 24)]
    [InlineData("Times New Roman", 14.25, 29)]
    public void The_edit_box_uses_the_measured_height_and_no_list_is_shown_without_a_row(string font, double size, int expected)
    {
        Run(font, size, 400, 1, false, "", (combo, _) =>
        {
            combo.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "PART_EditableTextBox")
                .Bounds.Height.Should().Be(expected);
            combo.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_List")
                .IsVisible.Should().BeFalse();
        });
    }

    [AvaloniaTheory]
    [InlineData("MS Sans Serif", 8.25, 13, 65)]
    [InlineData("MS Sans Serif", 9.75, 16, 64)]
    [InlineData("Arial", 8.25, 14, 70)]
    [InlineData("Arial", 12, 18, 54)]
    [InlineData("Tahoma", 8.25, 13, 65)]
    [InlineData("Courier New", 9.75, 16, 64)]
    [InlineData("Times New Roman", 14.25, 21, 63)]
    public void The_list_viewport_fits_whole_measured_rows(string font, double size, int rowHeight, int viewportHeight)
    {
        Run(font, size, 1500, 1, false, "For i = 1 To 10: Combo1.AddItem CStr(i): Next", (combo, _) =>
        {
            combo.ContainerFromIndex(0)!.Bounds.Height.Should().Be(rowHeight);
            combo.GetVisualDescendants().OfType<ScrollViewer>().Single(v => v.Content is Avalonia.Controls.Presenters.ItemsPresenter)
                .Viewport.Height.Should().Be(viewportHeight);
        });
    }

    [AvaloniaTheory]
    [InlineData("MS Sans Serif", 8.25, 1935, 1000, 960)]
    [InlineData("MS Sans Serif", 8.25, 1935, 1100, 960)]
    [InlineData("MS Sans Serif", 8.25, 1935, 1200, 1155)]
    [InlineData("MS Sans Serif", 8.25, 1935, 1500, 1350)]
    [InlineData("MS Sans Serif", 8.25, 1935, 2000, 1935)]
    [InlineData("MS Sans Serif", 8.25, 1935, 300, 1935)]
    [InlineData("MS Sans Serif", 8.25, 1935, 0, 1935)]
    [InlineData("Arial", 12, 1800, 1000, 990)]
    [InlineData("Arial", 12, 1800, 1100, 990)]
    [InlineData("Arial", 12, 1800, 1200, 990)]
    [InlineData("Arial", 12, 1800, 1500, 1260)]
    [InlineData("Arial", 12, 1800, 2000, 1800)]
    [InlineData("Arial", 12, 1800, 300, 1800)]
    [InlineData("Arial", 12, 1800, 0, 1800)]
    public void Code_snaps_or_keeps_the_height_without_error(string font, double size, int saved, int assigned, int expected)
    {
        Run(font, size, saved, 1, false,
            $"On Error Resume Next\nCombo1.Height = {assigned}\nDebug.Print Combo1.Height & \"/\" & Err.Number",
            (_, printed) => printed.Should().Equal($"{expected}/0"));
    }

    [AvaloniaTheory]
    [InlineData("MS Sans Serif", 8.25, 0, false)]
    [InlineData("MS Sans Serif", 8.25, 2, false)]
    [InlineData("Unmeasured Font", 8.25, 1, false)]
    [InlineData("MS Sans Serif", 8.25, 1, true)]
    public void Excluded_cases_keep_the_saved_height_and_assignment_refusal(string font, double size, int style, bool bold)
    {
        Run(font, size, 1500, style, bold,
            "Debug.Print Combo1.Height\nOn Error Resume Next\nCombo1.Height = 1000\nDebug.Print Err.Number",
            (_, printed) => printed.Should().Equal(" 1500 ", " 383 "));
    }

    [AvaloniaFact]
    public void An_unmeasured_assignment_between_edit_box_and_one_row_keeps_383()
    {
        Run("MS Sans Serif", 8.25, 1500, 1, false,
            "On Error Resume Next\nCombo1.Height = 400\nDebug.Print Err.Number",
            (_, printed) => printed.Should().Equal(" 383 "));
    }
}
