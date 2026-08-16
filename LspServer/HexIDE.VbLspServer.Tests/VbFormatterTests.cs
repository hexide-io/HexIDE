// SPDX-License-Identifier: MIT
using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public class VbFormatterTests
{
    [Fact]
    public void Format_NormalizesKeywordCasing()
    {
        var input = "dim x as integer";
        var result = VbFormatter.Format(input);
        result.Should().Be("Dim x As Integer");
    }

    [Fact]
    public void Format_IndentsSubBody()
    {
        var input = "Sub Test()\nDim x As Integer\nx = 1\nEnd Sub";
        var result = VbFormatter.Format(input);
        result.Should().Be("Sub Test()\n    Dim x As Integer\n    x = 1\nEnd Sub");
    }

    [Fact]
    public void Format_IndentsNestedBlocks()
    {
        var input = "Sub Test()\nIf True Then\nx = 1\nEnd If\nEnd Sub";
        var result = VbFormatter.Format(input);
        result.Should().Be(
            "Sub Test()\n    If True Then\n        x = 1\n    End If\nEnd Sub");
    }

    [Fact]
    public void Format_HandlesElse()
    {
        var input = "Sub Test()\nIf True Then\nx = 1\nElse\nx = 2\nEnd If\nEnd Sub";
        var result = VbFormatter.Format(input);
        result.Should().Be(
            "Sub Test()\n    If True Then\n        x = 1\n    Else\n        x = 2\n    End If\nEnd Sub");
    }

    // Bug-hunt MED: ElseIf was matched as BOTH a mid-block line and an If opener, adding a second indent level, so
    // the ElseIf body was 8-indented (not 4) and End If pulled in with it. Each ElseIf compounded it.
    [Fact]
    public void Format_HandlesElseIf()
    {
        var input = "Sub Test()\nIf True Then\nx = 1\nElseIf False Then\nx = 2\nEnd If\nEnd Sub";
        var result = VbFormatter.Format(input);
        result.Should().Be(
            "Sub Test()\n    If True Then\n        x = 1\n    ElseIf False Then\n        x = 2\n    End If\nEnd Sub");
    }

    [Fact]
    public void Format_HandlesForLoop()
    {
        var input = "Sub Test()\nFor i = 1 To 10\nDebug.Print i\nNext\nEnd Sub";
        var result = VbFormatter.Format(input);
        result.Should().Be(
            "Sub Test()\n    For i = 1 To 10\n        Debug.Print i\n    Next\nEnd Sub");
    }

    [Fact]
    public void Format_HandlesSelectCase()
    {
        var input = "Sub Test()\nSelect Case x\nCase 1\ny = 1\nCase 2\ny = 2\nEnd Select\nEnd Sub";
        var result = VbFormatter.Format(input);
        result.Should().Be(
            "Sub Test()\n    Select Case x\n    Case 1\n        y = 1\n    Case 2\n        y = 2\n    End Select\nEnd Sub");
    }

    [Fact]
    public void Format_PreservesStringContent()
    {
        var input = "dim x as string\nx = \"hello dim world\"";
        var result = VbFormatter.Format(input);
        result.Should().Be("Dim x As String\nx = \"hello dim world\"");
    }

    [Fact]
    public void Format_PreservesComments()
    {
        var input = "dim x as integer ' this is a dim comment";
        var result = VbFormatter.Format(input);
        result.Should().Be("Dim x As Integer ' this is a dim comment");
    }

    [Fact]
    public void Format_ReturnsNullWhenNoChangesNeeded()
    {
        var input = "Sub Test()\n    Dim x As Integer\nEnd Sub";
        var result = VbFormatter.Format(input);
        result.Should().BeNull();
    }

    [Fact]
    public void Format_HandlesMixedCaseKeywords()
    {
        var input = "PUBLIC SUB test()\nDIM x AS INTEGER\nEND SUB";
        var result = VbFormatter.Format(input);
        result.Should().Be("Public Sub test()\n    Dim x As Integer\nEnd Sub");
    }

    [Fact]
    public void Format_HandlesDoLoop()
    {
        var input = "Sub Test()\nDo While True\nx = 1\nLoop\nEnd Sub";
        var result = VbFormatter.Format(input);
        result.Should().Be(
            "Sub Test()\n    Do While True\n        x = 1\n    Loop\nEnd Sub");
    }

    [Fact]
    public void Format_HandlesWithBlock()
    {
        var input = "Sub Test()\nWith obj\n.Name = \"hi\"\nEnd With\nEnd Sub";
        var result = VbFormatter.Format(input);
        result.Should().Be(
            "Sub Test()\n    With obj\n        .Name = \"hi\"\n    End With\nEnd Sub");
    }

    [Fact]
    public void Format_HandlesEnumBlock()
    {
        var input = "Public Enum Colors\nRed\nGreen\nBlue\nEnd Enum";
        var result = VbFormatter.Format(input);
        result.Should().Be(
            "Public Enum Colors\n    Red\n    Green\n    Blue\nEnd Enum");
    }

    [Fact]
    public void Format_PreservesBlankLines()
    {
        var input = "Sub Test()\n\nx = 1\nEnd Sub";
        var result = VbFormatter.Format(input);
        result.Should().Be("Sub Test()\n\n    x = 1\nEnd Sub");
    }
}
