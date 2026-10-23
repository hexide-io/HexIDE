// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// The server's diagnostics under conditional compilation (#1112): inactive code is never parsed, an undetermined block says
// nothing, and the arguments come from the .vbp that lists the document.

using HexIDE.Language;
using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public sealed class ConditionalCompilationTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "hexide-lsp-condcomp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private static VbCompilationContext Project(string condComp)
    {
        ConditionalCompilation.TryParseArguments(condComp, out var arguments).Should().BeTrue();
        return new VbCompilationContext(arguments);
    }

    private static List<LspDiagnostic> Diagnose(string code, VbCompilationContext? project = null) =>
        VbDiagnosticsProvider.GetDiagnostics(code.ReplaceLineEndings("\n"), project);

    private static IEnumerable<int> Lines(IEnumerable<LspDiagnostic> diagnostics) => diagnostics.Select(d => d.Range.Start.Line).Distinct();

    [Fact]
    public void An_inactive_branch_is_never_parsed_so_it_says_nothing()
    {
        Diagnose("""
            Sub Main()
            #If Win16 Then
                this is not VB at all ((
            #End If
            End Sub
            """).Should().BeEmpty();
    }

    [Fact]
    public void The_active_branch_is_parsed_and_its_errors_are_on_their_own_lines()
    {
        var diagnostics = Diagnose("""
            Sub Main()
            #If Win16 Then
                this is fine to leave ((
            #Else
                x = = 1
            #End If
            End Sub
            """);

        Lines(diagnostics).Should().Equal(4);
    }

    // The idiom that multi-target source is written in: vb6.exe compiles it, as its VBA7 branch is inactive (measured).
    [Fact]
    public void A_VBA7_declaration_behind_its_If_is_not_flagged()
    {
        const string code = """
            #If VBA7 Then
            Private Declare PtrSafe Function GetTickCount Lib "kernel32" () As LongPtr
            #Else
            Private Declare Function GetTickCount Lib "kernel32" () As Long
            #End If
            Sub Main()
            End Sub
            """;

        Diagnose(code, Project("")).Should().BeEmpty();
        Diagnose(code).Should().BeEmpty();
    }

    [Fact]
    public void Not_is_bitwise_so_Not_Win32_holds()
    {
        Lines(Diagnose("Sub Main()\n#If Not Win32 Then\n    x = = 1\n#End If\nEnd Sub\n")).Should().Equal(2);
    }

    [Fact]
    public void A_block_whose_condition_cannot_be_valued_says_nothing_and_the_code_after_it_still_does()
    {
        Diagnose("""
            Sub Main()
            #If S = "x" Then
                x = = 1
            #Else
                y = = 2
            #End If
            End Sub
            """).Should().BeEmpty();

        Lines(Diagnose("""
            Sub Main()
            #If S = "x" Then
                x = 1
            #End If
                z = = 3
            End Sub
            """)).Should().Equal(4);
    }

    // The reduced evaluator's whole vocabulary, and what it leaves undetermined. "A" is the #If branch, "B" the #Else, and
    // "?" a block nobody can choose. Values are VB6's: True is -1, comparisons give True or False, and the logical operators
    // are bitwise, so Not 1 is -2, and true.
    [Theory]
    [InlineData("Not Win16", "A")]
    [InlineData("Not Win32", "A")]
    [InlineData("Not -1", "B")]
    [InlineData("Win32 And Win16", "B")]
    [InlineData("Win32 And 3", "A")]
    [InlineData("2 And 1", "B")]
    [InlineData("Win32 Or Win16", "A")]
    [InlineData("Win16 Or 0", "B")]
    [InlineData("Win32 Xor Win32", "B")]
    [InlineData("Win32 Xor Win16", "A")]
    [InlineData("-1 Eqv 0", "B")]
    [InlineData("0 Eqv 0", "A")]
    [InlineData("-1 Imp 0", "B")]
    [InlineData("0 Imp 0", "A")]
    [InlineData("1 = 1", "A")]
    [InlineData("1 = 2", "B")]
    [InlineData("1 <> 1", "B")]
    [InlineData("1 <> 2", "A")]
    [InlineData("1 < 2", "A")]
    [InlineData("2 < 2", "B")]
    [InlineData("3 > 2", "A")]
    [InlineData("2 > 2", "B")]
    [InlineData("2 <= 2", "A")]
    [InlineData("3 <= 2", "B")]
    [InlineData("2 >= 2", "A")]
    [InlineData("1 >= 2", "B")]
    [InlineData("Win32 = True", "B")]
    [InlineData("-Win32 = -1", "A")]
    [InlineData("(0)", "B")]
    [InlineData("((Win32))", "A")]
    [InlineData("True", "A")]
    [InlineData("False", "B")]
    [InlineData("Empty", "B")]
    [InlineData("-32768%", "A")]
    [InlineData("32768%", "?")]
    [InlineData("2147483647", "A")]
    [InlineData("2147483648", "?")]
    [InlineData("\"x\" = \"x\"", "?")]
    [InlineData("1.5", "?")]
    [InlineData("&H1", "?")]
    [InlineData("1 + 1", "?")]
    [InlineData("Len(\"x\")", "?")]
    [InlineData("Null", "?")]
    [InlineData("Win32%", "?")]
    [InlineData("1 Like 1", "?")]
    [InlineData("Win32 And \"x\"", "?")]
    [InlineData("UNKNOWN", "?")]
    public void The_reduced_evaluator_chooses_or_says_it_cannot(string condition, string branch)
    {
        var applied = VbConditionalCompilation.Apply($"#If {condition} Then\nA\n#Else\nB\n#End If\n", VbCompilationContext.NoProject);

        applied.Error.Should().BeNull();
        var chose = applied.Undetermined.Count > 0 ? "?" : applied.Text.Contains('A') ? "A" : "B";
        chose.Should().Be(branch);
        applied.Text.Contains('A').Should().Be(branch != "B", "an undetermined block keeps its first branch for the parser");
        applied.Text.Contains('B').Should().Be(branch == "B");
    }

    // A name that is no #Const or built-in might be a project argument, unless the project that lists the document says it
    // is not one, in which case it is 0, as vb6.exe reads it.
    [Fact]
    public void An_unknown_name_is_undetermined_without_a_project_and_zero_with_one()
    {
        const string code = "Sub Main()\n#If TRACING Then\n    x = = 1\n#End If\nEnd Sub\n";

        Diagnose(code).Should().BeEmpty();
        Diagnose(code, Project("OTHER = 1")).Should().BeEmpty();
        Lines(Diagnose(code, Project("TRACING = 1"))).Should().Equal(2);

        const string otherwise = "Sub Main()\n#If TRACING Then\n    x = 1\n#Else\n    x = = 1\n#End If\nEnd Sub\n";
        Diagnose(otherwise).Should().BeEmpty();
        Lines(Diagnose(otherwise, Project("OTHER = 1"))).Should().Equal(4);
    }

    // An #ElseIf nobody can value makes the whole block undetermined, from its #If to its #End If, and keeps the first
    // branch that might be the one.
    [Fact]
    public void An_undetermined_ElseIf_marks_the_whole_block_and_keeps_its_own_branch()
    {
        var applied = VbConditionalCompilation.Apply(
            "#If 0 Then\nA\n#ElseIf S = \"x\" Then\nB\n#Else\nC\n#End If\nD\n", VbCompilationContext.NoProject);

        applied.Undetermined.Should().Equal(new LineRange(1, 7));
        applied.Text.Should().Be("\n\n\nB\n\n\n\nD\n");
    }

    // vb6.exe defines a #Const even in an inactive branch (measured).
    [Fact]
    public void A_Const_in_an_inactive_branch_still_defines_its_constant()
    {
        Lines(Diagnose("#If 0 Then\n#Const Z = 1\n#End If\nSub Main()\n#If Z Then\n    x = = 1\n#End If\nEnd Sub\n")).Should().Equal(5);
    }

    [Theory]
    [InlineData("Sub Main()\n    x = 1\n#End If\nEnd Sub\n", ConditionalCompilation.NoIf, 2)]
    [InlineData("Sub Main()\n#If 1 Then\n    x = 1\nEnd Sub\n", ConditionalCompilation.Unterminated, 1)]
    [InlineData("Sub Main()\n#If 0 Then\n#Else\n#Else\n#End If\nEnd Sub\n", ConditionalCompilation.ElseAfterElse, 3)]
    [InlineData("#Const A = 1\n#Const A = 2\nSub Main()\nEnd Sub\n", ConditionalCompilation.DuplicateDefinition, 1)]
    public void A_directives_error_is_the_one_diagnostic_at_its_line(string code, string message, int line)
    {
        var diagnostics = Diagnose(code);

        diagnostics.Should().ContainSingle().Which.Should().Match<LspDiagnostic>(d => d.Message == message && d.Range.Start.Line == line);
    }

    private string Write(string relative, string text)
    {
        var path = Path.Combine([dir, .. relative.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\r\n"));
        return path;
    }

    // The .vbp's item paths are backslash-separated on every host, and resolved a segment at a time, so the Linux CI run is
    // the proof this is not System.IO.Path reading "sub\Mod1.bas" as one file name.
    [Fact]
    public void The_project_that_lists_the_document_gives_its_arguments_whatever_the_host()
    {
        Write("Probe.vbp", "Type=Exe\nModule=Mod1; sub\\deeper\\..\\Mod1.bas\nModule=Mod2; Mod2.bas\nCondComp=\"ARG = 5 : FLAG = 1\"\n");
        var mod1 = Write("sub/Mod1.bas", "Attribute VB_Name = \"Mod1\"\n");
        var mod2 = Write("Mod2.bas", "Attribute VB_Name = \"Mod2\"\n");

        var projects = new VbProjectArguments();

        projects.ForPath(mod1).Arguments.Should().BeEquivalentTo(new Dictionary<string, short> { ["ARG"] = 5, ["FLAG"] = 1 });
        projects.ForPath(mod2).Arguments!["arg"].Should().Be(5);
        projects.For(new Uri(mod1).AbsoluteUri).Arguments!["FLAG"].Should().Be(1);
    }

    [Fact]
    public void A_backslashed_item_path_resolves_from_the_projects_folder()
    {
        VbProjectArguments.Resolve(dir, @"sub\deeper\..\Mod1.bas").Should().Be(Path.GetFullPath(Path.Combine(dir, "sub", "Mod1.bas")));
        VbProjectArguments.Resolve(dir, @".\Mod1.bas").Should().Be(Path.GetFullPath(Path.Combine(dir, "Mod1.bas")));
    }

    [Fact]
    public void A_document_no_project_lists_has_no_project()
    {
        Write("Probe.vbp", "Type=Exe\nModule=Mod1; Mod1.bas\nCondComp=\"ARG = 5\"\n");
        var other = Write("Other.bas", "Attribute VB_Name = \"Other\"\n");

        new VbProjectArguments().ForPath(other).Should().Be(VbCompilationContext.NoProject);
    }

    [Fact]
    public void A_project_whose_arguments_vb6_exe_refuses_gives_no_arguments()
    {
        Write("Probe.vbp", "Type=Exe\nModule=Mod1; Mod1.bas\nCondComp=\"ARG = 70000\"\n");
        var mod1 = Write("Mod1.bas", "Attribute VB_Name = \"Mod1\"\n");

        new VbProjectArguments().ForPath(mod1).Should().Be(VbCompilationContext.NoProject);
    }

    [Fact]
    public void Two_projects_that_list_the_document_with_different_arguments_give_none()
    {
        Write("One.vbp", "Type=Exe\nModule=Mod1; Mod1.bas\nCondComp=\"ARG = 1\"\n");
        Write("Two.vbp", "Type=Exe\nModule=Mod1; Mod1.bas\nCondComp=\"ARG = 2\"\n");
        var mod1 = Write("Mod1.bas", "Attribute VB_Name = \"Mod1\"\n");

        new VbProjectArguments().ForPath(mod1).Should().Be(VbCompilationContext.NoProject);
    }

    [Fact]
    public void A_project_with_no_CondComp_line_has_no_arguments_but_is_known()
    {
        Write("Probe.vbp", "Type=Exe\nModule=Mod1; Mod1.bas\n");
        var mod1 = Write("Mod1.bas", "Attribute VB_Name = \"Mod1\"\n");

        new VbProjectArguments().ForPath(mod1).Arguments.Should().NotBeNull().And.BeEmpty();
    }

    // Through the document store, as a client's didOpen and didChange reach it: the project is read again when it changes.
    [Fact]
    public async Task A_documents_diagnostics_follow_its_projects_arguments()
    {
        var project = Write("Probe.vbp", "Type=Exe\nModule=Mod1; Mod1.bas\nCondComp=\"ARG = 1\"\n");
        var mod1 = Write("Mod1.bas", "Attribute VB_Name = \"Mod1\"\n");
        const string code = "Attribute VB_Name = \"Mod1\"\nSub Main()\n#If ARG Then\n    x = = 1\n#End If\nEnd Sub\n";
        var store = new DocumentStore();
        var uri = new Uri(mod1).AbsoluteUri;

        Lines((await store.UpdateDocumentAsync(uri, code)).Diagnostics).Should().Equal(3);

        File.WriteAllText(project, "Type=Exe\r\nModule=Mod1; Mod1.bas\r\nCondComp=\"ARG = 0\"\r\n");
        File.SetLastWriteTimeUtc(project, File.GetLastWriteTimeUtc(project).AddSeconds(5));

        (await store.UpdateDocumentAsync(uri, code)).Diagnostics.Should().BeEmpty();
    }
}
