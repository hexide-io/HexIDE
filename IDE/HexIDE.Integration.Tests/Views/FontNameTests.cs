using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// FontName at run time (#1141). Every expected line is what vb6.exe printed for the same statements, native and p-code
/// alike: the probes and their logs are in docs/vb6-fidelity-oracle/the-fonts-a-form-names.md, "The fonts a form names". Where a family isn't
/// installed here, as Wingdings isn't on Linux, the test expects VB6's answer for a machine without it, 380.
/// </summary>
public sealed class FontNameTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "hexide-fontname-" + Guid.NewGuid().ToString("N"));

    public FontNameTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static readonly bool HasWingdings = VBFontExtensions.IsInstalled("Wingdings");

    private const string FormHead = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "FontName probe"
           ClientHeight    =   3000
           ClientLeft      =   60
           ClientTop       =   450
           ClientWidth     =   4500
           ScaleHeight     =   3000
           ScaleWidth      =   4500
           Begin VB.CommandButton Command1
              Caption         =   "PMJ"
              Height          =   500
              Left            =   120
              TabIndex        =   1
              Top             =   1200
              Width           =   1500
           End
           Begin VB.Label Label2
              Caption         =   "two"
              Height          =   300
              Left            =   120
              TabIndex        =   2
              Top             =   2000
              Width           =   1500
           End
           Begin VB.Label Label1
              Caption         =   "PMJ"
              BeginProperty Font
                 Name            =   "Arial"
                 Size            =   12
                 Charset         =   0
                 Weight          =   700
                 Underline       =   0   'False
                 Italic          =   0   'False
                 Strikethrough   =   0   'False
              EndProperty
              Height          =   600
              Left            =   120
              TabIndex        =   0
              Top             =   120
              Width           =   3000
           End
        End
        Attribute VB_Name = "Form1"
        Attribute VB_GlobalNameSpace = False
        Attribute VB_Creatable = False
        Attribute VB_PredeclaredId = True
        Attribute VB_Exposed = False
        Option Explicit

        """;

    private async Task<(List<string> Printed, List<Exception> Errors, Window Window)> RunAsync(string load, CancellationToken token, string? formHead = null)
    {
        var frm = (formHead ?? FormHead) + "Private Sub Form_Load()\r\n" + load + "\r\nEnd Sub\r\n";
        File.WriteAllText(Path.Combine(dir, "Form1.frm"), frm.ReplaceLineEndings("\r\n"), Encoding.ASCII);
        File.WriteAllText(Path.Combine(dir, "Probe.vbp"), "Type=Exe\r\nForm=Form1.frm\r\nStartup=\"Form1\"\r\nName=\"Probe\"\r\n", Encoding.ASCII);
        var project = (await new ProjectLoader().LoadAsync(Path.Combine(dir, "Probe.vbp"), new NullSink())).Project;
        var printed = new List<string>();
        var errors = new List<Exception>();
        Action<string> print = printed.Add;
        void OnFaulted(VBWindowContext _, Exception e) => errors.Add(e);
        VBDebugConsole.Output += print;
        VBWindowContext.Faulted += OnFaulted;
        try
        {
            _ = VBLoader.RunMadeProgram(project, token, out var startup);
            for (var i = 0; i < 20; i++)
                Dispatcher.UIThread.RunJobs();
            return (printed, errors, startup!);
        }
        finally
        {
            VBDebugConsole.Output -= print;
            VBWindowContext.Faulted -= OnFaulted;
        }
    }

    private static Control Named(Window window, string name) =>
        window.GetLogicalDescendants().OfType<Control>().First(c => VBProps.GetName(c) == name);

    private async Task Check(string load, Action<List<string>, Window> assert, string? formHead = null)
    {
        using var cancel = new CancellationTokenSource();
        Window? window = null;
        try
        {
            (var printed, var errors, window) = await RunAsync(load, cancel.Token, formHead);
            errors.Should().BeEmpty();
            assert(printed, window);
        }
        finally
        {
            cancel.Cancel();
            window?.Close();
        }
    }

    [AvaloniaFact]
    public Task Missing_Font_blocks_read_the_forms_font_through_runtime_members() => Check("""
            Debug.Print Command1.FontName & " " & Command1.FontSize
            Debug.Print Label2.FontName & " " & Label2.FontSize
            Debug.Print Label1.FontName & " " & Label1.FontSize & " " & Label1.FontBold
            Debug.Print Command2.FontName & " " & Command2.FontSize
            Debug.Print Frame2.FontName & " " & Frame2.FontSize
            Debug.Print Command4.FontName & " " & Command4.FontSize
            Debug.Print Command3.FontName & " " & Command3.FontSize
            Debug.Print Picture1.FontName & " " & Picture1.FontSize & " " & Picture1.FontBold
            Debug.Print Frame1.FontName & " " & Frame1.FontSize & " " & Frame1.FontBold
            Me.FontName = "Tahoma"
            Debug.Print Command1.FontName & " " & Command1.FontSize
            Command1.FontName = "Tahoma"
            Debug.Print Label2.FontName & " " & Label2.FontSize
            """, (printed, _) => printed.Should().Equal(
                "Arial 12", "Arial 12", "Arial 12 True", "Arial 12", "Arial 12", "Arial 12", "Arial 12",
                "Courier New 9.75 False", "Times New Roman 9.75 True", "Arial 12", "Arial 12"),
            FormHead.Replace("Begin VB.Form Form1", """
                Begin VB.Form Form1
                   BeginProperty Font
                      Name = "Arial"
                      Size = 12
                      Weight = 400
                      Italic = 0
                   EndProperty
                """).Replace("Begin VB.CommandButton Command1", """
                Begin VB.PictureBox Picture1
                   BeginProperty Font
                      Name = "Courier New"
                      Size = 9.75
                      Weight = 400
                      Italic = 0
                   EndProperty
                   Begin VB.CommandButton Command2
                      Caption = "Command2"
                   End
                   Begin VB.Frame Frame2
                      Caption = "Frame2"
                      Begin VB.CommandButton Command4
                         Caption = "Command4"
                      End
                   End
                End
                Begin VB.Frame Frame1
                   BeginProperty Font
                      Name = "Times New Roman"
                      Size = 9.75
                      Weight = 700
                      Italic = 0
                   EndProperty
                   Begin VB.CommandButton Command3
                      Caption = "Command3"
                   End
                End
                Begin VB.CommandButton Command1
                """));

    [AvaloniaFact]
    public void A_face_FontName_takes_is_one_set_control_property_takes_whatever_its_name_ends_in()
    {
        // The predicate set_control_property passes PropertyText, so its Font text takes Wingdings 2 and MS Shell Dlg 2
        // as faces, not as a face and a size typed without their comma (#1311). MS Shell Dlg 2 is a Windows name vb6.exe
        // takes, installed or not; Arial 12 is no family at all.
        VBFontExtensions.IsNameable("MS Shell Dlg 2").Should().BeTrue();
        VBFontExtensions.IsNameable("ms shell dlg 2").Should().BeTrue("FontName takes a name without regard to case");
        VBFontExtensions.IsNameable("Arial 12").Should().BeFalse();
        VBFontExtensions.IsNameable("").Should().BeFalse();
        VBFontExtensions.IsNameable("Wingdings 2").Should().Be(VBFontExtensions.IsInstalled("Wingdings 2"));
    }

    [AvaloniaFact]
    public Task FontName_reads_and_sets_as_vb6_exe_did() => Check("""
            On Error Resume Next
            Debug.Print Label1.FontName & " " & TypeName(Label1.FontName)
            Label1.FontName = "arial"
            Debug.Print Err.Number & " " & Label1.FontName: Err.Clear
            Label1.FontName = "MS Sans Serif"
            Debug.Print Err.Number & " " & Label1.FontName: Err.Clear
            Label1.FontName = "Helvetica"
            Debug.Print Err.Number & " " & Label1.FontName: Err.Clear
            Label1.FontName = "Courier New"
            Debug.Print Err.Number & " " & Label1.FontName: Err.Clear
            Command1.FontName = "Tahoma"
            Debug.Print Err.Number & " " & Command1.FontName: Err.Clear
            """, (printed, _) => printed.Should().Equal(
                "Arial String", "0 Arial", "0 MS Sans Serif", "0 Helvetica", "0 Courier New", "0 Tahoma"));

    [AvaloniaFact]
    public Task A_family_the_machine_lacks_is_380_and_the_font_stays() => Check("""
            On Error Resume Next
            Label1.FontName = "Tahoma"
            Label1.FontName = "NoSuchFont123"
            Debug.Print Err.Number & " " & Err.Description & " " & Label1.FontName: Err.Clear
            Label1.FontName = ""
            Debug.Print Err.Number & " " & Label1.FontName: Err.Clear
            Label1.FontName = "  Arial  "
            Debug.Print Err.Number & " " & Label1.FontName: Err.Clear
            Label1.FontName = 5
            Debug.Print Err.Number & " " & Label1.FontName: Err.Clear
            Label1.FontName = Null
            Debug.Print Err.Number & " " & Label1.FontName: Err.Clear
            """, (printed, _) => printed.Should().Equal(
                "380 Invalid property value Tahoma", "380 Tahoma", "380 Tahoma", "380 Tahoma", "94 Tahoma"));

    [AvaloniaFact]
    public Task The_charset_is_the_familys_own_and_the_size_and_weight_stay() => Check("""
            Label1.FontName = "Terminal"
            """, (_, window) =>
        {
            var font = AttachedProperties.GetFont(Named(window, "Label1"))!.Value;
            font.FontFamilyName.Should().Be("Terminal");
            font.Charset.Should().Be(255);
            font.Size.Should().Be(12);
            font.Bold.Should().BeTrue();
        });

    [AvaloniaFact]
    public Task Wingdings_by_name_draws_its_glyphs_where_it_is_installed_and_is_380_where_it_is_not() => Check("""
            On Error Resume Next
            Label1.FontName = "Wingdings"
            Debug.Print Err.Number & " " & Label1.FontName & " " & Label1.Caption
            """, (printed, window) =>
        {
            var label = Named(window, "Label1");
            var drawn = label.GetVisualDescendants().OfType<TextBlock>().Single().Text;
            if (HasWingdings)
            {
                printed.Should().Equal("0 Wingdings PMJ");
                AttachedProperties.GetFont(label)!.Value.Charset.Should().Be(SymbolFontText.SymbolCharset);
                drawn.Should().Be(new string(new[] { (char)0xF050, (char)0xF04D, (char)0xF04A }));
            }
            else
            {
                // VB6's answer on a machine without the font, which a program can trap and fall back from.
                printed.Should().Equal("380 Arial PMJ");
                drawn.Should().Be("PMJ");
            }
        });

    [AvaloniaFact]
    public Task A_Wingdings_font_set_back_to_Arial_draws_letters_again() => Check("""
            On Error Resume Next
            Label1.FontName = "Wingdings"
            Err.Clear
            Label1.FontName = "Arial"
            Debug.Print Err.Number & " " & Label1.FontName
            """, (printed, window) =>
        {
            printed.Should().Equal("0 Arial");
            var label = Named(window, "Label1");
            AttachedProperties.GetFont(label)!.Value.Charset.Should().Be(0);
            label.GetVisualDescendants().OfType<TextBlock>().Single().Text.Should().Be("PMJ");
        });

    [AvaloniaFact]
    public Task The_forms_FontName_changes_its_own_font_and_none_of_its_controls() => Check("""
            Me.FontName = "Courier New"
            Debug.Print Me.FontName & " " & Label2.FontName
            """, (printed, _) => printed.Should().Equal("Courier New MS Sans Serif"));
}
