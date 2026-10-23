using System.Linq;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The window styles a running form is given (#1575). Each row is a form vb6.exe built and ran, native and p-code agreeing,
/// with what <c>GetWindowLong</c> read from its window: the oracle's "A form's window styles". The style is masked to the
/// bits <see cref="FormChrome"/> decides, as the window's visibility and clipping are Avalonia's.
/// </summary>
public class FormChromeTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Fixed = "   BorderStyle     =   1  'Fixed Single\r\n";
    private const string NoBox = "   ControlBox      =   0   'False\r\n";
    private const string Off = "   MaxButton       =   0   'False\r\n   MinButton       =   0   'False\r\n";
    private const string On = "   MaxButton       =   -1  'True\r\n   MinButton       =   -1  'True\r\n";

    private static FormChrome Read(string lines, string caption, bool menu)
    {
        var frm = "VERSION 5.00\r\nBegin VB.Form Form1 \r\n" + lines + $"   Caption         =   \"{caption}\"\r\n"
                  + "   ClientHeight    =   1500\r\n   ClientWidth     =   2520\r\n"
                  + (menu ? "   Begin VB.Menu mnuFile \r\n      Caption         =   \"File\"\r\n   End\r\n" : "")
                  + "End\r\nAttribute VB_Name = \"Form1\"\r\n";
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), frm, new NullSink())!;
        return FormChrome.Read(form.Components.First(c => c.BaseClass is FormComponentClass), VBLoader.HasMenuBar(form));
    }

    [Theory]
    [InlineData("B0", "   BorderStyle     =   0  'None\r\n", "B0", false, 0x00000000u, 0x00u)]
    [InlineData("B0m", "   BorderStyle     =   0  'None\r\n", "B0m", true, 0x00CB0000u, 0x00u)]
    [InlineData("B1", Fixed, "B1", false, 0x00CB0000u, 0x00u)]
    [InlineData("B1x0", Fixed + Off, "B1x0", false, 0x00C80000u, 0x00u)]
    [InlineData("B1x1", Fixed + On, "B1x1", false, 0x00CB0000u, 0x00u)]
    [InlineData("B2", "", "B2", false, 0x00CF0000u, 0x00u)]
    [InlineData("B2n0", "   MinButton       =   0   'False\r\n", "B2n0", false, 0x00CD0000u, 0x00u)]
    [InlineData("B2a0", "   MaxButton       =   0   'False\r\n", "B2a0", false, 0x00CE0000u, 0x00u)]
    [InlineData("B2c0", NoBox, "B2c0", false, 0x00C70000u, 0x00u)]
    [InlineData("B2c0e", NoBox, "", false, 0x00870000u, 0x00u)]
    [InlineData("B2c0x1", NoBox + On, "B2c0x1", false, 0x00C70000u, 0x00u)]
    [InlineData("B3", "   BorderStyle     =   3  'Fixed Dialog\r\n", "B3", false, 0x00C80080u, 0x01u)]
    [InlineData("B3x1", "   BorderStyle     =   3  'Fixed Dialog\r\n" + On, "B3x1", false, 0x00C80080u, 0x01u)]
    [InlineData("B4", "   BorderStyle     =   4  'Fixed ToolWindow\r\n", "B4", false, 0x00CB0000u, 0x80u)]
    [InlineData("B5", "   BorderStyle     =   5  'Sizable ToolWindow\r\n", "B5", false, 0x00CF0000u, 0x80u)]
    [InlineData("B5x1", "   BorderStyle     =   5  'Sizable ToolWindow\r\n" + On, "B5x1", false, 0x00CF0000u, 0x80u)]
    [InlineData("B1c0e", Fixed + NoBox, "", false, 0x00830000u, 0x00u)]
    [InlineData("B2t0", "   ShowInTaskbar   =   0   'False\r\n", "B2t0", false, 0x00CF0000u, 0x00u)]
    [InlineData("B3t1", "   BorderStyle     =   3  'Fixed Dialog\r\n   ShowInTaskbar   =   -1  'True\r\n", "B3t1", false, 0x00C80080u, 0x01u)]
    [InlineData("B4t1", "   BorderStyle     =   4  'Fixed ToolWindow\r\n   ShowInTaskbar   =   -1  'True\r\n", "B4t1", false, 0x00CB0000u, 0x80u)]
    public void The_window_has_the_styles_vb6_exe_gave_it(string row, string lines, string caption, bool menu, uint style, uint exStyle)
    {
        var (s, e) = Read(lines, caption, menu).Win32Styles;

        (s & FormChrome.OwnedStyle).Should().Be(style, row);
        (e & FormChrome.OwnedExStyle).Should().Be(exStyle, row);
    }

    // Elsewhere the same rules drive Avalonia's own window properties.
    [Theory]
    [InlineData("   BorderStyle     =   0  'None\r\n", "x", "None", false, false, false)]
    [InlineData(Fixed, "x", "Full", false, true, true)]
    [InlineData(Fixed + Off, "x", "Full", false, false, false)]
    [InlineData("", "x", "Full", true, true, true)]
    [InlineData(NoBox, "x", "Full", true, false, false)]
    [InlineData(NoBox, "", "BorderOnly", true, false, false)]
    [InlineData("   BorderStyle     =   3  'Fixed Dialog\r\n" + On, "x", "Full", false, false, false)]
    [InlineData("   BorderStyle     =   5  'Sizable ToolWindow\r\n", "x", "Full", true, false, false)]
    public void Avalonia_is_given_the_same_window(string lines, string caption, string decorations, bool resize, bool min, bool max)
    {
        var chrome = Read(lines, caption, menu: false);

        chrome.Decorations.ToString().Should().Be(decorations);
        (chrome.CanResize, chrome.CanMinimize, chrome.CanMaximize).Should().Be((resize, min, max));
    }
}
