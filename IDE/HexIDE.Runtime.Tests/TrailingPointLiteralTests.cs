using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A numeric literal may end in its point, <c>5.</c> or <c>5.E1</c>, and is then the literal it would be with a digit
/// after the point (#1615). A literal with a point takes no Integer or Long suffix: <c>2.5%</c> and <c>5.&amp;</c> are
/// "Syntax error". Measured on vb6.exe, native and p-code alike, one /make project per form, with the probes in
/// docs/vb6-fidelity-oracle/a-numeric-literal-ending-in-its-point.md, "A numeric literal ending in its point". Each program here is one of them, and each
/// expected line is what vb6.exe logged. Neither grammar read such a literal, so the module failed to load, and a
/// point literal with % or &amp; was rounded to an Integer or a Long.
/// </summary>
public class TrailingPointLiteralTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    [Theory]
    [InlineData("args")]
    [InlineData("arith")]
    [InlineData("ccconst")]
    [InlineData("ccif")]
    [InlineData("const")]
    [InlineData("index")]
    [InlineData("lit-at")]
    [InlineData("lit-bang")]
    [InlineData("lit-dexp")]
    [InlineData("lit-exp")]
    [InlineData("lit-exp-neg")]
    [InlineData("lit-hash")]
    [InlineData("lit-large")]
    [InlineData("lit-neg")]
    [InlineData("lit-point")]
    [InlineData("lit-zero")]
    [InlineData("print-sep")]
    public async Task A_program_vb6_exe_builds_prints_its_lines(string name)
    {
        var (expected, module1) = Accepted[name];

        await Run(module1);

        AssertDebugLog(Lines(expected));
    }

    // What vb6.exe refuses to build. Each is its "Syntax error" here too, except `5..5`, two literals side by side,
    // which HexIDE's parser refuses in its own words.
    [Theory]
    [InlineData("dot-dot", false)]
    [InlineData("lit-amp", true)]
    [InlineData("lit-pct", true)]
    [InlineData("lit-amp-exp", true)]
    [InlineData("lit-amp-frac", true)]
    [InlineData("lit-amp-whole", true)]
    [InlineData("lit-pct-frac", true)]
    [InlineData("lit-pct-lead", true)]
    [InlineData("lit-pct-whole", true)]
    public async Task A_program_vb6_exe_refuses_is_refused(string name, bool syntaxError)
    {
        var run = () => Run(Refused[name]);

        var refusal = await run.Should().ThrowAsync<VBCompileErrorException>();
        if (syntaxError)
            refusal.WithMessage("*Syntax error*");
    }

    private static readonly Dictionary<string, (string Expected, string Module1)> Accepted = new()
    {
        ["args"] = ("""
            Two(5., 2.): 52
            """, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Public Function Two(ByVal a As Double, ByVal b As Double) As Double
                Two = a * 10 + b
            End Function

            Sub Main()
                L "Two(5., 2.): " & Two(5., 2.)
            End Sub

            Main
            """"""),
        ["arith"] = ("""
            5. + 1: Double 6
            2.*3.: 6
            """, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "5. + 1: " & TypeName(5. + 1) & " " & CStr(5. + 1)
                L "2.*3.: " & CStr(2.*3.)
            End Sub

            Main
            """"""),
        ["ccconst"] = ("""
            #Const C = 5.: equals 5
            """, """"""
            #Const C = 5.

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
            #If C = 5 Then
                L "#Const C = 5.: equals 5"
            #Else
                L "#Const C = 5.: not 5"
            #End If
            End Sub

            Main
            """"""),
        ["ccif"] = ("""
            #If 5. = 5: true
            """, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
            #If 5. = 5 Then
                L "#If 5. = 5: true"
            #Else
                L "#If 5. = 5: false"
            #End If
            End Sub

            Main
            """"""),
        ["const"] = ("""
            Const K = 5.: Double 5
            """, """"""
            Const K = 5.

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "Const K = 5.: " & TypeName(K) & " " & CStr(K)
            End Sub

            Main
            """"""),
        ["index"] = ("""
            a(2.) = 7, a(2): 7
            """, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim a(3) As Integer
                a(2.) = 7
                L "a(2.) = 7, a(2): " & a(2)
            End Sub

            Main
            """"""),
        ["lit-at"] = ("""
            5.@: Currency 5
            """, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "5.@: " & TypeName(5.@) & " " & CStr(5.@)
            End Sub

            Main
            """"""),
        ["lit-bang"] = ("""
            5.!: Single 5
            """, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "5.!: " & TypeName(5.!) & " " & CStr(5.!)
            End Sub

            Main
            """"""),
        ["lit-dexp"] = ("""
            5.D1: Double 50
            """, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "5.D1: " & TypeName(5.D1) & " " & CStr(5.D1)
            End Sub

            Main
            """"""),
        ["lit-exp"] = ("""
            5.E1: Double 50
            """, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "5.E1: " & TypeName(5.E1) & " " & CStr(5.E1)
            End Sub

            Main
            """"""),
        ["lit-exp-neg"] = ("""
            5.E-1: Double 0.5
            """, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "5.E-1: " & TypeName(5.E-1) & " " & CStr(5.E-1)
            End Sub

            Main
            """"""),
        ["lit-hash"] = ("""
            5.#: Double 5
            """, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "5.#: " & TypeName(5.#) & " " & CStr(5.#)
            End Sub

            Main
            """"""),
        ["lit-large"] = ("""
            123456789012.: Double 123456789012
            """, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "123456789012.: " & TypeName(123456789012.) & " " & CStr(123456789012.)
            End Sub

            Main
            """"""),
        ["lit-neg"] = ("""
            -5.: Double -5
            """, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "-5.: " & TypeName(-5.) & " " & CStr(-5.)
            End Sub

            Main
            """"""),
        ["lit-point"] = ("""
            5.: Double 5
            """, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "5.: " & TypeName(5.) & " " & CStr(5.)
            End Sub

            Main
            """"""),
        ["lit-zero"] = ("""
            0.: Double 0
            """, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "0.: " & TypeName(0.) & " " & CStr(0.)
            End Sub

            Main
            """"""),
        ["print-sep"] = ("""
            5.; 6.: 5;6
            """, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "5.; 6.: " & CStr(5.) & ";" & CStr(6.)
            End Sub

            Main
            """"""),
    };

    private static readonly Dictionary<string, string> Refused = new()
    {
        ["dot-dot"] = """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "5..5: " & CStr(5..5)
            End Sub

            Main
            """""",
        ["lit-amp"] = """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "5.&: " & TypeName(5.&) & " " & CStr(5.&)
            End Sub

            Main
            """""",
        ["lit-pct"] = """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "5.%: " & TypeName(5.%) & " " & CStr(5.%)
            End Sub

            Main
            """""",
        ["lit-amp-exp"] = """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "2.5E1&: " & TypeName(2.5E1&) & " " & CStr(2.5E1&)
            End Sub

            Main
            """""",
        ["lit-amp-frac"] = """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "2.5&: " & TypeName(2.5&) & " " & CStr(2.5&)
            End Sub

            Main
            """""",
        ["lit-amp-whole"] = """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "5.0&: " & TypeName(5.0&) & " " & CStr(5.0&)
            End Sub

            Main
            """""",
        ["lit-pct-frac"] = """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "2.5%: " & TypeName(2.5%) & " " & CStr(2.5%)
            End Sub

            Main
            """""",
        ["lit-pct-lead"] = """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L ".5%: " & TypeName(.5%) & " " & CStr(.5%)
            End Sub

            Main
            """""",
        ["lit-pct-whole"] = """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "5.0%: " & TypeName(5.0%) & " " & CStr(5.0%)
            End Sub

            Main
            """""",
    };
}
