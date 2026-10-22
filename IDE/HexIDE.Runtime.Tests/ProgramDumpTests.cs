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
        module.GetProperty("source").GetString().Should().Be(code);
        module.GetProperty("bare").GetBoolean().Should().BeTrue();
        module.GetProperty("wrapped").GetString().Should().Be(
            "Dim n As Integer\nSub Bump()\n    n = n + 3\nEnd Sub\n" +
            "Public Sub HexDumpMain()\nn = 5\nBump\nDebug.Print n\nDebug.Print \"hi\"\nEnd Sub\n");
        record.GetProperty("printed").EnumerateArray()
            .Select(p => (p.GetProperty("type").GetString(), p.GetProperty("text").GetString()))
            .Should().Equal(("Integer", "8"), ("String", "hi"));
        record.TryGetProperty("error", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_run_that_raises_is_written_with_what_it_printed_first_and_the_error_s_number()
    {
        var act = () => Run("Debug.Print 1\nDebug.Print 1 / 0\nDebug.Print 2\n");
        await act.Should().ThrowAsync<VBRunTimeException>();

        var record = Records().Should().ContainSingle().Subject;
        record.GetProperty("printed").EnumerateArray().Select(p => p.GetProperty("text").GetString()).Should().Equal("1");
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
        records[1].GetProperty("printed").EnumerateArray().Select(p => p.GetProperty("text").GetString()).Should().Equal("2");
    }

    [Fact]
    public async Task A_class_module_is_written_as_it_is()
    {
        const string point = "Public X As Integer\n";

        await RunClasses("Dim p As Point\nSet p = New Point\np.X = 4\nDebug.Print p.X\n", ("Point", point));

        var modules = Records().Should().ContainSingle().Subject.GetProperty("modules").EnumerateArray().ToArray();
        modules.Select(m => m.GetProperty("kind").GetString()).Should().Equal("startup", "class");
        modules[1].GetProperty("source").GetString().Should().Be(point);
        modules[1].TryGetProperty("wrapped", out _).Should().BeFalse();
    }

    [Fact]
    public void A_line_label_goes_with_its_statement_and_a_Static_into_the_procedure()
    {
        var (wrapped, bare) = ProgramDump.Wrap("Option Explicit\nConst K = 2\nStatic s As Integer\nGoTo Done\nDebug.Print 1\nDone: Debug.Print K\n");

        bare.Should().BeTrue();
        wrapped.Should().Be(
            "Option Explicit\nConst K = 2\n" +
            "Public Sub HexDumpMain()\nStatic s As Integer\nGoTo Done\nDebug.Print 1\nDone: Debug.Print K\nEnd Sub\n");
    }

    [Fact]
    public void A_label_on_a_line_of_its_own_stays_in_the_first_column()
    {
        // Indented, it was "Label not defined" to another engine. The source has it in the first column.
        var (wrapped, _) = ProgramDump.Wrap("On Error GoTo H\nCall A\nExit Sub\nH:\nDebug.Print Err.Number\nSub A()\nEnd Sub\n");

        wrapped.Should().Be(
            "Sub A()\nEnd Sub\n" +
            "Public Sub HexDumpMain()\nOn Error GoTo H\nCall A\nExit Sub\nH:\nDebug.Print Err.Number\nEnd Sub\n");
    }

    [Fact]
    public void A_module_of_procedures_alone_keeps_its_order_and_gains_no_procedure()
    {
        var (wrapped, bare) = ProgramDump.Wrap("Private Type T\n    A As Integer\nEnd Type\nSub Main()\n    Debug.Print 1\nEnd Sub\n");

        bare.Should().BeFalse();
        wrapped.Should().Be("Private Type T\n    A As Integer\nEnd Type\nSub Main()\n    Debug.Print 1\nEnd Sub\n");
    }
}
