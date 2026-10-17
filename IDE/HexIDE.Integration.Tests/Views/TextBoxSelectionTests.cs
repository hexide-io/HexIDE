using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A TextBox's SelStart, SelLength and SelText (#1150). The form is vb6.exe's probe, run as it ran there, and every line
/// it prints is the line vb6.exe logged, native and p-code alike (the oracle's "A TextBox's selection").
/// </summary>
public class TextBoxSelectionTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Probe =
        "VERSION 5.00\r\n"
        + "Begin VB.Form Form1\r\n"
        + "   Caption         =   \"p\"\r\n"
        + "   ClientHeight    =   3000\r\n"
        + "   ClientLeft      =   60\r\n"
        + "   ClientTop       =   450\r\n"
        + "   ClientWidth     =   4500\r\n"
        + "   ScaleHeight     =   3000\r\n"
        + "   ScaleWidth      =   4500\r\n"
        + "   Begin VB.TextBox Text3\r\n"
        + "      Height          =   375\r\n"
        + "      Left            =   120\r\n"
        + "      Locked          =   -1  'True\r\n"
        + "      TabIndex        =   2\r\n"
        + "      Top             =   1200\r\n"
        + "      Width           =   2000\r\n"
        + "   End\r\n"
        + "   Begin VB.TextBox Text2\r\n"
        + "      Height          =   375\r\n"
        + "      Left            =   120\r\n"
        + "      MultiLine       =   -1  'True\r\n"
        + "      TabIndex        =   1\r\n"
        + "      Top             =   600\r\n"
        + "      Width           =   2000\r\n"
        + "   End\r\n"
        + "   Begin VB.TextBox Text1\r\n"
        + "      Height          =   375\r\n"
        + "      Left            =   120\r\n"
        + "      TabIndex        =   0\r\n"
        + "      Text            =   \"Text1\"\r\n"
        + "      Top             =   120\r\n"
        + "      Width           =   2000\r\n"
        + "   End\r\n"
        + "End\r\n"
        + "Attribute VB_Name = \"Form1\"\r\n"
        + "Option Explicit\r\n"
        + "Private Sub L(ByVal s As String)\r\n"
        + "    Debug.Print s\r\n"
        + "End Sub\r\n"
        + "Private Sub S(ByVal tag As String, t As TextBox)\r\n"
        + "    L tag & \" text=[\" & Replace(Replace(t.Text, vbCrLf, \"<CRLF>\"), vbLf, \"<LF>\") & \"] start=\" & t.SelStart & \" len=\" & t.SelLength & \" sel=[\" & Replace(Replace(t.SelText, vbCrLf, \"<CRLF>\"), vbLf, \"<LF>\") & \"] err=\" & Err.Number\r\n"
        + "    Err.Clear\r\n"
        + "End Sub\r\n"
        + "Private Sub Form_Load()\r\n"
        + "    On Error Resume Next\r\n"
        + "    L \"types \" & TypeName(Text1.SelStart) & \" \" & TypeName(Text1.SelLength) & \" \" & TypeName(Text1.SelText)\r\n"
        + "    S \"design\", Text1\r\n"
        + "    Text1.Text = \"Hello world\"\r\n"
        + "    S \"after-text\", Text1\r\n"
        + "    Text1.SelStart = 6\r\n"
        + "    S \"start6\", Text1\r\n"
        + "    Text1.SelLength = 5\r\n"
        + "    S \"len5\", Text1\r\n"
        + "    Text1.SelText = \"there\"\r\n"
        + "    S \"seltext\", Text1\r\n"
        + "    Text1.SelStart = 2\r\n"
        + "    Text1.SelLength = 3\r\n"
        + "    Text1.SelStart = 4\r\n"
        + "    S \"start-resets-len\", Text1\r\n"
        + "    Text1.SelStart = 100\r\n"
        + "    S \"start100\", Text1\r\n"
        + "    Text1.SelStart = -1\r\n"
        + "    S \"start-1\", Text1\r\n"
        + "    Text1.SelStart = 6\r\n"
        + "    Text1.SelLength = 100\r\n"
        + "    S \"len100\", Text1\r\n"
        + "    Text1.SelLength = -1\r\n"
        + "    S \"len-1\", Text1\r\n"
        + "    Text1.SelStart = 0\r\n"
        + "    Text1.SelLength = 0\r\n"
        + "    Text1.SelText = \">\"\r\n"
        + "    S \"insert-at-0\", Text1\r\n"
        + "    Text1.SelStart = 3\r\n"
        + "    Text1.SelLength = 2\r\n"
        + "    Text1.Text = \"new\"\r\n"
        + "    S \"text-resets\", Text1\r\n"
        + "    Text1.SelStart = 1.6\r\n"
        + "    S \"start1.6\", Text1\r\n"
        + "    Text1.SelStart = \"2\"\r\n"
        + "    S \"start-str\", Text1\r\n"
        + "    Text1.SelText = Null\r\n"
        + "    S \"seltext-null\", Text1\r\n"
        + "    Text1.SelStart = Null\r\n"
        + "    S \"start-null\", Text1\r\n"
        + "    Text2.Text = \"ab\" & vbCrLf & \"cd\"\r\n"
        + "    S \"multi\", Text2\r\n"
        + "    Text2.SelStart = 3\r\n"
        + "    Text2.SelLength = 2\r\n"
        + "    S \"multi-start3\", Text2\r\n"
        + "    Text2.SelStart = 4\r\n"
        + "    Text2.SelLength = 1\r\n"
        + "    S \"multi-start4\", Text2\r\n"
        + "    Text2.SelStart = Len(Text2.Text)\r\n"
        + "    Text2.SelText = vbCrLf & \"ef\"\r\n"
        + "    S \"multi-append\", Text2\r\n"
        + "    Text3.Text = \"locked\"\r\n"
        + "    Text3.SelStart = 0\r\n"
        + "    Text3.SelLength = 1\r\n"
        + "    Text3.SelText = \"L\"\r\n"
        + "    S \"locked\", Text3\r\n"
        + "End Sub\r\n"
        + "\r\n";

    [AvaloniaFact]
    public void The_selection_reads_and_writes_as_vb6_exe_s_did()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddForm(new FormDeserializer().Deserialize(project, Probe, new NullSink())!);
        var printed = new List<string>();
        Action<string> print = printed.Add;
        VBDebugConsole.Output += print;
        var cancel = new CancellationTokenSource();
        try
        {
            _ = VBLoader.RunForm(project.StartupForm!, cancel.Token, out _);
            for (var i = 0; i < 20; i++)
                Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            cancel.Cancel();
            Dispatcher.UIThread.RunJobs();
            VBDebugConsole.Output -= print;
        }

        // vb6.exe's MaxLength row is left out: HexIDE's TextBox doesn't model MaxLength yet.
        printed.Should().Equal(
            "types Long Long String",
            "design text=[Text1] start=0 len=0 sel=[] err=0",
            "after-text text=[Hello world] start=0 len=0 sel=[] err=0",
            "start6 text=[Hello world] start=6 len=0 sel=[] err=0",
            "len5 text=[Hello world] start=6 len=5 sel=[world] err=0",
            "seltext text=[Hello there] start=11 len=0 sel=[] err=0",
            "start-resets-len text=[Hello there] start=4 len=0 sel=[] err=0",
            "start100 text=[Hello there] start=11 len=0 sel=[] err=0",
            "start-1 text=[Hello there] start=11 len=0 sel=[] err=380",
            "len100 text=[Hello there] start=6 len=5 sel=[there] err=0",
            "len-1 text=[Hello there] start=6 len=5 sel=[there] err=380",
            "insert-at-0 text=[>Hello there] start=1 len=0 sel=[] err=0",
            "text-resets text=[new] start=0 len=0 sel=[] err=0",
            "start1.6 text=[new] start=2 len=0 sel=[] err=0",
            "start-str text=[new] start=2 len=0 sel=[] err=0",
            "seltext-null text=[new] start=2 len=0 sel=[] err=94",
            "start-null text=[new] start=2 len=0 sel=[] err=94",
            "multi text=[ab<CRLF>cd] start=0 len=0 sel=[] err=0",
            "multi-start3 text=[ab<CRLF>cd] start=3 len=2 sel=[<LF>c] err=0",
            "multi-start4 text=[ab<CRLF>cd] start=4 len=1 sel=[c] err=0",
            "multi-append text=[ab<CRLF>cd<CRLF>ef] start=10 len=0 sel=[] err=0",
            "locked text=[Locked] start=1 len=0 sel=[] err=0");
    }
}
