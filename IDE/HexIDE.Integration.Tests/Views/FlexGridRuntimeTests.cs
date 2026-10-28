using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

public class FlexGridRuntimeTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           ClientHeight = 3015
           ClientWidth = 4560
           Begin MSFlexGridLib.MSFlexGrid Grid1
              Height = 750
              Width = 1500
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static async Task<string[]> Run(string code)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        return (await ContainerRuntimeHarness.Run(ctx, env, code))
            .Select(v => Convert.ToString(v.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "").ToArray();
    }

    [AvaloniaFact]
    public async Task Sort_is_write_only_and_keeps_its_measured_error()
    {
        (await Run("""
            Grid1.Rows = 4
            Grid1.TextMatrix(1, 0) = "fixed column b"
            Grid1.TextMatrix(1, 1) = "b"
            Grid1.TextMatrix(2, 0) = "fixed column a"
            Grid1.TextMatrix(2, 1) = "a"
            Grid1.TextMatrix(3, 1) = "z"
            Grid1.RowSel = 2
            Grid1.Sort = 1
            Debug.Print Grid1.TextMatrix(1, 0), Grid1.TextMatrix(2, 0), Grid1.TextMatrix(3, 1)
            On Error Resume Next
            Grid1.Sort = 10
            Debug.Print Err.Number
            """)).Should().Equal("fixed column a", "fixed column b", "z", "380");
        var read = () => Run("Debug.Print Grid1.Sort");
        await read.Should().ThrowAsync<NotImplementedException>();
    }

    private sealed class CompareRoot(VBWindowContext context) : Decorator, IModuleExecutionRoot
    {
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) =>
            context.ExecuteSub(name, args);
        public bool Handles(string name) => context.Handles(name);
        // Bind by name so this test also builds against the pre-Sort execution root.
        public Task<int> CompareRowsAsync(string name, int a, int b, int cmp) =>
            ((dynamic)context).CompareRowsAsync(name, a, b, cmp);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Custom_Compare_runs_inline_on_the_owning_form_and_returns_ByRef_Cmp(int incomingCmp)
    {
        var library = Substitute.For<IBasicStandardLibrary>();
        var context = new VBWindowContext(library);
        context.SetCode($$"""
            Private Sub Grid1_Compare(ByVal Row1 As Long, ByVal Row2 As Long, Cmp As Integer)
                {{(incomingCmp == 1 ? "Debug.Print Cmp" : incomingCmp == 2 ? "Exit Sub" : "")}}
                Debug.Print Row1, Grid1.TextMatrix(Row1, 1), Row2, Grid1.TextMatrix(Row2, 1)
                Cmp = CInt(Grid1.TextMatrix(Row2, 1)) - CInt(Grid1.TextMatrix(Row1, 1))
            End Sub
            """, "Form1");
        var project = new ProjectDefinition(VBProjectType.EXE, "SortTest");
        var sink = Substitute.For<IDeserializeErrorSink>();
        var form = new FormDeserializer().Deserialize(project, Form, sink)!;
        var content = VBLoader.SpawnComponents(form, context.ExecutionContext, context.InstanceEnv);
        var root = new CompareRoot(context) { Child = content };
        var act = () => ContainerRuntimeHarness.Run(context.ExecutionContext, context.InstanceEnv, """
            Grid1.Rows = 3
            Grid1.TextMatrix(1, 1) = "1"
            Grid1.TextMatrix(2, 1) = "2"
            Grid1.RowSel = 2
            Grid1.Sort = 9
            """);
        if (incomingCmp != 0)
        {
            await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*initial Cmp*#1099*");
            return;
        }
        var printed = await act();
        var grid = ((DockPanel)content).Children.OfType<Canvas>().Single()
            .Children.OfType<VBMSFlexGrid>().Single();
        grid.Model.GetText(1, 1).Should().Be("2");
        grid.Model.GetText(2, 1).Should().Be("1");
        printed.Select(value => value.Value)
            .Should().Equal(1L, "1", 2L, "2", 1L, "1", 2L, "2", 1L, "2", 2L, "1");
        GC.KeepAlive(root);
    }

    [AvaloniaFact]
    public async Task Rows_can_be_appended_inserted_and_removed_from_code()
    {
        (await Run("""
            Grid1.Cols = 3
            Grid1.AddItem "x" & vbTab & "y" & vbTab & "z" & vbTab & "extra"
            Grid1.AddItem "only", Grid1.Rows
            Grid1.AddItem "a" & vbCr & "b", 2
            Debug.Print Grid1.Rows, Grid1.TextMatrix(2, 0), Grid1.TextMatrix(2, 1)
            Debug.Print Grid1.TextMatrix(3, 0), Grid1.TextMatrix(3, 1), Grid1.TextMatrix(3, 2)
            Debug.Print Grid1.TextMatrix(4, 0), Grid1.TextMatrix(4, 1)
            Grid1.Row = 2
            Grid1.RemoveItem 1
            Debug.Print Grid1.Rows, Grid1.Row, Grid1.TextMatrix(1, 0), Grid1.TextMatrix(2, 0)
            """)).Should().Equal("5", "a\rb", "", "x", "y", "z", "only", "", "4", "2", "a\rb", "x");
    }

    [AvaloniaTheory]
    [InlineData("Grid1.AddItem \"x\", 0", "30001")]
    [InlineData("Grid1.AddItem \"x\", 3", "30002")]
    [InlineData("Grid1.RemoveItem 0", "30000")]
    [InlineData("Grid1.RemoveItem 99", "30002")]
    [InlineData("Grid1.RemoveItem 1", "30015")]
    public async Task Row_operations_raise_measured_errors_without_mutating(string statement, string error)
    {
        (await Run($"On Error Resume Next\n{statement}\nDebug.Print Err.Number, Grid1.Rows"))
            .Should().Equal(error, "2");
    }

    [AvaloniaFact]
    public async Task A_form_can_fill_and_read_its_grid()
    {
        (await Run("""
            Debug.Print Grid1.Rows, Grid1.Cols, Grid1.FixedRows, Grid1.FixedCols
            Grid1.Rows = 4
            Grid1.Cols = 3
            Grid1.Row = 3
            Grid1.Col = 2
            Grid1.Text = "current"
            Grid1.TextMatrix(0, 0) = "heading"
            Debug.Print Grid1.Row, Grid1.Col, Grid1.Text, Grid1.TextMatrix(0, 0)
            Grid1.FixedRows = 2
            Debug.Print Grid1.Row
            Grid1.Rows = 0
            Grid1.Cols = 0
            Debug.Print Grid1.Row, Grid1.Col, Grid1.FixedRows, Grid1.FixedCols
            """)).Should().Equal("2", "2", "1", "1", "3", "2", "current", "heading", "2", "-1", "-1", "0", "0");
    }

    [AvaloniaFact]
    public async Task FormatString_sets_headings_literally_and_keeps_its_assigned_value()
    {
        (await Run("""
            Grid1.FormatString = "<a|<b;>x|>y"
            Debug.Print Grid1.TextMatrix(0, 0), Grid1.TextMatrix(0, 1), Grid1.TextMatrix(1, 0)
            Grid1.TextMatrix(0, 0) = "changed"
            Debug.Print Grid1.FormatString
            """)).Should().Equal(">x", "b", ">y", "<a|<b;>x|>y");
    }

    [AvaloniaFact]
    public async Task Invalid_indices_keep_the_controls_own_error_numbers()
    {
        (await Run("""
            On Error Resume Next
            Grid1.Row = 2
            Debug.Print Err.Number
            Err.Clear
            Grid1.Col = -1
            Debug.Print Err.Number
            Err.Clear
            Debug.Print Grid1.TextMatrix(2, 0)
            Debug.Print Err.Number
            """)).Should().Equal("30009", "30010", "381");
    }

    [AvaloniaFact]
    public async Task FormatString_grows_columns_but_never_shrinks_them()
    {
        (await Run("""
            Grid1.FormatString = "A||C"
            Debug.Print Grid1.Cols, Grid1.TextMatrix(0, 1), Grid1.TextMatrix(0, 2)
            Grid1.FormatString = "X|Y"
            Debug.Print Grid1.Cols, Grid1.TextMatrix(0, 2)
            """)).Should().Equal("3", "", "C", "3", "C");
    }

    [AvaloniaFact]
    public async Task FormatString_trims_measured_trailing_spaces_from_column_headings()
    {
        (await Run("""
            Grid1.FormatString = "Name     |Wide"
            Debug.Print Grid1.TextMatrix(0, 0), Grid1.TextMatrix(0, 1), Grid1.FormatString
            """)).Should().Equal("Name", "Wide", "Name     |Wide");
    }

    [AvaloniaFact]
    public async Task Selection_text_and_clip_are_reachable_from_code()
    {
        (await Run("""
            Grid1.Rows = 4
            Grid1.Cols = 3
            Grid1.Row = 3
            Grid1.Col = 2
            Grid1.RowSel = 1
            Grid1.ColSel = 0
            Grid1.FillStyle = 1
            Grid1.Text = "all"
            Grid1.FillStyle = -1
            Grid1.FillStyle = 5
            Grid1.FillStyle = 2
            Debug.Print Grid1.FillStyle, Grid1.RowSel, Grid1.ColSel
            Grid1.TextArray(7) = "array"
            Debug.Print Grid1.TextMatrix(1, 0), Grid1.TextArray(7)
            Grid1.Row = 1
            Grid1.Col = 1
            Grid1.RowSel = 2
            Grid1.ColSel = 2
            Grid1.Clip = "1" & vbTab & "2" & vbTab & "extra" & vbCr & "4"
            Debug.Print Grid1.Clip
            """)).Should().Equal("1", "1", "0", "all", "array", "1\t2\r4\tall");
    }

    [AvaloniaFact]
    public async Task Sizes_round_to_pixels_restore_defaults_and_set_all_columns()
    {
        (await Run("""
            Debug.Print Grid1.ColWidth(1), Grid1.RowHeight(1)
            Grid1.ColWidth(1) = 500
            Grid1.RowHeight(1) = 500
            Debug.Print Grid1.ColWidth(1), Grid1.RowHeight(1)
            Grid1.ColWidth(1) = 0
            Grid1.RowHeight(1) = 0
            Debug.Print Grid1.ColWidth(1), Grid1.RowHeight(1)
            Grid1.ColWidth(1) = -1
            Grid1.RowHeight(1) = -1
            Debug.Print Grid1.ColWidth(1), Grid1.RowHeight(1)
            Grid1.ColWidth(-1) = 777
            Debug.Print Grid1.ColWidth(0), Grid1.ColWidth(1)
            Grid1.ColWidth(1) = 100000
            Debug.Print Grid1.ColWidth(1)
            Grid1.RowHeightMin = 400
            Debug.Print Grid1.RowHeight(0), Grid1.RowHeight(1)
            """)).Should().Equal("960", "240", "495", "495", "0", "0", "960", "240",
                "780", "780", "100005", "405", "405");
    }

    [AvaloniaTheory]
    [InlineData("Grid1.ColWidth(1) = -2", "30014")]
    [InlineData("Grid1.RowHeight(1) = -2", "30013")]
    [InlineData("Grid1.RowHeightMin = -1", "30013")]
    [InlineData("Grid1.ColWidth(2) = 500", "30010")]
    [InlineData("Grid1.ColAlignment(1) = 10", "30005")]
    [InlineData("Grid1.ColAlignment(1) = -1", "30005")]
    [InlineData("Debug.Print Grid1.ColAlignment(2)", "30004")]
    [InlineData("Debug.Print Grid1.FixedAlignment(2)", "30004")]
    [InlineData("Debug.Print Grid1.TextArray(4)", "381")]
    [InlineData("Grid1.TextArray(-1) = \"bad\"", "381")]
    public async Task Size_alignment_and_array_errors_keep_the_measured_numbers(string statement, string error)
    {
        (await Run("On Error Resume Next\n" + statement + "\nDebug.Print Err.Number"))
            .Should().Equal(error);
    }

    [AvaloniaFact]
    public async Task Column_alignments_accept_every_measured_value()
    {
        (await Run("""
            Debug.Print Grid1.ColAlignment(1), Grid1.FixedAlignment(1)
            For i = 0 To 9
                Grid1.ColAlignment(1) = i
                Debug.Print Grid1.ColAlignment(1)
            Next
            """)).Should().Equal("9", "9", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9");
    }

    [AvaloniaTheory]
    [InlineData("CellAlignment", "4", "7", "4", "7")]
    [InlineData("CellFontName", "\"MS Sans Serif\"", "\"Arial\"", "MS Sans Serif", "Arial")]
    [InlineData("CellFontSize", "12", "8.25", "12", "8.25")]
    [InlineData("CellFontBold", "True", "False", "True", "False")]
    [InlineData("CellFontItalic", "True", "False", "True", "False")]
    [InlineData("CellFontUnderline", "True", "False", "True", "False")]
    [InlineData("CellFontStrikeThrough", "True", "False", "True", "False")]
    public async Task Cell_formatting_uses_FillStyle_and_reads_the_current_cell(
        string member, string first, string second, string firstRead, string secondRead)
    {
        (await Run($"""
            Grid1.Rows = 4
            Grid1.Cols = 3
            Grid1.Row = 3
            Grid1.Col = 2
            Grid1.RowSel = 0
            Grid1.ColSel = 0
            Grid1.FillStyle = 1
            Grid1.{member} = {first}
            Grid1.FillStyle = 0
            Grid1.{member} = {second}
            Grid1.FillStyle = 1
            Debug.Print Grid1.{member}
            Grid1.Row = 0
            Grid1.Col = 0
            Debug.Print Grid1.{member}
            Grid1.Row = 2
            Grid1.Col = 1
            Debug.Print Grid1.{member}, Grid1.RowHeight(2)
            """)).Should().Equal(secondRead, firstRead, firstRead, "240");
        var shrinkRows = () => Run($"Grid1.{member} = {first}\nGrid1.Rows = 1");
        await shrinkRows.Should().ThrowAsync<NotImplementedException>().WithMessage("*resizing formatted cells*#1099*");
        var shrinkCols = () => Run($"Grid1.{member} = {first}\nGrid1.Cols = 1");
        await shrinkCols.Should().ThrowAsync<NotImplementedException>().WithMessage("*resizing formatted cells*#1099*");
        var empty = () => Run($"Grid1.Rows = 0\nGrid1.{member} = {first}");
        await empty.Should().ThrowAsync<NotImplementedException>().WithMessage("*without a current cell*#1099*");
    }

    [AvaloniaFact]
    public async Task Cell_font_defaults_and_invalid_alignment_are_measured()
    {
        (await Run("""
            Debug.Print Grid1.CellFontName, Grid1.CellFontSize, Grid1.CellFontBold
            Grid1.CellAlignment = 4
            On Error Resume Next
            Grid1.CellAlignment = 10
            Debug.Print Err.Number, Grid1.CellAlignment
            """)).Should().Equal("MS Sans Serif", "8.25", "False", "30005", "4");
    }

    [AvaloniaTheory]
    [InlineData("Grid1.Clear")]
    [InlineData("Debug.Print Grid1.hWnd")]
    [InlineData("Grid1.Text = 42")]
    [InlineData("Grid1.Rows = 1.5")]
    public async Task Later_members_and_unmeasured_conversions_are_refused(string code)
    {
        var act = () => Run(code);
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*MSFlexGrid*not implemented*#1099*");
    }
}
