using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The in-box control libraries (#1082): which one an <c>Object=</c> line names, which a project references, and their
/// constants. The identities and values are the type libraries' own, read on the oracle VM (the oracle's "In-box controls:
/// the ProgressBar").
/// </summary>
public class ControlLibraryTests
{
    private const string Vbp6 = "Object={831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0; MSCOMCTL.OCX";
    private const string Frm6 = "Object = \"{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0\"; \"MSCOMCTL.OCX\"";
    private const string Vbp5 = "Object={6B7E6392-850A-101B-AFC0-4210102A8DA7}#1.3#0; COMCTL32.OCX";
    private const string Frm5 = "Object = \"{6B7E6392-850A-101B-AFC0-4210102A8DA7}#1.3#0\"; \"COMCTL32.OCX\"";
    private const string VbpDialog = "Object={F9043C88-F6F2-101A-A3C9-08002B2F49FB}#1.2#0; COMDLG32.OCX";

    [Theory]
    [InlineData(Vbp6, "{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}", "2.0", "MSCOMCTL.OCX")]
    [InlineData(Frm6, "{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}", "2.0", "MSCOMCTL.OCX")]
    [InlineData(Vbp5, "{6B7E6392-850A-101B-AFC0-4210102A8DA7}", "1.3", "COMCTL32.OCX")]
    [InlineData("  Object = \"{6B7E6392-850A-101B-AFC0-4210102A8DA7}#1.3#0\"; \"COMCTL32.OCX\"  ", "{6B7E6392-850A-101B-AFC0-4210102A8DA7}", "1.3", "COMCTL32.OCX")]
    public void An_Object_line_in_either_files_spelling_reads_as_its_library(string line, string guid, string version, string file)
    {
        ObjectReference.TryParse(line, out var reference).Should().BeTrue();

        reference.Should().Be(new ObjectReference(guid, version, file));
    }

    [Theory]
    [InlineData(@"Object=*\A..\Ctl\Ctl.vbp")]
    [InlineData(@"Reference=*\G{00020430-0000-0000-C000-000000000046}#2.0#0#C:\Windows\SysWOW64\stdole2.tlb#OLE Automation")]
    [InlineData("Objects={831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0; MSCOMCTL.OCX")]
    [InlineData("Object={831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0")]
    [InlineData("Title=\"Object\"")]
    public void A_line_that_names_no_type_library_is_not_a_reference(string line) =>
        ObjectReference.TryParse(line, out _).Should().BeFalse();

    [Theory]
    [InlineData(Vbp6, "MSComctlLib")]
    [InlineData(Frm6, "MSComctlLib")]
    [InlineData(Vbp5, "ComctlLib")]
    [InlineData(Frm5, "ComctlLib")]
    // A version VB6's "Upgrade ActiveX Controls" might write is still the same library.
    [InlineData("Object={831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.1#0; MSCOMCTL.OCX", "MSComctlLib")]
    [InlineData("Object={831fdd16-0c5c-11d2-a9fc-0000f8754da1}#2.0#0; mscomctl.ocx", "MSComctlLib")]
    public void An_Object_line_names_its_library_by_GUID(string line, string library)
    {
        ObjectReference.TryParse(line, out var reference).Should().BeTrue();

        ControlLibraries.For(reference)!.Name.Should().Be(library);
    }

    [Fact]
    public void A_library_HexIDE_does_not_know_is_none()
    {
        ObjectReference.TryParse(VbpDialog, out var reference).Should().BeTrue();

        ControlLibraries.For(reference).Should().BeNull();
    }

    [Fact]
    public void The_CommonDialogs_reference_is_read_by_the_same_parser() =>
        new[] { VbpDialog, "Object = \"{F9043C88-F6F2-101A-A3C9-08002B2F49FB}#1.2#0\"; \"COMDLG32.OCX\"", Vbp6, "Title=\"F9043C88-F6F2-101A-A3C9-08002B2F49FB\"" }
            .Select(CommonDialogConstants.IsReference).Should().Equal(true, true, false, false);

    [Fact]
    public void A_project_references_its_vbps_libraries_then_its_forms_each_once()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.UnknownPreSectionLines.Add((0, VbpDialog));
        project.UnknownPreSectionLines.Add((0, Vbp5));
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        form.HeaderLines.Add(Frm6);
        form.HeaderLines.Add(Frm5);
        project.AddForm(form);

        ControlLibraries.ReferencedBy(project).Select(l => l.Name).Should().Equal("ComctlLib", "MSComctlLib");
    }

    [Fact]
    public void Each_library_lists_the_controls_its_type_library_declares()
    {
        ControlLibraries.MSComctlLib.Controls.Keys.Should().BeEquivalentTo(
            ["TabStrip", "Toolbar", "StatusBar", "ProgressBar", "TreeView", "ListView", "ImageList", "Slider", "ImageCombo"]);
        ControlLibraries.ComctlLib.Controls.Keys.Should().BeEquivalentTo(
            ["TabStrip", "Toolbar", "StatusBar", "ProgressBar", "TreeView", "ListView", "ImageList", "Slider"]);
    }

    [Theory]
    [InlineData("MSComctlLib.ListView", "MSComctlLib")]
    [InlineData("comctllib.TreeView", "ComctlLib")]
    [InlineData("VB.TextBox", null)]
    [InlineData("ListView", null)]
    public void A_type_names_library_is_its_prefix(string typeName, string? library) =>
        (ControlLibraries.ForTypeName(typeName)?.Name).Should().Be(library);

    private static async Task<List<Vb6Value>> Run(string code, params ControlLibrary[] referenced)
    {
        var log = new List<Vb6Value>();
        var vb = new BasicInterpreter(new CapturingLib(log), new ModuleExecutionContext(), new ExecutionEnvironment(), code);
        vb.SetAppInfo(new AppInfo(ControlLibraries: referenced));
        await vb.Execute();
        return log;
    }

    private sealed class CapturingLib(List<Vb6Value> log) : IBasicStandardLibrary
    {
        public Task<MessageBoxResult> MsgBox(string t, string? c, MessageBoxButtons b, MessageBoxIcon i) => Task.FromResult(MessageBoxResult.Ok);
        public Task<string?> InputBox(string p, string? t, string d) => Task.FromResult<string?>("");
        public void DebugPrint(Vb6Value value) => log.Add(value);
    }

    // Measured: ccScrollingSmooth 1, ccFixedSingle 1 and cc3D 1, each a Long, in a project referencing MSCOMCTL.OCX.
    [Fact]
    public async Task A_referenced_librarys_constants_resolve_by_their_bare_names()
    {
        var log = await Run("Debug.Print ccScrollingSmooth, TypeName(ccScrollingSmooth), ccFixedSingle, cc3D, ccOrientationVertical",
            ControlLibraries.MSComctlLib);

        log.Select(v => v.Value?.ToString()).Should().Equal("1", "Long", "1", "1", "1");
    }

    [Fact]
    public async Task The_50_librarys_constants_resolve_while_it_is_referenced()
    {
        var log = await Run("Debug.Print sldVertical, TypeName(ccScrollingSmooth)", ControlLibraries.ComctlLib);

        // sldVertical is 5.0's OrientationConstants; ccScrollingSmooth is a 6.0 addition, so under 5.0 it's undeclared.
        log.Select(v => v.Value?.ToString()).Should().Equal("1", "Empty");
    }

    [Fact]
    public async Task Without_the_reference_a_librarys_constant_is_an_undeclared_variable()
    {
        var log = await Run("Debug.Print TypeName(ccScrollingSmooth)");

        log.Select(v => v.Value?.ToString()).Should().Equal("Empty");
    }

    // Measured, native and p-code: 1, 0 and 1, each a Long.
    [Fact]
    public async Task A_constant_resolves_qualified_by_its_library_its_enum_or_both()
    {
        var log = await Run("""
            Debug.Print MSComctlLib.ccOrientationVertical, TypeName(MSComctlLib.ccOrientationVertical)
            Debug.Print ScrollingConstants.ccScrollingStandard, MSComctlLib.ScrollingConstants.ccScrollingSmooth
            """, ControlLibraries.MSComctlLib);

        log.Select(v => v.Value?.ToString()).Should().Equal("1", "Long", "0", "1");
    }

    [Fact]
    public async Task The_50_library_qualifies_its_own_constants()
    {
        var log = await Run("Debug.Print ComctlLib.sldVertical, OrientationConstants.sldHorizontal", ControlLibraries.ComctlLib);

        log.Select(v => v.Value?.ToString()).Should().Equal("1", "0");
    }

    // An enum is strict about its members, as the in-box libraries' are (measured there: `DragConstants.vbYes` is refused).
    [Theory]
    [InlineData("Debug.Print ScrollingConstants.ccOrientationVertical")]
    [InlineData("Debug.Print MSComctlLib.ScrollingConstants.ccOrientationVertical")]
    [InlineData("Debug.Print MSComctlLib.NoSuchConstant")]
    public async Task A_member_the_qualifier_does_not_declare_is_not_found(string code)
    {
        var act = () => Run(code, ControlLibraries.MSComctlLib);

        await act.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*Method or data member not found*");
    }

    // A module-level Const or bound may name one, as an in-box library's constant may (measured there, "What a module-level
    // array's bound may name"). Not measured for a control library's.
    [Theory]
    [InlineData("Const K = MSComctlLib.ccScrollingSmooth\nDebug.Print K", 1L)]
    [InlineData("Const K = ScrollingConstants.ccScrollingStandard\nDebug.Print K", 0L)]
    [InlineData("Const K = MSComctlLib.ScrollingConstants.ccScrollingSmooth\nDebug.Print K", 1L)]
    [InlineData("Dim d(MSComctlLib.ccOrientationVertical To 3) As Long\nDebug.Print LBound(d)", 1L)]
    public async Task A_module_level_Const_or_bound_may_name_one(string code, long expected)
    {
        var log = await Run(code + "\n", ControlLibraries.MSComctlLib);

        log.Should().ContainSingle().Which.Value.Should().Match<object>(v => Convert.ToInt64(v) == expected);
    }

    // A member of another enum is "Method or data member not found", as through an in-box library's enum (measured there).
    [Fact]
    public async Task A_module_level_Const_naming_a_member_its_enum_lacks_is_not_found()
    {
        var act = () => Run("Const K = ScrollingConstants.ccOrientationVertical\nDebug.Print K\n", ControlLibraries.MSComctlLib);

        await act.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*Method or data member not found*");
    }

    // A name the library lacks is refused as an in-box library's is there, as a name the check doesn't read.
    [Fact]
    public async Task A_module_level_Const_naming_what_the_library_lacks_is_refused()
    {
        var act = () => Run("Const K = MSComctlLib.NoSuchConstant\nDebug.Print K\n", ControlLibraries.MSComctlLib);

        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*MSComctlLib.NoSuchConstant*");
    }

    // An enum both libraries declare is the in-box library's, which VB6 references first: BorderStyleConstants is VBRUN's,
    // and MSComctlLib's is reached through its library. Not measured: the order is VB6's reference order.
    [Fact]
    public async Task An_enum_an_in_box_library_also_declares_is_the_in_box_librarys()
    {
        var log = await Run("Debug.Print BorderStyleConstants.vbBSDash, MSComctlLib.BorderStyleConstants.ccFixedSingle",
            ControlLibraries.MSComctlLib);

        log.Select(v => v.Value?.ToString()).Should().Equal("2", "1");
    }

    // A referenced library's enum is a Long, as an in-box library's is, and was an object, Nothing, before #1082.
    [Theory]
    [InlineData("ScrollingConstants")]
    [InlineData("MSComctlLib.ScrollingConstants")]
    public async Task A_librarys_enum_declares_a_Long(string type)
    {
        var log = await Run($"Dim s As {type}\r\nDebug.Print TypeName(s), s\r\ns = ccScrollingSmooth\r\nDebug.Print s",
            ControlLibraries.MSComctlLib);

        log.Select(v => v.Value?.ToString()).Should().Equal("Long", "0", "1");
    }
}
