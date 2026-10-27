using System.Text.Json;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The corpus dump for #1368: off unless named, never written outside its folder, and a record of exactly what ran and
/// what it printed, with the startup module's bare statements moved into a procedure another engine can call.
/// </summary>
public class ProgramDumpTests : BaseVBTestFixture, IDisposable
{
    private readonly string file = Path.Combine(Path.GetTempPath(), $"hexide-dump-{Guid.NewGuid():N}.jsonl");

    public ProgramDumpTests() => Dump = new ProgramDump(file);

    public void Dispose()
    {
        if (File.Exists(file))
            File.Delete(file);
    }

    private JsonElement[] Records() =>
        File.ReadAllLines(file).Select(l => JsonDocument.Parse(l).RootElement).ToArray();

    [Fact]
    public async Task Null_is_recorded_as_printed()
    {
        await Run("Debug.Print Null\nDebug.Print 1; \"a\"\n");
        var record = Records().Should().ContainSingle().Subject;
        record.GetProperty("printed")[0].GetProperty("text").GetString().Should().Be("Null");
    }

    [Fact]
    public async Task Statement_lines_keep_joined_items_and_only_the_current_run()
    {
        await Run("Debug.Print 9\n");
        await Run("Debug.Print 1; \"a\"\n");
        Records()[1].GetProperty("lines").EnumerateArray().Select(l => l.GetString()).Should().Equal(" 1 a");
    }

    [Fact]
    public async Task The_program_locale_is_recorded()
    {
        using var locale = VbLocale.Use(System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
        await Run("Debug.Print 1\n");
        Records().Should().ContainSingle().Subject.GetProperty("locale").GetString().Should().Be("de-DE");
    }

    [Fact]
    public void Nothing_is_dumped_unless_a_dump_is_named()
    {
        ProgramDump.ForName(null).Should().BeNull();
        ProgramDump.ForName("").Should().BeNull();
        ProgramDump.ForName("run-1")!.FilePath.Replace('\\', '/').Should().EndWith("/artifacts/program-dump/run-1.jsonl");
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("..\\x")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData(".hidden")]
    [InlineData("Upper")]
    [InlineData("C:x")]
    public void A_name_that_would_reach_another_path_is_refused(string name)
    {
        var act = () => ProgramDump.ForName(name);
        act.Should().Throw<InvalidOperationException>().WithMessage($"*'{name}' is refused*");
    }

    [Fact]
    public async Task A_run_is_written_with_its_source_its_values_and_its_types()
    {
        const string code = "Dim n As Integer\nSub Bump()\n    n = n + 3\nEnd Sub\nn = 5\nBump\nDebug.Print n\nDebug.Print \"hi\"\n";

        await Run(code);

        var record = Records().Should().ContainSingle().Subject;
        record.GetProperty("test").GetString().Should().Contain(nameof(A_run_is_written_with_its_source_its_values_and_its_types));
        record.GetProperty("sequence").GetInt32().Should().Be(1);
        var module = record.GetProperty("modules").EnumerateArray().Should().ContainSingle().Subject;
        module.GetProperty("name").GetString().Should().Be("Module1");
        module.GetProperty("kind").GetString().Should().Be("startup");
        module.GetProperty("source").GetString()!.ReplaceLineEndings("\n").Should().Be(code);
        module.GetProperty("bare").GetBoolean().Should().BeTrue();
        module.GetProperty("wrapped").GetString()!.ReplaceLineEndings("\n").Should().Be(
            "Dim n As Integer\nSub Bump()\n    n = n + 3\nEnd Sub\n" +
            "Public Sub HexDumpMain()\nn = 5\nBump\nDebug.Print n\nDebug.Print \"hi\"\nEnd Sub\n");
        record.GetProperty("printed").EnumerateArray()
            .Select(p => (p.GetProperty("type").GetString(), p.GetProperty("text").GetString()))
            .Should().Equal(("Integer", " 8 "), ("String", "hi"));
        record.TryGetProperty("error", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_run_that_raises_is_written_with_what_it_printed_first_and_the_error_s_number()
    {
        var act = () => Run("Debug.Print 1\nDebug.Print 1 / 0\nDebug.Print 2\n");
        await act.Should().ThrowAsync<VBRunTimeException>();

        var record = Records().Should().ContainSingle().Subject;
        record.GetProperty("printed").EnumerateArray().Select(p => p.GetProperty("text").GetString()).Should().Equal(" 1 ");
        var error = record.GetProperty("error");
        error.GetProperty("number").GetInt32().Should().Be(11);
        error.GetProperty("kind").GetString().Should().Be(nameof(VBRunTimeException));
    }

    [Fact]
    public async Task Two_runs_in_one_test_are_numbered_and_each_writes_only_what_it_printed()
    {
        await Run("Debug.Print 1\n");
        await Run("Debug.Print 2\n");

        var records = Records();
        records.Select(r => r.GetProperty("sequence").GetInt32()).Should().Equal(1, 2);
        records[1].GetProperty("printed").EnumerateArray().Select(p => p.GetProperty("text").GetString()).Should().Equal(" 2 ");
    }

    [Fact]
    public async Task A_class_module_is_written_as_it_is()
    {
        const string point = "Public X As Integer\n";

        await RunClasses("Dim p As Point\nSet p = New Point\np.X = 4\nDebug.Print p.X\n", ("Point", point));

        var modules = Records().Should().ContainSingle().Subject.GetProperty("modules").EnumerateArray().ToArray();
        modules.Select(m => m.GetProperty("kind").GetString()).Should().Equal("startup", "class");
        modules[1].GetProperty("source").GetString()!.ReplaceLineEndings("\n").Should().Be(point);
        modules[1].TryGetProperty("wrapped", out _).Should().BeFalse();
    }

    [Fact]
    public void A_line_label_goes_with_its_statement_and_a_Static_into_the_procedure()
    {
        var (wrapped, bare, _) = ProgramDump.Wrap("Option Explicit\nConst K = 2\nStatic s As Integer\nGoTo Done\nDebug.Print 1\nDone: Debug.Print K\n");

        bare.Should().BeTrue();
        wrapped!.ReplaceLineEndings("\n").Should().Be(
            "Option Explicit\nConst K = 2\n" +
            "Public Sub HexDumpMain()\nStatic s As Integer\nGoTo Done\nDebug.Print 1\nDone: Debug.Print K\nEnd Sub\n");
    }

    [Fact]
    public void A_label_on_a_line_of_its_own_stays_in_the_first_column()
    {
        // Indented, it was "Label not defined" to another engine. The source has it in the first column.
        var (wrapped, _, _) = ProgramDump.Wrap("On Error GoTo H\nCall A\nExit Sub\nH:\nDebug.Print Err.Number\nSub A()\nEnd Sub\n");

        wrapped!.ReplaceLineEndings("\n").Should().Be(
            "Sub A()\nEnd Sub\n" +
            "Public Sub HexDumpMain()\nOn Error GoTo H\nCall A\nExit Sub\nH:\nDebug.Print Err.Number\nEnd Sub\n");
    }

    [Fact]
    public void A_line_number_on_its_own_or_with_a_remark_stays_a_jump_target()
    {
        // Dropped, a GoTo to it was "Label not defined" to another engine.
        var (wrapped, _, _) = ProgramDump.Wrap("GoTo 30\nDebug.Print 1\n30\n40 Rem nothing here\nDebug.Print 2\n");

        wrapped!.ReplaceLineEndings("\n").Should().Be("Public Sub HexDumpMain()\nGoTo 30\nDebug.Print 1\n30\n40\nDebug.Print 2\nEnd Sub\n");
    }

    [Fact]
    public void A_module_of_procedures_alone_keeps_its_order_and_gains_an_empty_entry()
    {
        var (wrapped, bare, _) = ProgramDump.Wrap("Private Type T\n    A As Integer\nEnd Type\nSub Main()\n    Debug.Print 1\nEnd Sub\n");

        bare.Should().BeFalse();
        wrapped!.ReplaceLineEndings("\n").Should().Be("Private Type T\n    A As Integer\nEnd Type\nSub Main()\n    Debug.Print 1\nEnd Sub\nPublic Sub HexDumpMain()\nEnd Sub\n");
    }

    [Fact]
    public void A_directive_inside_a_procedure_goes_with_it_even_where_the_grammar_has_no_rule_for_it()
    {
        // vb6.exe compiles #ElseIf without Then. The grammar doesn't, and the parse once dropped Main's End Sub and all of
        // Sub L, so the other engine was handed a different program (#2211).
        const string inside = "Sub Main()\n#If 0 Then\n#ElseIf 1\n    L \"true\"\n#End If\n    L \"compiled\"\nEnd Sub\n";
        var (wrapped, bare, refused) = ProgramDump.Wrap(inside + "Public Sub L(ByVal s As String)\n    Debug.Print s\nEnd Sub\nMain\n");

        refused.Should().BeNull();
        bare.Should().BeTrue();
        wrapped!.ReplaceLineEndings("\n").Should().Be(
            inside + "Public Sub L(ByVal s As String)\n    Debug.Print s\nEnd Sub\nPublic Sub HexDumpMain()\nMain\nEnd Sub\n");
    }

    [Fact]
    public void A_directive_at_module_level_around_bare_statements_is_refused_rather_than_dropped()
    {
        // Moved into the entry procedure without it, both branches would run.
        var (wrapped, bare, refused) = ProgramDump.Wrap("#Const Scale = 0.6\n#If Scale * 10 = 6 Then\nDebug.Print True\n#Else\nDebug.Print False\n#End If\n");

        wrapped.Should().BeNull();
        bare.Should().BeFalse();
        refused.Should().StartWith("the wrapped module would not hold the source's code; it would lose #");
    }

    [Fact]
    public void A_module_that_does_not_parse_is_refused_where_wrapping_it_would_lose_code()
    {
        // vb6.exe refuses 1D, and so does the grammar. The recovered parse kept only "Debug.Print", which another engine
        // then ran, so the run counted it as running what vb6.exe refuses (#2211).
        var (wrapped, _, refused) = ProgramDump.Wrap("Debug.Print TypeName(1D)\n");

        wrapped.Should().BeNull();
        refused.Should().StartWith("the source does not parse here, and wrapping it would change it: line ");
    }

    [Fact]
    public void A_module_that_does_not_parse_is_kept_where_wrapping_it_moves_nothing()
    {
        // vb6.exe refuses a Static parameter, and the question for another engine is whether it does too.
        var (wrapped, bare, refused) = ProgramDump.Wrap("Sub S(Static x As Long)\nEnd Sub\nS 1\n");

        refused.Should().BeNull();
        bare.Should().BeTrue();
        wrapped!.ReplaceLineEndings("\n").Should().Be("Sub S(Static x As Long)\nEnd Sub\nPublic Sub HexDumpMain()\nS 1\nEnd Sub\n");
    }

    [Fact]
    public async Task A_refused_module_is_written_with_its_reason_and_no_wrapped_text()
    {
        try
        {
            await Run("#Const K = 1\n#If K Then\nDebug.Print 1\n#End If\n");
        }
        catch (Exception)
        {
            // Whether HexIDE runs it doesn't matter here, only what the dump records.
        }

        var startup = Records().Should().ContainSingle().Subject.GetProperty("modules")[0];
        startup.TryGetProperty("wrapped", out _).Should().BeFalse();
        startup.TryGetProperty("bare", out _).Should().BeFalse();
        startup.GetProperty("notWrapped").GetString().Should().StartWith("the wrapped module would not hold");
    }
}
