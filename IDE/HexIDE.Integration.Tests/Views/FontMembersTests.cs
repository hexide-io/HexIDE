using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A control's and a form's FontSize, FontBold, FontItalic, FontUnderline and FontStrikethru at run time (#1343), and a
/// TextBox's own Font (#915). The form is vb6.exe's probe, and every line it prints is a line vb6.exe logged for the same
/// statements, native and p-code alike (the oracle's "A font's FontSize, FontBold and the rest"). The probe's
/// <c>Font.Weight</c> and <c>Font.Size</c> are left out of each line: the Font object is #934.
/// </summary>
public class FontMembersTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static string Font(string name, string size, int weight = 400, bool italic = false) =>
        "      BeginProperty Font\r\n"
        + $"         Name            =   \"{name}\"\r\n"
        + $"         Size            =   {size}\r\n"
        + "         Charset         =   0\r\n"
        + $"         Weight          =   {weight}\r\n"
        + "         Underline       =   0   'False\r\n"
        + (italic ? "         Italic          =   -1  'True\r\n" : "         Italic          =   0   'False\r\n")
        + "         Strikethrough   =   0   'False\r\n"
        + "      EndProperty\r\n";

    private static string Label(string name, int top, string font = "") =>
        $"   Begin VB.Label {name}\r\n"
        + "      Caption         =   \"a\"\r\n"
        + font
        + "      Height          =   375\r\n"
        + "      Left            =   120\r\n"
        + $"      Top             =   {top}\r\n"
        + "      Width           =   2000\r\n"
        + "   End\r\n";

    private static string Probe(string code) =>
        "VERSION 5.00\r\n"
        + "Begin VB.Form Form1\r\n"
        + "   Caption         =   \"ftprobe\"\r\n"
        + "   ClientHeight    =   3000\r\n"
        + "   ClientLeft      =   60\r\n"
        + "   ClientTop       =   450\r\n"
        + "   ClientWidth     =   4500\r\n"
        + "   ScaleHeight     =   3000\r\n"
        + "   ScaleWidth      =   4500\r\n"
        + "   Begin VB.TextBox Text1\r\n"
        + Font("Arial", "9.75")
        + "      Height          =   495\r\n"
        + "      Left            =   120\r\n"
        + "      TabIndex        =   0\r\n"
        + "      Text            =   \"x\"\r\n"
        + "      Top             =   120\r\n"
        + "      Width           =   2000\r\n"
        + "   End\r\n"
        + Label("Label1", 720)
        + Label("Label2", 1200, Font("Tahoma", "12", 700, italic: true))
        + Label("Label3", 1600, Font("Arial", "10"))
        + Label("Label4", 2000, Font("MS Sans Serif", "10"))
        + Label("Label5", 2400, Font("Arial", "0.1"))
        + Label("Label6", 2800, Font("Arial", "12", 600))
        + "End\r\n"
        + "Attribute VB_Name = \"Form1\"\r\n"
        + "Option Explicit\r\n"
        + "Private Function D(c As Object) As String\r\n"
        + "    D = \"[\" & c.FontName & \" \" & c.FontSize & \" b=\" & c.FontBold & \" i=\" & c.FontItalic & \" u=\" & c.FontUnderline _\r\n"
        + "        & \" s=\" & c.FontStrikethru & \"]\"\r\n"
        + "End Function\r\n"
        + "Private Sub Sizes(c As Object, ByVal tag As String, values As Variant)\r\n"
        + "    Dim v As Variant\r\n"
        + "    On Error Resume Next\r\n"
        + "    For Each v In values\r\n"
        + "        c.FontSize = v\r\n"
        + "        Debug.Print tag & \" FontSize=\" & TypeName(v) & \" \" & v & \" -> \" & c.FontSize & \" err=\" & Err.Number: Err.Clear\r\n"
        + "    Next v\r\n"
        + "End Sub\r\n"
        + "Private Sub Form_Load()\r\n"
        + "    Dim v As Variant\r\n"
        + "    On Error Resume Next\r\n"
        // The code comes from raw strings, whose lines end as the checkout ends them, and a .frm's end in CR-LF.
        + code.ReplaceLineEndings("\r\n")
        + "End Sub\r\n"
        + "\r\n";

    private static List<string> Run(string code, Action<Dictionary<string, TemplatedControl>>? after = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddForm(new FormDeserializer().Deserialize(project, Probe(code), new NullSink())!);
        var printed = new List<string>();
        Action<string> print = printed.Add;
        VBDebugConsole.Output += print;
        var cancel = new CancellationTokenSource();
        try
        {
            _ = VBLoader.RunForm(project.StartupForm!, cancel.Token, out var window);
            for (var i = 0; i < 20; i++)
                Dispatcher.UIThread.RunJobs();
            after?.Invoke(window.GetLogicalDescendants().OfType<TemplatedControl>()
                .Where(c => VBProps.GetName(c) is not null).ToDictionary(c => VBProps.GetName(c)!));
        }
        finally
        {
            cancel.Cancel();
            Dispatcher.UIThread.RunJobs();
            VBDebugConsole.Output -= print;
        }
        return printed;
    }

    [AvaloniaFact]
    public void The_font_members_read_with_vb6_exe_s_types_and_a_file_s_sizes_as_it_gave_them()
    {
        Run("""
                Debug.Print "types " & TypeName(Text1.FontName) & " " & TypeName(Text1.FontSize) & " " & TypeName(Text1.FontBold) & " " _
                    & TypeName(Text1.FontItalic) & " " & TypeName(Text1.FontUnderline) & " " & TypeName(Text1.FontStrikethru)
                Debug.Print "design Text1 " & D(Text1) & " Label1 " & D(Label1) & " Label2 " & D(Label2) & " Form " & D(Me)
                Debug.Print "design Label3 Arial 10 -> " & Label3.FontSize & "; Label4 MS Sans Serif 10 -> " & Label4.FontSize _
                    & "; Label5 Arial 0.1 -> " & Label5.FontSize & "; Label6 weight 600 bold=" & Label6.FontBold

            """).Should().Equal(
            "types String Single Boolean Boolean Boolean Boolean",
            "design Text1 [Arial 9.75 b=False i=False u=False s=False] Label1 [MS Sans Serif 8.25 b=False i=False u=False s=False]"
            + " Label2 [Tahoma 12 b=True i=True u=False s=False] Form [MS Sans Serif 8.25 b=False i=False u=False s=False]",
            "design Label3 Arial 10 -> 9.75; Label4 MS Sans Serif 10 -> 9.75; Label5 Arial 0.1 -> 1.5; Label6 weight 600 bold=True");
    }

    [AvaloniaFact]
    public void FontSize_takes_the_size_vb6_exe_gave_and_refuses_what_it_refused()
    {
        // The probe's second TrueType face was Lucida Console. Linux has none, and FontName refuses a face the machine
        // lacks unless it is one of the Windows fonts it always takes, so this uses Courier New, which is, and which the
        // every-height run measured exactly as it measured Lucida Console.
        Run("""
                Sizes Text1, "Text1/Arial", Array(8, 9, 10, 11, 12, 13, 14, 16, 7.5, 9.7, 36, 0.5, 1, 9.375, 11.625, 0.1, 0, -1, 2000, 2048, 2160, 2161, 1E+30, "12", "abc", Null, True)
                Text1.FontName = "Courier New"
                Sizes Text1, "Text1/Courier", Array(10, 13, 9.375)
                Sizes Label1, "Label1/MSSansSerif", Array(9, 10, 11, 12, 13, 14, 16, 7.5, 36, 0.5)
                Sizes Me, "Form/MSSansSerif", Array(9, 13)

            """).Should().Equal(
            "Text1/Arial FontSize=Integer 8 -> 8.25 err=0",
            "Text1/Arial FontSize=Integer 9 -> 9 err=0",
            "Text1/Arial FontSize=Integer 10 -> 9.75 err=0",
            "Text1/Arial FontSize=Integer 11 -> 11.25 err=0",
            "Text1/Arial FontSize=Integer 12 -> 12 err=0",
            "Text1/Arial FontSize=Integer 13 -> 12.75 err=0",
            "Text1/Arial FontSize=Integer 14 -> 14.25 err=0",
            "Text1/Arial FontSize=Integer 16 -> 15.75 err=0",
            "Text1/Arial FontSize=Double 7.5 -> 7.5 err=0",
            "Text1/Arial FontSize=Double 9.7 -> 9.75 err=0",
            "Text1/Arial FontSize=Integer 36 -> 36 err=0",
            "Text1/Arial FontSize=Double 0.5 -> 1.5 err=0",
            "Text1/Arial FontSize=Integer 1 -> 1.5 err=0",
            "Text1/Arial FontSize=Double 9.375 -> 9.75 err=0",
            "Text1/Arial FontSize=Double 11.625 -> 12 err=0",
            "Text1/Arial FontSize=Double 0.1 -> 1.5 err=0",
            "Text1/Arial FontSize=Integer 0 -> 1.5 err=380",
            "Text1/Arial FontSize=Integer -1 -> 1.5 err=380",
            "Text1/Arial FontSize=Integer 2000 -> 2000.25 err=0",
            "Text1/Arial FontSize=Integer 2048 -> 2048.25 err=0",
            "Text1/Arial FontSize=Integer 2160 -> 2160 err=0",
            "Text1/Arial FontSize=Integer 2161 -> 2160 err=6",
            "Text1/Arial FontSize=Double 1E+30 -> 2160 err=6",
            "Text1/Arial FontSize=String 12 -> 12 err=0",
            "Text1/Arial FontSize=String abc -> 12 err=13",
            "Text1/Arial FontSize=Null  -> 12 err=13",
            "Text1/Arial FontSize=Boolean True -> 12 err=380",
            "Text1/Courier FontSize=Integer 10 -> 9.75 err=0",
            "Text1/Courier FontSize=Integer 13 -> 12.75 err=0",
            "Text1/Courier FontSize=Double 9.375 -> 9.75 err=0",
            "Label1/MSSansSerif FontSize=Integer 9 -> 8.25 err=0",
            "Label1/MSSansSerif FontSize=Integer 10 -> 9.75 err=0",
            "Label1/MSSansSerif FontSize=Integer 11 -> 9.75 err=0",
            "Label1/MSSansSerif FontSize=Integer 12 -> 12 err=0",
            "Label1/MSSansSerif FontSize=Integer 13 -> 12 err=0",
            "Label1/MSSansSerif FontSize=Integer 14 -> 13.5 err=0",
            "Label1/MSSansSerif FontSize=Integer 16 -> 15 err=0",
            "Label1/MSSansSerif FontSize=Double 7.5 -> 8.25 err=0",
            "Label1/MSSansSerif FontSize=Integer 36 -> 36 err=0",
            "Label1/MSSansSerif FontSize=Double 0.5 -> 8.25 err=0",
            "Form/MSSansSerif FontSize=Integer 9 -> 8.25 err=0",
            "Form/MSSansSerif FontSize=Integer 13 -> 12 err=0");
    }

    [AvaloniaFact]
    public void The_Boolean_members_coerce_as_vb6_exe_s_did_and_each_keeps_the_rest_of_the_font()
    {
        Run("""
                Text1.FontName = "Arial": Text1.FontSize = 12
                For Each v In Array(True, False, 1, 0, -1, 2, "True", "x", Null, 0.4)
                    Text1.FontBold = v
                    Debug.Print "FontBold=" & TypeName(v) & " " & v & " -> " & Text1.FontBold & " err=" & Err.Number: Err.Clear
                Next v
                For Each v In Array(True, False, 1, "x", Null)
                    Text1.FontItalic = v
                    Debug.Print "FontItalic=" & TypeName(v) & " " & v & " -> " & Text1.FontItalic & " err=" & Err.Number: Err.Clear
                Next v
                For Each v In Array(True, False, 1, 0, "True", "x", Null, 0.4)
                    Label1.FontUnderline = v
                    Debug.Print "FontUnderline=" & TypeName(v) & " " & v & " -> " & Label1.FontUnderline & " err=" & Err.Number: Err.Clear
                    Label1.FontStrikethru = v
                    Debug.Print "FontStrikethru=" & TypeName(v) & " " & v & " -> " & Label1.FontStrikethru & " err=" & Err.Number: Err.Clear
                Next v
                Text1.FontBold = True: Text1.FontItalic = True: Text1.FontUnderline = True: Text1.FontStrikethru = True
                Debug.Print "all on " & D(Text1)
                Text1.FontName = "Courier New"
                Debug.Print "after FontName " & D(Text1)
                Text1.FontSize = 20
                Debug.Print "after FontSize " & D(Text1)
                Text1.FontBold = False
                Debug.Print "after FontBold=False " & D(Text1)
                Me.FontSize = 13
                Debug.Print "form 13 " & D(Me) & " Text1 " & D(Text1)

            """).Should().Equal(
            "FontBold=Boolean True -> True err=0",
            "FontBold=Boolean False -> False err=0",
            "FontBold=Integer 1 -> True err=0",
            "FontBold=Integer 0 -> False err=0",
            "FontBold=Integer -1 -> True err=0",
            "FontBold=Integer 2 -> True err=0",
            "FontBold=String True -> True err=0",
            "FontBold=String x -> True err=13",
            "FontBold=Null  -> True err=94",
            "FontBold=Double 0.4 -> True err=0",
            "FontItalic=Boolean True -> True err=0",
            "FontItalic=Boolean False -> False err=0",
            "FontItalic=Integer 1 -> True err=0",
            "FontItalic=String x -> True err=13",
            "FontItalic=Null  -> True err=94",
            "FontUnderline=Boolean True -> True err=0",
            "FontStrikethru=Boolean True -> True err=0",
            "FontUnderline=Boolean False -> False err=0",
            "FontStrikethru=Boolean False -> False err=0",
            "FontUnderline=Integer 1 -> True err=0",
            "FontStrikethru=Integer 1 -> True err=0",
            "FontUnderline=Integer 0 -> False err=0",
            "FontStrikethru=Integer 0 -> False err=0",
            "FontUnderline=String True -> True err=0",
            "FontStrikethru=String True -> True err=0",
            "FontUnderline=String x -> True err=13",
            "FontStrikethru=String x -> True err=13",
            "FontUnderline=Null  -> True err=94",
            "FontStrikethru=Null  -> True err=94",
            "FontUnderline=Double 0.4 -> True err=0",
            "FontStrikethru=Double 0.4 -> True err=0",
            "all on [Arial 12 b=True i=True u=True s=True]",
            "after FontName [Courier New 12 b=True i=True u=True s=True]",
            "after FontSize [Courier New 20.25 b=True i=True u=True s=True]",
            "after FontBold=False [Courier New 20.25 b=False i=True u=True s=True]",
            "form 13 [MS Sans Serif 12 b=False i=False u=False s=False] Text1 [Courier New 20.25 b=False i=True u=True s=True]");
    }

    [AvaloniaFact]
    public void A_change_of_family_keeps_the_size_as_it_reads_and_a_bitmap_font_gives_way_to_Arial()
    {
        Run("""
                Label1.FontName = "Arial": Label1.FontSize = 9
                Debug.Print "Arial 9 -> " & Label1.FontSize
                Label1.FontName = "MS Sans Serif"
                Debug.Print "then MS Sans Serif -> " & Label1.FontSize
                Label1.FontName = "Arial"
                Debug.Print "then Arial -> " & Label1.FontSize
                Label1.FontName = "System"
                Debug.Print "then System -> " & Label1.FontSize
                Label1.FontName = "Arial"
                Debug.Print "then Arial -> " & Label1.FontSize
                Label1.FontName = "MS Sans Serif"
                Label1.FontSize = 300
                Debug.Print "MS Sans Serif 300 -> " & Label1.FontName & " " & Label1.FontSize
                Label1.FontSize = 9
                Debug.Print "then 9 -> " & Label1.FontName & " " & Label1.FontSize
                Label1.FontName = "MS Sans Serif"
                Debug.Print "then MS Sans Serif -> " & Label1.FontName & " " & Label1.FontSize

            """).Should().Equal(
            "Arial 9 -> 9",
            "then MS Sans Serif -> 8.25",
            "then Arial -> 8.25",
            "then System -> 9.75",
            "then Arial -> 9.75",
            "MS Sans Serif 300 -> Arial 300",
            "then 9 -> Arial 9",
            "then MS Sans Serif -> MS Sans Serif 8.25");
    }

    [AvaloniaFact]
    public void A_TextBox_draws_in_its_own_font_and_a_change_from_code_redraws_it()
    {
        Run("""
                Text1.FontSize = 12
                Text1.FontBold = True

            """, controls =>
        {
            var box = controls["Text1"];
            box.FontFamily.Name.Should().Be("Arial");
            // 12 points draw at 16 pixels (#910).
            box.FontSize.Should().Be(16);
            box.FontWeight.Should().Be(Avalonia.Media.FontWeight.Bold);
        });
    }

    [AvaloniaFact]
    public void Every_family_s_table_answers_at_the_heights_where_vb6_exe_s_changed()
    {
        // Spot checks against the measured tables: the edges of runs, and where a bitmap font gives way to Arial.
        static (double, string) Snap(string family, double pixels) => VbFontSizes.Snap(family, pixels * 0.75);

        Snap("Arial", 1).Should().Be((1.5, "Arial"));
        Snap("Modern", 1).Should().Be((0.75, "Modern"));
        Snap("MS Sans Serif", 12).Should().Be((8.25, "MS Sans Serif"));
        Snap("MS Sans Serif", 78).Should().Be((60, "MS Sans Serif"));
        Snap("MS Sans Serif", 385).Should().Be((240, "MS Sans Serif"));
        Snap("MS Sans Serif", 386).Should().Be((289.5, "Arial"));
        Snap("System", 22).Should().Be((9.75, "System"));
        Snap("System", 23).Should().Be((19.5, "System"));
        Snap("System", 400).Should().Be((78, "System"));
        Snap("Terminal", 209).Should().Be((108, "Terminal"));
        Snap("Terminal", 210).Should().Be((157.5, "Arial"));
        Snap("Fixedsys", 161).Should().Be((72, "Fixedsys"));
        Snap("Fixedsys", 162).Should().Be((121.5, "Arial"));
        Snap("Courier", 94).Should().Be((75, "Courier"));
        Snap("Small Fonts", 154).Should().Be((115.5, "Arial"));
        Snap("MS Shell Dlg", 9).Should().Be((8.25, "MS Shell Dlg"));
        Snap("MS Shell Dlg", 16).Should().Be((12, "MS Shell Dlg"));
    }
}
