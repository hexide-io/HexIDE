using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

/// <summary>
/// The form designer asks the registered run engine to preview a form and which carried controls it runs (#5633). The
/// engine's code is the provider's. The test here asks the engine directly; the class of the same name in
/// <c>HexIDE.Integration.Tests</c> reads what the designer then shows.
/// </summary>
public class DesignerRunEngineTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    // A form the provider's interpreter previews, carrying one control it runs: a top-level Toolbar (#2327).
    private const string ToolbarForm =
        "VERSION 5.00\r\n" +
        "Object = \"{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0\"; \"MSCOMCTL.OCX\"\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   ClientHeight    =   4500\r\n" +
        "   ClientLeft      =   60\r\n" +
        "   ClientTop       =   345\r\n" +
        "   ClientWidth     =   6000\r\n" +
        "   Begin MSComctlLib.Toolbar Toolbar1 \r\n" +
        "      Align           =   1  'Align Top\r\n" +
        "      Height          =   660\r\n" +
        "      Left            =   0\r\n" +
        "      TabIndex        =   0\r\n" +
        "      Top             =   0\r\n" +
        "      Width           =   6000\r\n" +
        "      _Version        =   393216\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    // A box the preview raises with no title takes the program's name, as App.Title does under F5, never the IDE's (#3411).
    [AvaloniaTheory]
    [InlineData("Title=\"Accounts\"", "Accounts")]
    [InlineData(null, "P")]
    public void A_box_the_preview_raises_untitled_takes_the_programs_name(string? titleLine, string expected)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        if (titleLine is not null)
            project.UnknownPreSectionLines.Add((0, titleLine));
        var form = new FormDeserializer().Deserialize(project, ToolbarForm, new NullSink())!;

        HexIDE.Runtime.Running.InterpreterRunEngine.UntitledBoxTitle(form).Should().Be(expected);
    }
}
