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
}
