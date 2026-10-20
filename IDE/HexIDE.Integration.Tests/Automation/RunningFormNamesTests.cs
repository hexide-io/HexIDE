using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// A running form's controls carry the names their code uses, and are named as their captions read (#954, #1128). No node
/// used to carry <c>Text1</c> or <c>Command1</c>, so a caller holding names from the code had no bridge to the running
/// form, and a button captioned <c>&amp;OK</c> was <c>Button[_OK]</c>, a menu <c>MenuItem[&amp;Game]</c>.
/// </summary>
public class RunningFormNamesTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static string Button(string name, string caption, int top, int? index = null) =>
        $"   Begin VB.CommandButton {name} \r\n      Caption         =   \"{caption}\"\r\n      Height          =   375\r\n"
      + (index is { } i ? $"      Index           =   {i}\r\n" : "")
      + $"      Left            =   120\r\n      Top             =   {top}\r\n      Width           =   1215\r\n   End\r\n";

    private static readonly string Frm =
        "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"Form1\"\r\n   ClientHeight    =   4000\r\n"
      + "   ClientWidth     =   4680\r\n"
      + Button("Command1", "&OK", 120)
      + Button("Command2", "A", 600, 0)
      + Button("Command2", "B", 1080, 1)
      + Button("Command3", "E&xit", 1560)
      + "   Begin VB.CheckBox Check1 \r\n      Caption         =   \"Tom && Jerry\"\r\n      Height          =   375\r\n"
      + "      Left            =   2400\r\n      Top             =   120\r\n      Width           =   1500\r\n   End\r\n"
      + "   Begin VB.TextBox Text1 \r\n      Height          =   375\r\n      Left            =   2400\r\n"
      + "      Top             =   600\r\n      Width           =   1500\r\n   End\r\n"
      + "   Begin VB.Menu mnuGame \r\n      Caption         =   \"&Game\"\r\n"
      + "      Begin VB.Menu mnuNew \r\n         Caption         =   \"&New\"\r\n      End\r\n   End\r\n"
      + "End\r\nAttribute VB_Name = \"Form1\"\r\n"
      + "Private Sub Form_Load()\r\n    Load Command2(2)\r\n    Command2(2).Visible = True\r\nEnd Sub\r\n"
      + "Private Sub Command1_Click()\r\n    Command1.Caption = \"&Cancel\"\r\nEnd Sub\r\n";

    private static Window Run()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, Frm, new NullSink())!;
        project.AddForm(form);
        VBLoader.RunForm(form, CancellationToken.None, out var window);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static IEnumerable<UiNode> Flatten(UiNode node) => node.Children.SelectMany(Flatten).Prepend(node);

    private static string? NameOf(Control control) => ControlAutomationPeer.CreatePeerForElement(control).GetName();

    private static Control Named(Window window, string name, int? index = null) =>
        window.GetLogicalDescendants().OfType<Control>().First(c => VBProps.GetName(c) == name && VBProps.GetIndex(c) == index);

    [AvaloniaFact]
    public void Each_control_is_shown_and_reached_by_the_name_its_code_uses()
    {
        var window = Run();
        try
        {
            var nodes = Flatten(UiAutomationDriver.Dump(window, "Window", 10, false)).ToList();

            nodes.Select(n => n.Path).Should().Contain(
            [
                "Window/Button[Command1]", "Window/Button[Command2(0)]", "Window/Button[Command2(1)]",
                "Window/Button[Command2(2)]", "Window/Button[Command3]", "Window/CheckBox[Check1]", "Window/Edit[Text1]",
            ], "a control array's elements share a name, so each is told apart by its index, as the code does");
            nodes.Single(n => n.Path == "Window/Button[Command1]").Name.Should().Be("OK");
            UiAutomationDriver.Resolve(window, "Window/#Text1").control.Should().BeSameAs(Named(window, "Text1"));
            UiAutomationDriver.Resolve(window, "Window/#Command2(1)").control.Should().BeSameAs(Named(window, "Command2", 1));
        }
        finally { window.Close(); }
    }

    // The caption still reaches a control, as it reads: a mid-word access key used to leave E_xit, which Exit missed.
    [AvaloniaFact]
    public void A_captioned_control_is_named_as_its_caption_reads()
    {
        var window = Run();
        try
        {
            NameOf(Named(window, "Command3")).Should().Be("Exit");
            NameOf(Named(window, "Check1")).Should().Be("Tom & Jerry", "&& shows one &");
            UiAutomationDriver.Resolve(window, "Window/Button[Exit]").control.Should().BeSameAs(Named(window, "Command3"));

            UiAutomationDriver.Interact(Named(window, "Command1"), "invoke", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();

            NameOf(Named(window, "Command1")).Should().Be("Cancel", "the name follows a caption the program sets");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_menu_carries_its_name_and_is_named_as_its_caption_reads()
    {
        var window = Run();
        try
        {
            var game = Named(window, "mnuGame");

            Avalonia.Automation.AutomationProperties.GetAutomationId(game).Should().Be("mnuGame");
            NameOf(game).Should().Be("Game");
            UiAutomationDriver.Resolve(window, "Window/#mnuGame").control.Should().BeSameAs(game);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("&OK", "OK")]
    [InlineData("E&xit", "Exit")]
    [InlineData("Tom && Jerry", "Tom & Jerry")]
    [InlineData("&&&Save", "&Save")]
    [InlineData("Plain", "Plain")]
    [InlineData("Ends &", "Ends ")]
    public void A_caption_reads_without_its_access_key_marker(string caption, string shown) =>
        VBCaption.AsShown(caption).Should().Be(shown);
}
