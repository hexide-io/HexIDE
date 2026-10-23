using System.IO;
using System.Text;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Interpreter.ConditionalCompilation;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The preprocessor behind conditional compilation (#1112): the text the parser reads, the lines the code window can grey,
/// the project's arguments, and that none of it ever reaches a file. What a program does under it is
/// <see cref="ConditionalCompilationTests"/>, from vb6.exe's own probes.
/// </summary>
public sealed class ConditionalCompilerTests : BaseVBTestFixture, IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "hexide-condcomp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private static PreprocessedSource Preprocess(string source, string? condComp = null)
    {
        CompilationArguments.TryParse(condComp, out var arguments).Should().BeTrue();
        return ConditionalCompiler.Preprocess(source, new CompilationConstants(arguments!), "Module1");
    }

    [Fact]
    public void Every_line_keeps_its_number_with_the_directives_and_the_inactive_branch_made_empty()
    {
        var result = Preprocess("Sub Main()\r\n#If Win32 Then\r\n    A\r\n#Else\r\n    B\r\n    C\r\n#End If\r\nEnd Sub\r\n");

        result.Text.Should().Be("Sub Main()\r\n\r\n    A\r\n\r\n\r\n\r\n\r\nEnd Sub\r\n");
        result.InactiveLines.Should().Equal(new LineRange(5, 6));
    }

    [Fact]
    public void A_module_with_no_directive_is_read_as_it_is()
    {
        const string source = "Sub Main()\r\n    Print #1, \"#If 0 Then\"\r\n    d = #1/1/2000#\r\nEnd Sub\r\n";

        var result = Preprocess(source);

        result.Text.Should().BeSameAs(source);
        result.InactiveLines.Should().BeEmpty();
    }

    [Fact]
    public void Each_line_keeps_its_own_ending()
    {
        Preprocess("#If 0 Then\nA\r\n#End If\nB").Text.Should().Be("\n\r\n\nB");
    }

    [Fact]
    public void A_continued_directive_is_one_directive_and_each_of_its_lines_is_emptied()
    {
        var result = Preprocess("#If 0 _\r\n    Or 0 Then\r\nA\r\n#End If\r\n");

        result.Text.Should().Be("\r\n\r\n\r\n\r\n");
        result.InactiveLines.Should().Equal(new LineRange(3, 3));
    }

    // Measured: a comment that ends in " _" goes on to the next line, so the #If is part of the comment and no #End If is due.
    [Fact]
    public void A_comment_continued_onto_a_directive_swallows_it()
    {
        const string source = "' a comment _\r\n#If 0 Then\r\nA\r\n";

        Preprocess(source).Text.Should().BeSameAs(source);
    }

    [Fact]
    public void Inactive_lines_next_to_each_other_are_one_range_and_a_nested_branch_is_inside_it()
    {
        var result = Preprocess("#If 0 Then\r\nA\r\n#If 1 Then\r\nB\r\n#End If\r\nC\r\n#End If\r\nD\r\n#If 0 Then\r\nE\r\n#End If\r\n");

        result.InactiveLines.Should().Equal(new LineRange(2, 2), new LineRange(4, 4), new LineRange(6, 6), new LineRange(10, 10));
    }

    [Fact]
    public void A_directives_error_names_the_module_and_the_line()
    {
        var preprocess = () => ConditionalCompiler.Preprocess("A\r\n#If 0 Then\r\n#If Chr(65) = \"A\" Then\r\n#End If\r\n#End If\r\n",
            CompilationConstants.Vb6, "Module7");

        preprocess.Should().Throw<VBCompileErrorException>()
            .WithMessage("*Variable not defined in Module7, line 3*")
            .Which.Line.Should().Be(3);
    }

    [Fact]
    public void An_unterminated_if_is_reported_at_its_own_line()
    {
        var preprocess = () => Preprocess("A\r\n#If 1 Then\r\nB\r\n");

        preprocess.Should().Throw<VBCompileErrorException>().WithMessage("*terminate the # If block*").Which.Line.Should().Be(2);
    }

    [Theory]
    [InlineData("ARG = 5 : FLAG = 1", "ARG", 5)]
    [InlineData("A=1:B=2", "B", 2)]
    [InlineData("X = +1", "X", 1)]
    [InlineData("x = 3", "X", 3)]
    [InlineData("M = 32767", "M", 32767)]
    [InlineData("N = -32768", "N", -32768)]
    [InlineData("  NEG  =  -1  ", "neg", -1)]
    public void A_project_argument_vb6_exe_accepts_is_read(string condComp, string name, short value)
    {
        CompilationArguments.TryParse(condComp, out var arguments).Should().BeTrue();

        arguments!.Values[name].Should().Be(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_project_arguments_are_none(string? condComp)
    {
        CompilationArguments.TryParse(condComp, out var arguments).Should().BeTrue();

        arguments!.Values.Should().BeEmpty();
    }

    // Each refused by vb6.exe: "Invalid syntax for constant declarations", and /make built nothing.
    [Theory]
    [InlineData("BIG = 70000")]
    [InlineData("X = 32768")]
    [InlineData("X = -32769")]
    [InlineData("HEX = &H10")]
    [InlineData("T = True")]
    [InlineData("DD = 1.5")]
    [InlineData("SARG = \"\"x\"\"")]
    [InlineData("X = 1 : X = 2")]
    [InlineData("X =")]
    [InlineData("X = 1; Y = 2")]
    [InlineData("X = 1 :")]
    public void A_project_argument_vb6_exe_refuses_is_refused(string condComp)
    {
        CompilationArguments.TryParse(condComp, out _).Should().BeFalse();
    }

    // A directive's error is a compile error, so it stops the program as the module loads, before anything has run, even
    // in a branch that is never taken (measured).
    [Fact]
    public async Task A_directives_error_stops_the_program_before_anything_runs()
    {
        var run = () => Run("""
            Debug.Print "ran"
            #If 0 Then
            #If Chr(65) = "A" Then
            #End If
            #End If
            """);

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Variable not defined*");
        AssertDebugLog([]);
    }

    private sealed class NullLib : IBasicStandardLibrary
    {
        public Task<HexIDE.IDE.MessageBoxResult> MsgBox(
            string text, string? caption, HexIDE.IDE.MessageBoxButtons buttons, HexIDE.IDE.MessageBoxIcon icon)
            => Task.FromResult<HexIDE.IDE.MessageBoxResult>(default);
        public Task<string?> InputBox(string prompt, string? title, string defaultText) => Task.FromResult<string?>(null);
        public void DebugPrint(Vb6Value value) { }
    }

    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) { }
    }

    private async Task<(ProjectDefinition Project, string ProjectPath, byte[] Module)> LoadProject(string condComp)
    {
        Directory.CreateDirectory(dir);
        var module = Encoding.ASCII.GetBytes("""
            Attribute VB_Name = "Module1"
            #Const TRACE = 1
            Sub Main()
            #If Win32 And TRACE Then
                Debug.Print "traced"
            #Else
                this is not VB at all ((
            #End If
            End Sub

            """.ReplaceLineEndings("\r\n"));
        File.WriteAllBytes(Path.Combine(dir, "Module1.bas"), module);
        var projectPath = Path.Combine(dir, "Probe.vbp");
        File.WriteAllText(projectPath, $"Type=Exe\r\nModule=Module1; Module1.bas\r\nCondComp=\"{condComp}\"\r\nStartup=\"Sub Main\"\r\nName=\"Probe\"\r\n",
            Encoding.ASCII);
        var project = (await new ProjectLoader().LoadAsync(projectPath, new Sink())).Project;
        return (project, projectPath, module);
    }

    // The preprocessed text is the parser's alone: after the program has been built and run, the module saves as the bytes
    // it was read from, and the project keeps its CondComp line as it was.
    [Fact]
    public async Task A_module_with_directives_saves_as_it_was_read_after_it_has_run()
    {
        var (project, projectPath, bytes) = await LoadProject("DEBUG = 1 : TRACE = 0");

        var program = new VBProgram(project, new VBWindowContext(new NullLib()), debugController: null, openForm: null);
        await program.RunMainAsync();

        var module = project.Modules.Single();
        Encoding.ASCII.GetBytes(ModuleFileFormat.ToFileContent(module.Code, module.Name, module.Kind, module.OriginalHeader))
            .Should().Equal(bytes);
        new ProjectSerializer().Serialize(project, projectPath).Should().Contain("CondComp=\"DEBUG = 1 : TRACE = 0\"\r\n");
    }

    // vb6.exe refuses a CondComp it cannot read, and /make builds nothing; HexIDE refuses to run, before any module loads.
    [Fact]
    public async Task A_project_argument_vb6_exe_refuses_stops_the_program_before_it_runs()
    {
        var (project, _, _) = await LoadProject("TRACE = 70000");

        var build = () => new VBProgram(project, new VBWindowContext(new NullLib()), debugController: null, openForm: null);

        build.Should().Throw<VBCompileErrorException>().WithMessage("*Invalid syntax for constant declarations*TRACE = 70000*");
    }
}
