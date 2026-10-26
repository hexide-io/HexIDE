using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Tests;

// VB6 lifetime and precedence: docs/vb6-fidelity-oracle/a-globalmultiuse-class-in-a-referenced-library.md
public class LibraryReferenceTests
{
    private static RuntimeLibrary Library(string name, List<string> events) => new(name,
        "{11111111-1111-1111-1111-111111111111}", "1.0",
        [new("Counter", () => new Counter(name, events), ["Bump", "Trim"], true),
         new("Other", () => new Counter(name + "Other", events), ["OtherBump"], true)],
        new Dictionary<string, IReadOnlyDictionary<string, long>> { ["Numbers"] = new Dictionary<string, long> { ["One"] = 1 } });

    private sealed class Counter : ICSharpAsyncProxy, ICSharpPropertyBag, IDisposable
    {
        private readonly string name;
        private readonly List<string> events;
        private int count;
        public Counter(string name, List<string> events)
        {
            this.name = name;
            this.events = events;
            events.Add(name + "+");
        }
        public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args) => Task.FromResult<Vb6Value?>(new Vb6Value(++count));
        public void Call(string method, List<Vb6Value> args) => throw new InvalidOperationException();
        public bool TryGetProperty(string member, out Vb6Value value)
        {
            value = new Vb6Value(count);
            return member.Equals("Value", StringComparison.OrdinalIgnoreCase);
        }
        public bool TrySetProperty(string member, Vb6Value value)
        {
            if (!member.Equals("Value", StringComparison.OrdinalIgnoreCase)) return false;
            count = Convert.ToInt32(value.Value);
            return true;
        }
        public void Dispose() => events.Add(name + "-");
    }

    private sealed class Output(List<string> log) : IBasicStandardLibrary
    {
        public Task<MessageBoxResult> MsgBox(string t, string? c, MessageBoxButtons b, MessageBoxIcon i) => Task.FromResult(MessageBoxResult.Ok);
        public Task<string?> InputBox(string p, string? t, string d) => Task.FromResult<string?>("");
        public void DebugPrint(Vb6Value value) => log.Add(value.Value?.ToString() ?? "");
    }

    private static async Task Run(string body, List<string> log, params RuntimeLibrary[] libraries)
    {
        var vb = new BasicInterpreter(new Output(log), new ModuleExecutionContext(), new ExecutionEnvironment(), body);
        vb.SetAppInfo(new AppInfo(ControlLibraries: libraries));
        await vb.RunStartupSubMain();
    }

    [Fact]
    public async Task Each_implicit_class_starts_at_first_use_and_lives_until_Main_returns()
    {
        var log = new List<string>();
        await Run("""
            Sub Main()
                Debug.Print "start"
                Debug.Print Bump(), LibA.Bump(), OtherBump(), Bump()
                Debug.Print "last"
            End Sub
            """, log, Library("LibA", log));
        log.Should().Equal("start", "LibA+", "1", "2", "LibAOther+", "1", "3", "last", "LibAOther-", "LibA-");
    }

    [Fact]
    public async Task Bare_and_qualified_statement_calls_share_the_implicit_instance()
    {
        var log = new List<string>();
        await Run("Sub Main()\nBump\nCall Bump\nLibA.Bump\nCall LibA.Bump\nDebug.Print Bump\nEnd Sub", log, Library("LibA", log));
        log.Should().Equal("LibA+", "5", "LibA-");
    }

    [Fact]
    public async Task An_explicit_instance_survives_a_ByVal_call_and_function_return()
    {
        var log = new List<string>();
        await Run("""
            Sub Main()
                Dim c As Counter
                Set c = Make()
                Touch c
                Debug.Print c.Bump()
                Set c = Nothing
                Debug.Print "last"
            End Sub
            Function Make() As Object
                Set Make = New Counter
            End Function
            Sub Touch(ByVal c As Object)
                Debug.Print c.Bump()
            End Sub
            """, log, Library("LibA", log));
        log.Should().Equal("LibA+", "1", "2", "LibA-", "last");
    }

    [Fact]
    public async Task A_factorys_property_bag_is_available_on_the_explicit_instance()
    {
        var log = new List<string>();
        await Run("Sub Main()\nDim c As Object\nSet c = New Counter\nc.Value = 12\nDebug.Print c.Value\nEnd Sub", log, Library("LibA", log));
        log.Should().Equal("LibA+", "12", "LibA-");
    }

    [Fact]
    public async Task CreateObject_as_a_statement_constructs_and_releases_its_discarded_instance()
    {
        var log = new List<string>();
        await Run("Sub Main()\nCreateObject \"LibA.Counter\"\nDebug.Print \"last\"\nEnd Sub", log, Library("LibA", log));
        log.Should().Equal("LibA+", "LibA-", "last");
    }

    [Fact]
    public async Task An_unused_reference_constructs_nothing()
    {
        var log = new List<string>();
        await Run("Sub Main()\nDebug.Print 1\nEnd Sub", log, Library("LibA", log));
        log.Should().Equal("1");
    }

    [Fact]
    public async Task End_terminates_nothing()
    {
        var log = new List<string>();
        await Run("Sub Main()\nDebug.Print Bump()\nEnd\nEnd Sub", log, Library("LibA", log));
        log.Should().Equal("LibA+", "1");
    }

    [Theory]
    [InlineData("LibA", "LibB")]
    [InlineData("LibB", "LibA")]
    public async Task The_first_reference_wins(string first, string second)
    {
        var log = new List<string>();
        await Run("Sub Main()\nDebug.Print Bump()\nEnd Sub", log, Library(first, log), Library(second, log));
        log.Should().Equal(first + "+", "1", first + "-");
    }

    [Fact]
    public async Task A_project_procedure_wins_without_constructing_the_library_class()
    {
        var log = new List<string>();
        await Run("Sub Main()\nDebug.Print Bump()\nEnd Sub\nFunction Bump() As Long\nBump = 42\nEnd Function", log, Library("LibA", log));
        log.Should().Equal("42");
    }

    [Fact]
    public async Task A_library_member_hides_VBA_but_not_explicit_VBA_qualification()
    {
        var log = new List<string>();
        await Run("Sub Main()\nDebug.Print Trim(\" x \"), VBA.Trim(\" x \"), LibA.Trim(\"x\")\nEnd Sub", log, Library("LibA", log));
        log.Should().Equal("LibA+", "1", "x", "2", "LibA-");
    }

    [Theory]
    [InlineData("New Counter")]
    [InlineData("New LibA.Counter")]
    [InlineData("CreateObject(\"LibA.Counter\")")]
    public async Task Explicit_construction_is_separate_from_the_implicit_instance(string construction)
    {
        var log = new List<string>();
        await Run($"Sub Main()\nDim c As Object\nDebug.Print Bump()\nSet c = {construction}\nDebug.Print c.Bump(), Bump()\nSet c = Nothing\nEnd Sub", log, Library("LibA", log));
        log.Should().Equal("LibA+", "1", "LibA+", "1", "2", "LibA-", "LibA-");
    }

    [Fact]
    public async Task A_referenced_library_exposes_its_enums()
    {
        var log = new List<string>();
        await Run("Sub Main()\nDim n As LibA.Numbers\nDebug.Print One, Numbers.One, LibA.Numbers.One\nEnd Sub", log, Library("LibA", log));
        log.Should().Equal("1", "1", "1");
    }

    [Fact]
    public void References_select_known_GUID_and_version_in_project_order()
    {
        var log = new List<string>();
        var a = Library("LibA", log);
        var b = new RuntimeLibrary("LibB", "{22222222-2222-2222-2222-222222222222}", "2.0", [], new Dictionary<string, IReadOnlyDictionary<string, long>>());
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.SetReferences([new(a.Guid, "9.0", 0, null, null),
            new("{33333333-3333-3333-3333-333333333333}", "1.0", 0, null, null)]);
        RuntimeLibraries.ReferencedBy(project, [a, b]).Should().BeEmpty();
        project.SetReferences([new(b.Guid, "2.0", 0, null, null), new(a.Guid, "1.0", 0, null, null)]);
        RuntimeLibraries.ReferencedBy(project, [a, b]).Should().Equal(b, a);
    }
}
