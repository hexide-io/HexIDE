using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A Const declared with a type character, <c>Const K% = 2.5</c>, takes the character as the As clause it stands for:
/// the same conversions and the same compile errors (#1614). Measured on vb6.exe, native and p-code alike, with the probe
/// projects in docs/vb6-fidelity-oracle/a-const-declared-with-a-type-character.md, "A Const declared with a type character". Each expected line is what
/// vb6.exe logged. HexIDE stopped the program as the module loaded, with "Const type hints not supported".
/// </summary>
public class ConstTypeCharacterTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    // Eleven Consts at module level and ten in a procedure, one Public. The probe also read KS! and KT$ with their
    // characters, a Single 250 and a String 5 in vb6.exe: a name read with a type character waits for #742.
    [Fact]
    public async Task Each_character_types_its_Const_as_its_As_clause_would()
    {
        await Run(""""""
            Const KI% = 2.5
            Const KL& = 3.5
            Const KS! = 2.5E2
            Const KD# = 1
            Const KC@ = 1.23456
            Const KT$ = 5
            Const KU$ = "x"
            Const KB% = True
            Const KN% = "7"
            Const KE& = KI + 0.5
            Const KH% = &HFFFF
            Public Const KP% = 7

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub InProc()
                Const LKI% = 2.5
                Const LKL& = 3.5
                Const LKS! = 2.5E2
                Const LKD# = 1
                Const LKC@ = 1.23456
                Const LKT$ = 5
                Const LKU$ = "x"
                Const LKB% = True
                Const LKN% = "7"
                Const LKH% = &HFFFF
                L "local KI% = 2.5: " & TypeName(LKI) & " " & CStr(LKI)
                L "local KL& = 3.5: " & TypeName(LKL) & " " & CStr(LKL)
                L "local KS! = 2.5E2: " & TypeName(LKS) & " " & CStr(LKS)
                L "local KD# = 1: " & TypeName(LKD) & " " & CStr(LKD)
                L "local KC@ = 1.23456: " & TypeName(LKC) & " " & CStr(LKC)
                L "local KT$ = 5: " & TypeName(LKT) & " " & CStr(LKT)
                L "local KU$ = ""x"": " & TypeName(LKU) & " " & CStr(LKU)
                L "local KB% = True: " & TypeName(LKB) & " " & CStr(LKB)
                L "local KN% = ""7"": " & TypeName(LKN) & " " & CStr(LKN)
                L "local KH% = &HFFFF: " & TypeName(LKH) & " " & CStr(LKH)
            End Sub

            Sub Main()
                L "KI% = 2.5: " & TypeName(KI) & " " & CStr(KI)
                L "KL& = 3.5: " & TypeName(KL) & " " & CStr(KL)
                L "KS! = 2.5E2: " & TypeName(KS) & " " & CStr(KS)
                L "KD# = 1: " & TypeName(KD) & " " & CStr(KD)
                L "KC@ = 1.23456: " & TypeName(KC) & " " & CStr(KC)
                L "KT$ = 5: " & TypeName(KT) & " " & CStr(KT)
                L "KU$ = ""x"": " & TypeName(KU) & " " & CStr(KU)
                L "KB% = True: " & TypeName(KB) & " " & CStr(KB)
                L "KN% = ""7"": " & TypeName(KN) & " " & CStr(KN)
                L "KE& = KI + 0.5: " & TypeName(KE) & " " & CStr(KE)
                L "KH% = &HFFFF: " & TypeName(KH) & " " & CStr(KH)
                L "Public Const KP% = 7: " & TypeName(KP) & " " & CStr(KP)
                InProc
            End Sub

            Main
            """""");

        AssertDebugLog(Lines("""
            KI% = 2.5: Integer 2
            KL& = 3.5: Long 4
            KS! = 2.5E2: Single 250
            KD# = 1: Double 1
            KC@ = 1.23456: Currency 1.2346
            KT$ = 5: String 5
            KU$ = "x": String x
            KB% = True: Integer -1
            KN% = "7": Integer 7
            KE& = KI + 0.5: Long 2
            KH% = &HFFFF: Integer -1
            Public Const KP% = 7: Integer 7
            local KI% = 2.5: Integer 2
            local KL& = 3.5: Long 4
            local KS! = 2.5E2: Single 250
            local KD# = 1: Double 1
            local KC@ = 1.23456: Currency 1.2346
            local KT$ = 5: String 5
            local KU$ = "x": String x
            local KB% = True: Integer -1
            local KN% = "7": Integer 7
            local KH% = &HFFFF: Integer -1
            """));
    }

    [Fact]
    public async Task A_String_Const_holds_a_number_as_its_text()
    {
        await Run(""""""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Const K$ = 1.5
                L "ran: " & TypeName(K) & " " & K
            End Sub

            Main
            """""");

        AssertDebugLog([new Vb6Value("ran: String 1.5")]);
    }

    // What vb6.exe refuses to build, one project each.
    // The probe also declared one name twice at module level, Const K = 1 and Const K% = 2, which vb6.exe refuses as
    // "Ambiguous name detected: K" with or without a character. DeclaredTwiceTests pins those rows (#1622).
    [Theory]
    [InlineData("ce-overflow")]
    [InlineData("ce-mismatch")]
    [InlineData("ce-null")]
    [InlineData("ce-char-and-as")]
    [InlineData("ce-byte-overflow-local")]
    [InlineData("ce-currency-overflow")]
    [InlineData("ce-single-overflow")]
    [InlineData("ce-assign")]
    public async Task A_Const_vb6_exe_refuses_is_refused_with_its_message(string name)
    {
        var (message, module1) = Refused[name];

        var run = () => Run(module1);

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage($"*{message}*");
    }

    private static readonly Dictionary<string, (string Message, string Module1)> Refused = new()
    {
        ["ce-overflow"] = ("Overflow", """
            Const K% = 40000

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran: " & K
            End Sub

            Main
            """),
        ["ce-mismatch"] = ("Type mismatch", """
            Const K% = "x"

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran: " & K
            End Sub

            Main
            """),
        ["ce-null"] = ("Invalid use of Null", """
            Const K% = Null

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran: " & K
            End Sub

            Main
            """),
        ["ce-char-and-as"] = ("Expected: =", """
            Const K% As Integer = 1

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran: " & K
            End Sub

            Main
            """),
        ["ce-byte-overflow-local"] = ("Overflow", """
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Const K% = 32768
                L "ran: " & K
            End Sub

            Main
            """),
        ["ce-currency-overflow"] = ("Overflow", """
            Const K@ = 1E20

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran: " & K
            End Sub

            Main
            """),
        ["ce-single-overflow"] = ("Overflow", """
            Const K! = 1E39

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran: " & K
            End Sub

            Main
            """),
        ["ce-assign"] = ("Assignment to constant not permitted", """
            Const K% = 1

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                K = 2
                L "ran: " & K
            End Sub

            Main
            """),
    };
}
