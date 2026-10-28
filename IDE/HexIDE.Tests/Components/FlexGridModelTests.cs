using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Tests.Components;

public class FlexGridModelTests
{
    private static dynamic Grid()
    {
        // Discover by name so the same tests can execute against the pre-model assembly.
        var type = typeof(InBoxComponentClass).Assembly.GetType("HexIDE.Runtime.Components.InBox.FlexGridModel");
        type.Should().NotBeNull("the measured MSFlexGrid model must exist");
        return Activator.CreateInstance(type!)!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnmeasuredRowMetadataMovementIsRefusedWithoutMutation(bool height)
    {
        var grid = Grid();
        grid.Rows = 3;
        grid.SetText(1, 0, "keep");
        if (height) grid.SetRowHeight(1, 500);
        else grid.SetCellFormat("cellfontbold", true);
        Action add = () => grid.AddItem("new", 1);
        Action remove = () => grid.RemoveItem(1);
        add.Should().Throw<NotImplementedException>();
        remove.Should().Throw<NotImplementedException>();
        ((int)grid.Rows).Should().Be(3);
        ((string)grid.GetText(1, 0)).Should().Be("keep");
    }

    [Fact]
    public void DefaultsAndEmptyDimensions()
    {
        var grid = Grid();
        ((int)grid.Rows).Should().Be(2);
        ((int)grid.Cols).Should().Be(2);
        ((int)grid.FixedRows).Should().Be(1);
        ((int)grid.FixedCols).Should().Be(1);
        ((int)grid.Row).Should().Be(1);
        ((int)grid.Col).Should().Be(1);
        grid.Rows = 0;
        grid.Cols = 0;
        ((int)grid.FixedRows).Should().Be(0);
        ((int)grid.FixedCols).Should().Be(0);
        ((int)grid.Row).Should().Be(-1);
        ((int)grid.ColSel).Should().Be(-1);
    }

    [Fact]
    public void CursorEventsObserveOldThenNewCellAndNeverSelChange()
    {
        var grid = Grid();
        var events = new List<string>();
        grid.CellEvent = (Action<string>)(name => events.Add($"{name}:{grid.Row},{grid.Col}"));
        grid.Row = 0;
        grid.Row = 0;
        grid.Col = 0;
        grid.RowSel = 1;
        grid.ColSel = 1;
        events.Should().Equal("LeaveCell:1,1", "EnterCell:0,1", "RowColChange:0,1",
            "LeaveCell:0,1", "EnterCell:0,0", "RowColChange:0,0");
    }

    [Fact]
    public void ShrinkingClampsWithoutEventsAndDiscardsRemovedText()
    {
        var grid = Grid();
        grid.Rows = 5;
        grid.Cols = 4;
        grid.Row = 4;
        grid.Col = 3;
        grid.Text = "discard";
        grid.CellEvent = (Action<string>)(_ => throw new Exception("shrinking raised an event"));
        grid.Rows = 3;
        grid.Cols = 2;
        ((int)grid.Row).Should().Be(2);
        ((int)grid.RowSel).Should().Be(2);
        ((int)grid.Col).Should().Be(1);
        grid.Rows = 5;
        grid.Cols = 4;
        ((string)grid.GetText(4, 3)).Should().BeEmpty();
    }

    [Fact]
    public void FixedRowsMoveCursorAndTopRow()
    {
        var grid = Grid();
        grid.Rows = 5;
        grid.FixedRows = 2;
        ((int)grid.Row).Should().Be(2);
        ((int)grid.RowSel).Should().Be(2);
        ((int)grid.TopRow).Should().Be(2);
    }

    [Theory]
    [InlineData("Rows", -1, 30009)]
    [InlineData("Cols", -1, 30010)]
    [InlineData("FixedRows", 2, 30016)]
    [InlineData("FixedCols", 2, 30017)]
    [InlineData("Row", 2, 30009)]
    [InlineData("Col", 2, 30010)]
    [InlineData("RowSel", -1, 30009)]
    public void InvalidShapeOrCursorLeavesValue(string name, int value, int error)
    {
        object grid = Grid();
        var property = grid.GetType().GetProperty(name)!;
        var before = property.GetValue(grid);
        Action act = () => property.SetValue(grid, value);
        act.Should().Throw<System.Reflection.TargetInvocationException>().WithInnerException<VBRunTimeException>()
            .Which.Error.ErrNo.Should().Be(error);
        property.GetValue(grid).Should().Be(before);
    }

    [Fact]
    public void TextUsesFillStyleAndBackwardsSelectionIncludesFixedCells()
    {
        var grid = Grid();
        grid.Rows = 4;
        grid.Cols = 3;
        grid.Row = 3;
        grid.Col = 2;
        grid.RowSel = 1;
        grid.ColSel = 0;
        grid.Text = "one";
        ((string)grid.GetText(1, 0)).Should().BeEmpty();
        grid.FillStyle = 1;
        grid.Text = "all";
        ((string)grid.GetText(1, 0)).Should().Be("all");
        ((string)grid.GetText(3, 2)).Should().Be("all");
        grid.FillStyle = 2;
        ((int)grid.FillStyle).Should().Be(1);
        grid.FillStyle = -1;
        grid.FillStyle = 5;
        ((int)grid.FillStyle).Should().Be(1);
        grid.SetTextArray(7, "array");
        ((string)grid.GetTextArray(7)).Should().Be("array");
        ((string)grid.GetText(2, 1)).Should().Be("array");
        Action invalid = () => grid.GetText(4, 0);
        invalid.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(381);
        Action invalidArray = () => grid.GetTextArray(12);
        invalidArray.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(381);
        Action invalidWrite = () => grid.SetTextArray(-1, "bad");
        invalidWrite.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(381);
    }

    [Fact]
    public void UnmeasuredFixedColumnCursorEffectsAreRefused()
    {
        var grid = Grid();
        Action assign = () => grid.FixedCols = 0;
        assign.Should().Throw<NotImplementedException>();
        Action negative = () => grid.FixedCols = -1;
        negative.Should().Throw<NotImplementedException>();
        ((int)grid.FixedCols).Should().Be(1);
    }

    [Fact]
    public void ClipDropsExtraValuesAndKeepsMissingCells()
    {
        var grid = Grid();
        grid.Rows = 3;
        grid.Cols = 3;
        grid.RowSel = 2;
        grid.ColSel = 2;
        grid.SetText(2, 2, "keep");
        grid.Clip = "1\t2\t3\r4\r5\r6";
        ((string)grid.Clip).Should().Be("1\t2\r4\tkeep");
        grid.Row = 2;
        grid.Col = 2;
        grid.RowSel = 1;
        grid.ColSel = 1;
        ((string)grid.Clip).Should().Be("1\t2\r4\tkeep");
    }
}
