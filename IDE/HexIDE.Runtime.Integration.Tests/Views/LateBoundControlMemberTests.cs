using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Integration.Tests.Views;

// #1433: docs/vb6-fidelity-oracle/a-controls-name-and-index-at-run-time.md,
// "A member no object has, late-bound"; member inventory from oracle PR #5193.
public class LateBoundControlMemberTests
{
    private static Task<List<Vb6Value>> Run(string code, string type = "TextBox")
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn($"""
            VERSION 5.00
            Begin VB.Form Form1
             Begin VB.{type} Text1
             End
            End
            Attribute VB_Name = "Form1"
            """);
        return ContainerRuntimeHarness.Run(ctx, env, code);
    }

    [AvaloniaTheory]
    [InlineData("Object", "v = o.Foo")]
    [InlineData("Object", "o.Foo = 1")]
    [InlineData("Object", "o.Foo")]
    [InlineData("Control", "v = o.Foo")]
    [InlineData("Control", "o.Foo = 1")]
    [InlineData("Control", "o.Foo")]
    [InlineData("Variant", "v = o.Foo")]
    [InlineData("Variant", "o.Foo = 1")]
    [InlineData("Variant", "o.Foo")]
    public async Task Missing_member_is_trappable_438(string declaration, string statement)
    {
        var result = await Run($"Dim o As {declaration}, v\nSet o = Text1\nOn Error Resume Next\n{statement}\nDebug.Print Err.Number");
        result.Single().Value?.ToString().Should().Be("438");
    }

    [AvaloniaTheory]
    [InlineData("Label", "v = o.Foo")]
    [InlineData("Label", "o.Foo = 1")]
    [InlineData("Label", "o.Foo")]
    [InlineData("CommandButton", "v = o.Foo")]
    [InlineData("CommandButton", "o.Foo = 1")]
    [InlineData("CommandButton", "o.Foo")]
    public async Task Other_intrinsic_classes_use_their_own_inventory(string type, string statement)
    {
        var result = await Run($"Dim o As Object, v\nSet o = Text1\nOn Error Resume Next\n{statement}\nDebug.Print Err.Number", type);
        result.Single().Value?.ToString().Should().Be("438");
    }

    [AvaloniaFact]
    public async Task Early_bound_read_keeps_compile_error()
    {
        var act = () => Run("On Error Resume Next\nDim v\nv = Text1.Foo\nDebug.Print Err.Number");
        await act.Should().ThrowAsync<VBMethodOrDataMemberNotFoundException>();
    }

    [AvaloniaTheory]
    [InlineData("v = o.LinkItem", "LinkItem")]
    [InlineData("o.LinkItem = 1", "LinkItem")]
    [InlineData("o.LinkItem", "LinkItem")]
    [InlineData("v = o.lInKiTeM", "lInKiTeM")]
    [InlineData("o.AddItem \"hello\"", "AddItem")]
    [InlineData("o.LinkItem(0) = 1", "LinkItem")]
    public async Task Known_unimplemented_member_cannot_be_swallowed(string statement, string member)
    {
        var act = () => Run($"Dim o As Object, v\nSet o = Text1\nOn Error Resume Next\n{statement}\nDebug.Print Err.Number");
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage($"TextBox.{member} is not implemented (#1433)");
    }

    [AvaloniaFact]
    public async Task Known_member_read_is_untrappable_on_main_too()
    {
        var act = () => Run("Dim o As Object, v\nSet o = Text1\nOn Error Resume Next\nv = o.LinkItem\nDebug.Print Err.Number");
        await act.Should().ThrowAsync<Exception>().WithMessage("*LinkItem*");
    }

    [AvaloniaFact]
    public async Task Implemented_properties_still_resolve()
    {
        var result = await Run("Dim o As Object\nSet o = Text1\no.Text = \"hello\"\nDebug.Print o.Text\nDebug.Print o.Name");
        result.Select(v => v.Value?.ToString()).Should().Equal("hello", "Text1");
    }

    [AvaloniaFact]
    public async Task Hidden_AddItem_still_runs()
    {
        var result = await Run("Dim o As Object\nSet o = Text1\no.AddItem \"hello\"\nDebug.Print o.List(0)", "ListBox");
        result.Single().Value?.ToString().Should().Be("hello");
    }
}
