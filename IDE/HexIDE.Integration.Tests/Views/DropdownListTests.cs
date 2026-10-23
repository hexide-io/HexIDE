using System.Collections.Generic;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A ComboBox's Style, and a Dropdown List's Text (#1428). Every expectation is vb6.exe's, native and p-code agreeing: the
/// Style 2 column of the oracle's "A ComboBox's Text, at each Style", and its second probe of a Dropdown List's Text from
/// code. Combo1 is a Dropdown List saved with a Text, which VB6 ignores; Combo2 a Simple Combo; Combo3 a Dropdown Combo.
/// </summary>
public class DropdownListTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Controls = """
           Begin VB.ComboBox Combo3
              Height          =   315
              Left            =   120
              TabIndex        =   2
              Text            =   "Combo3"
              Top             =   1320
              Width           =   2000
           End
           Begin VB.ComboBox Combo2
              Height          =   900
              Left            =   120
              Style           =   1  'Simple Combo
              TabIndex        =   1
              Text            =   "Combo2"
              Top             =   360
              Width           =   2000
           End
           Begin VB.ComboBox Combo1
              Height          =   315
              Left            =   120
              Style           =   2  'Dropdown List
              TabIndex        =   0
              Text            =   "x"
              Top             =   0
              Width           =   2000
           End
        """;

    private sealed record Ran(List<string> Printed, List<System.Exception> Errors);

    /// <summary>Run Form1 with <paramref name="load"/> as its Load, every Click and Change on Combo1 printed, then close it.</summary>
    private static Ran Run(string load)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var frm = ("VERSION 5.00\nBegin VB.Form Form1\n" + Controls + "\nEnd\nAttribute VB_Name = \"Form1\"\n"
                   + "Private Sub Combo1_Click()\n    Debug.Print \"Click \" & Combo1.ListIndex\nEnd Sub\n"
                   + "Private Sub Combo1_Change()\n    Debug.Print \"Change\"\nEnd Sub\n"
                   + "Private Sub Form_Load()\n" + load + "\nEnd Sub\n").ReplaceLineEndings("\r\n");
        var form = new FormDeserializer().Deserialize(project, frm, new NullSink())!;
        project.AddForm(form);

        var ran = new Ran([], []);
        void OnFaulted(VBWindowContext _, System.Exception e) => ran.Errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => ran.Errors.Add(e);
        void OnCompileError(VBWindowContext _, VBCompileErrorException e) => ran.Errors.Add(e);
        VBDebugConsole.Output += ran.Printed.Add;
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        VBWindowContext.CompileError += OnCompileError;
        try
        {
            VBLoader.RunForm(form, CancellationToken.None, out var window);
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
        finally
        {
            VBDebugConsole.Output -= ran.Printed.Add;
            VBWindowContext.Faulted -= OnFaulted;
            VBWindowContext.RunTimeError -= OnRunTimeError;
            VBWindowContext.CompileError -= OnCompileError;
        }
        return ran;
    }

    private const string Fill = "Combo1.AddItem \"apple\": Combo1.AddItem \"Banana\": Combo1.AddItem \"cherry\"\n";

    // Each statement, then the Text and ListIndex it left, and the error it raised.
    private static string Row(string statement) =>
        statement + "\nDebug.Print \"[\" & Combo1.Text & \"] \" & Combo1.ListIndex & \" \" & Err.Number: Err.Clear\n";

    [AvaloniaFact]
    public void Style_reads_as_an_Integer_and_a_Dropdown_List_ignores_its_saved_Text()
    {
        var ran = Run("Debug.Print Combo1.Style\nDebug.Print Combo2.Style\nDebug.Print Combo3.Style\n"
                      + "Debug.Print TypeName(Combo1.Style)\nDebug.Print \"[\" & Combo1.Text & \"] \" & TypeName(Combo1.Text)\n");

        ran.Errors.Should().BeEmpty();
        // Debug.Print writes a number with a space either side.
        ran.Printed.Should().Equal(" 2 ", " 1 ", " 0 ", "Integer", "[] String");
    }

    [AvaloniaTheory]
    [InlineData("Combo1.Text = \"Banana\"", new[] { "Click 1", "[Banana] 1 0" })]
    [InlineData("Combo1.Text = \"banana\"", new[] { "Click 1", "[Banana] 1 0" })]
    [InlineData("Combo1.Text = \"Ban\"", new[] { "[] -1 383" })]
    [InlineData("Combo1.Text = \"zz\"", new[] { "[] -1 383" })]
    [InlineData("Combo1.Text = \"\"", new[] { "[] -1 383" })]
    [InlineData("Combo1.Text = \"cherry\"", new[] { "Click 2", "[cherry] 2 0" })]
    [InlineData("Combo1.ListIndex = 0", new[] { "Click 0", "[apple] 0 0" })]
    [InlineData("Combo1.Text = Null", new[] { "[] -1 13" })]
    [InlineData("Combo1.Text = 5", new[] { "[] -1 383" })]
    public void A_Dropdown_List_s_Text_selects_the_item_it_names_or_is_383(string statement, string[] expected)
    {
        var ran = Run("On Error Resume Next\n" + Fill + Row(statement));

        ran.Errors.Should().BeEmpty();
        ran.Printed.Should().Equal(expected);
    }

    // Click every time, the item already selected too; no match changes nothing, the selection included.
    [AvaloniaFact]
    public void Its_Text_raises_Click_every_time_and_a_miss_keeps_the_selection()
    {
        var ran = Run("On Error Resume Next\n" + Fill + Row("Combo1.Text = \"Banana\"") + Row("Combo1.Text = \"BANANA\"")
                      + Row("Combo1.Text = \"zz\"") + Row("Combo1.ListIndex = -1"));

        ran.Printed.Should().Equal("Click 1", "[Banana] 1 0", "Click 1", "[Banana] 1 0", "[Banana] 1 383", "Click -1", "[] -1 0");
    }

    // The Text is made a String as CStr makes it, then matched (the second probe).
    [AvaloniaFact]
    public void A_number_or_a_Boolean_is_matched_as_CStr_makes_it()
    {
        var ran = Run("On Error Resume Next\n"
                      + "Combo1.AddItem \"5\": Combo1.AddItem \"apple\": Combo1.AddItem \"10\": Combo1.AddItem \"True\"\n"
                      + Row("Combo1.Text = 5") + Row("Combo1.Text = 10#") + Row("Combo1.Text = True")
                      + Row("Combo1.Text = \"APPLE\"") + Row("Combo1.Text = \"5 \"") + Row("Combo1.Text = Empty"));

        ran.Printed.Should().Equal("Click 0", "[5] 0 0", "Click 2", "[10] 2 0", "Click 3", "[True] 3 0",
            "Click 1", "[apple] 1 0", "[apple] 1 383", "[apple] 1 383");
    }

    // A compile error, so whatever the value: VB6 refuses the assignment itself, before anything is evaluated.
    [AvaloniaTheory]
    [InlineData("Combo3.Style = 2")]
    [InlineData("Combo3.Style = \"q\"")]
    public void Style_cannot_be_assigned(string statement)
    {
        var ran = Run(statement + "\n");

        ran.Errors.Should().ContainSingle().Which.Should().BeOfType<VBCompileErrorException>()
            .Which.Message.Should().Contain("Can't assign to read-only property");
    }
}
