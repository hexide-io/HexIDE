using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured in #2555 on VB6 SP6, native and p-code agreeing.
public class PredeclaredClassTests : BaseVBTestFixture
{
    private const string Class1 = """
        Attribute VB_PredeclaredId = True
        Option Explicit
        Public Value As Long
        Private Sub Class_Initialize()
            Debug.Print "Initialize"
        End Sub
        Private Sub Class_Terminate()
            Debug.Print "Terminate"
        End Sub

        """;

    [Theory]
    [InlineData("Class1.Value = 5", "Initialize|5", "Debug.Print Class1.Value")]
    [InlineData("Debug.Print TypeName(Class1)", "Initialize|Class1", "")]
    [InlineData("Class1.Value = 5: Dim c As Class1: Set c = New Class1: c.Value = 7", "Initialize|Initialize|5|7|Terminate", "Debug.Print Class1.Value: Debug.Print c.Value: Set c = Nothing")]
    [InlineData("Class1.Value = 5: Set Class1 = Nothing", "Initialize|Terminate", "")]
    [InlineData("Class1.Value = 5: Set Class1 = Nothing", "Initialize|Terminate|Initialize|False|0", "Debug.Print Class1 Is Nothing: Debug.Print Class1.Value")]
    public async Task Measured_rows(string setup, string expected, string probe)
    {
        await RunClasses("Debug.Print \"before\"\n" + setup + "\n" + probe + "\nDebug.Print \"end\"\n", ("Class1", Class1));
        var ending = setup.EndsWith("Set Class1 = Nothing") && probe == "" ? new[] { "end" } : new[] { "end", "Terminate" };
        AssertDebugLog(new[] { "before" }.Concat(expected.Split('|')).Concat(ending)
            .Select(s => s switch
            {
                "5" => new Vb6Value(5L),
                "7" => new Vb6Value(7L),
                "0" => new Vb6Value(0L),
                "False" => new Vb6Value(false),
                _ => new Vb6Value(s),
            }).ToList());
    }
    // VB6 SP6 native/p-code measurements: #4605, rows 1–5.
    private const string SelfNaming = """
        Attribute VB_PredeclaredId = True
        Public IsDefault As Boolean
        Public Tag As Long
        Private Sub Class_Initialize()
            Made = Made + 1
            If Made <= 3 Then
                IsDefault = (Me Is Class1)
                Debug.Print IsDefault
                If Fail Then Err.Raise 5
            End If
        End Sub
        Private Sub Class_Terminate()
            Debug.Print "Terminate"
        End Sub

        """;

    [Theory]
    [InlineData("d = Class1.IsDefault", false)]
    [InlineData("d = Class1.IsDefault", true)]
    [InlineData("Set o = New Class1", true)]
    public async Task Initialize_can_name_the_default_instance(string first, bool explicitNew)
    {
        var code = "Public Made As Long, Fail As Boolean\nDim d As Boolean, o As Class1\n" + first + "\n";
        if (explicitNew && first.StartsWith("d")) code += "Set o = New Class1\n";
        code += first.StartsWith("d") ? "Debug.Print d\n" : "Debug.Print Class1.IsDefault\n";
        if (explicitNew) code += "Debug.Print o.IsDefault\n";
        code += "Debug.Print Made, Err.Number\n";
        if (explicitNew) code += "Set o = Nothing\n";
        await RunClasses(code, ("Class1", SelfNaming));
        var expected = new List<Vb6Value>();
        if (explicitNew && first.StartsWith("Set")) expected.Add(new(true));
        expected.Add(new(explicitNew && first.StartsWith("Set") ? false : true));
        if (explicitNew && first.StartsWith("d")) expected.Add(new(false));
        expected.Add(new(true));
        if (explicitNew) expected.Add(new(false));
        expected.Add(new(explicitNew ? 2L : 1L));
        expected.Add(new(0L));
        expected.Add(new("Terminate"));
        if (explicitNew) expected.Add(new("Terminate"));
        AssertDebugLog(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Raising_Initialize_unpublishes_without_Terminate_and_can_retry(bool recover)
    {
        await RunClasses("""
            Public Made As Long, Fail As Boolean
            Dim v As Long, b As Boolean
            On Error Resume Next
            Fail = True
            v = Class1.Tag
            Debug.Print Err.Number
            Err.Clear
            v = Class1.Tag
            Debug.Print Err.Number
            Err.Clear
            b = True
            b = (Class1 Is Nothing)
            Debug.Print Err.Number, b, Made
            Err.Clear

            """ + (recover ? "Fail = False\nv = Class1.Tag\nDebug.Print Made, Err.Number\n" : ""),
            ("Class1", SelfNaming));
        var expected = new List<Vb6Value> { new(true), new(5L), new(true), new(5L), new(true), new(5L), new(true), new(3L) };
        if (recover) expected.AddRange([new(4L), new(0L), new("Terminate")]);
        AssertDebugLog(expected);
    }
}
