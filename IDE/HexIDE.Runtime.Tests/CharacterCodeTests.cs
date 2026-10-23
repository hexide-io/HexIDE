using System.Text;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// <c>Chr</c>, <c>Asc</c>, <c>ChrW</c> and <c>AscW</c> (#1134). ChrW and AscW stopped the program as undefined. Chr and Asc
/// worked in UTF-16 units, where VB6 goes through the ANSI code page: on 1252, <c>Chr(&amp;H80)</c> is "€". Each transcript
/// is what <c>vb6.exe</c> logged for the same program on a 1252 machine under en-GB, native and p-code agreeing (the
/// oracle's "Chr, Asc, ChrW and AscW"). Like the file tests, these assume code page 1252, which CI's and this project's
/// machines have.
/// </summary>
public class CharacterCodeTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    // Each row's value, or the error it raised.
    private const string Show = """
        Private Sub S(ByVal label As String, ByVal value As Variant)
            Debug.Print label & " = " & value
        End Sub
        Private Sub E(ByVal label As String)
            Debug.Print label & " -> err " & Err.Number
            Err.Clear
        End Sub

        """;

    [Fact]
    public async Task Chr_and_Asc_go_through_the_ANSI_code_page()
    {
        await Run(Show + """""
            Private Sub Rows()
                On Error Resume Next
                S "AscW(Chr(&H80))", AscW(Chr(&H80))
                S "AscW(Chr(&H81))", AscW(Chr(&H81))
                S "AscW(Chr(&H9D))", AscW(Chr(&H9D))
                S "AscW(Chr(&H9F))", AscW(Chr(&H9F))
                S "AscW(Chr(255))", AscW(Chr(255))
                S "Chr(256)", AscW(Chr(256)): E "Chr(256)"
                S "Chr(-1)", AscW(Chr(-1)): E "Chr(-1)"
                S "Chr(True)", AscW(Chr(True)): E "Chr(True)"
                S "Chr(3000000000#)", AscW(Chr(3000000000#)): E "Chr(3000000000#)"
                S "Chr(Null)", Chr(Null): E "Chr(Null)"
                S "Chr(""x"")", Chr("x"): E "Chr(""x"")"
                S "AscW(Chr(65.5))", AscW(Chr(65.5))
                S "AscW(Chr(66.5))", AscW(Chr(66.5))
                S "Chr(""66"")", Chr("66")
                S "Len(Chr(Empty))", Len(Chr(Empty))
                S "Asc(ChrW(8364))", Asc(ChrW(8364))
                S "Asc(ChrW(&H81))", Asc(ChrW(&H81))
                S "Asc(ChrW(&H178))", Asc(ChrW(&H178))
                S "Asc(ChrW(&H2019))", Asc(ChrW(&H2019))
                S "Asc(ChrW(&H100))", Asc(ChrW(&H100))
                S "Asc(ChrW(&H416))", Asc(ChrW(&H416))
                S "Asc(True)", Asc(True)
                S "Asc("""")", Asc(""): E "Asc("""")"
                S "Asc(Empty)", Asc(Empty): E "Asc(Empty)"
                S "Asc(Null)", Asc(Null): E "Asc(Null)"
            End Sub

            Rows

            """"");

        AssertDebugLog(Lines("""
            AscW(Chr(&H80)) = 8364
            AscW(Chr(&H81)) = 129
            AscW(Chr(&H9D)) = 157
            AscW(Chr(&H9F)) = 376
            AscW(Chr(255)) = 255
            Chr(256) -> err 5
            Chr(-1) -> err 5
            Chr(True) -> err 5
            Chr(3000000000#) -> err 6
            Chr(Null) -> err 94
            Chr("x") -> err 13
            AscW(Chr(65.5)) = 66
            AscW(Chr(66.5)) = 66
            Chr("66") = B
            Len(Chr(Empty)) = 1
            Asc(ChrW(8364)) = 128
            Asc(ChrW(&H81)) = 129
            Asc(ChrW(&H178)) = 159
            Asc(ChrW(&H2019)) = 146
            Asc(ChrW(&H100)) = 65
            Asc(ChrW(&H416)) = 63
            Asc(True) = 84
            Asc("") -> err 5
            Asc(Empty) -> err 5
            Asc(Null) -> err 94
            """));
    }

    [Fact]
    public async Task ChrW_and_AscW_are_UTF16_units()
    {
        await Run(Show + """""
            Private Sub Rows()
                On Error Resume Next
                S "ChrW(65)", ChrW(65)
                S "ChrW$(67)", ChrW$(67)
                S "ChrW(CharCode:=68)", ChrW(CharCode:=68)
                S "AscW(ChrW(&H20AC))", AscW(ChrW(&H20AC))
                S "AscW(ChrW(&HF050))", AscW(ChrW(&HF050))
                S "AscW(ChrW(61520))", AscW(ChrW(61520))
                S "ChrW(-4016) = ChrW(61520)", ChrW(-4016) = ChrW(61520)
                S "AscW(ChrW(65535))", AscW(ChrW(65535))
                S "AscW(ChrW(-32768))", AscW(ChrW(-32768))
                S "AscW(ChrW(True))", AscW(ChrW(True))
                S "ChrW(65536)", AscW(ChrW(65536)): E "ChrW(65536)"
                S "ChrW(-32769)", AscW(ChrW(-32769)): E "ChrW(-32769)"
                S "ChrW(3000000000#)", AscW(ChrW(3000000000#)): E "ChrW(3000000000#)"
                S "ChrW(Null)", ChrW(Null): E "ChrW(Null)"
                S "AscW(ChrW(65.5))", AscW(ChrW(65.5))
                S "Len(ChrW(Empty))", Len(ChrW(Empty))
                S "Len(ChrW(&HD800))", Len(ChrW(&HD800))
                S "AscW(""AB"")", AscW("AB")
                S "AscW(String:=""Z"")", AscW(String:="Z")
                S "AscW(1.5)", AscW(1.5)
                S "AscW("""")", AscW(""): E "AscW("""")"
                S "AscW(Null)", AscW(Null): E "AscW(Null)"
                S "TypeName(AscW(""A""))", TypeName(AscW("A"))
                S "TypeName(ChrW(65))", TypeName(ChrW(65))
            End Sub

            Rows

            """"");

        AssertDebugLog(Lines("""
            ChrW(65) = A
            ChrW$(67) = C
            ChrW(CharCode:=68) = D
            AscW(ChrW(&H20AC)) = 8364
            AscW(ChrW(&HF050)) = -4016
            AscW(ChrW(61520)) = -4016
            ChrW(-4016) = ChrW(61520) = True
            AscW(ChrW(65535)) = -1
            AscW(ChrW(-32768)) = -32768
            AscW(ChrW(True)) = -1
            ChrW(65536) -> err 5
            ChrW(-32769) -> err 5
            ChrW(3000000000#) -> err 6
            ChrW(Null) -> err 94
            AscW(ChrW(65.5)) = 66
            Len(ChrW(Empty)) = 1
            Len(ChrW(&HD800)) = 1
            AscW("AB") = 65
            AscW(String:="Z") = 90
            AscW(1.5) = 49
            AscW("") -> err 5
            AscW(Null) -> err 94
            TypeName(AscW("A")) = Integer
            TypeName(ChrW(65)) = String
            """));
    }

    // AscW is a typed Integer, as Asc is, and ChrW a Variant, as Chr is (#987's probe: a typed Integer converts 65.4 into 65
    // and takes Case Else, then overflows past 7000 times; a Variant String "9" is read as a number against Is > 10).
    [Fact]
    public async Task AscW_is_typed_and_ChrW_a_Variant()
    {
        await Run("""
            Private Sub Rows()
                On Error Resume Next
                Dim v, r
                v = "A"
                Select Case AscW("A")
                    Case Is < 65.4: Debug.Print "AscW(""A"") Case"
                    Case Else: Debug.Print "AscW(""A"") Else"
                End Select
                r = AscW(v) * 7000: Debug.Print "AscW(v) * 7000 -> err " & Err.Number
                Err.Clear
                Select Case ChrW(57)
                    Case Is > 10: Debug.Print "ChrW(57) Case"
                    Case Else: Debug.Print "ChrW(57) Else"
                End Select
                Select Case ChrW$(57)
                    Case Is > 10: Debug.Print "ChrW$(57) Case"
                    Case Else: Debug.Print "ChrW$(57) Else"
                End Select
            End Sub

            Rows

            """);

        AssertDebugLog(Lines("""
            AscW("A") Else
            AscW(v) * 7000 -> err 6
            ChrW(57) Else
            ChrW$(57) Case
            """));
    }

    // A `$` form answers a typed String, where its plain form answers a Variant (#987's probe again: "9" against Is > 10
    // takes the Case only as text). Str$(19) is " 19", which reads the other way round.
    [Fact]
    public async Task A_dollar_form_is_a_typed_String()
    {
        await Run("""
            Private Sub Rows()
                Select Case Chr$(57)
                    Case Is > 10: Debug.Print "Chr$(57) Case"
                    Case Else: Debug.Print "Chr$(57) Else"
                End Select
                Select Case Mid$("9", 1)
                    Case Is > 10: Debug.Print "Mid$(""9"", 1) Case"
                    Case Else: Debug.Print "Mid$(""9"", 1) Else"
                End Select
                Select Case Mid("9", 1)
                    Case Is > 10: Debug.Print "Mid(""9"", 1) Case"
                    Case Else: Debug.Print "Mid(""9"", 1) Else"
                End Select
                Select Case Format$(9)
                    Case Is > 10: Debug.Print "Format$(9) Case"
                    Case Else: Debug.Print "Format$(9) Else"
                End Select
                Select Case Str$(19)
                    Case Is > 10: Debug.Print "Str$(19) Case"
                    Case Else: Debug.Print "Str$(19) Else"
                End Select
                Select Case Str(19)
                    Case Is > 10: Debug.Print "Str(19) Case"
                    Case Else: Debug.Print "Str(19) Else"
                End Select
            End Sub

            Rows

            """);

        AssertDebugLog(Lines("""
            Chr$(57) Case
            Mid$("9", 1) Case
            Mid("9", 1) Else
            Format$(9) Case
            Str$(19) Else
            Str(19) Case
            """));
    }

    [Theory]
    [InlineData("Sub S()\n    Debug.Print AscW$(\"A\")\nEnd Sub\nS", "*Type-declaration character does not match declared data type*")]
    [InlineData("Sub S()\n    Debug.Print ChrW(Code:=65)\nEnd Sub\nS", "*Named argument not found*")]
    [InlineData("Sub S()\n    Debug.Print AscW(Str:=\"A\")\nEnd Sub\nS", "*Named argument not found*")]
    public async Task What_VB6_refuses_to_compile_is_refused(string program, string message)
    {
        var run = () => Run(program + "\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage(message);
    }

    // A double-byte code page such as 932 has not been measured, so Chr and Asc refuse a code beyond ASCII under one rather
    // than guess, and give ASCII as every code page does.
    [Fact]
    public void Under_a_double_byte_code_page_Chr_and_Asc_refuse_beyond_ASCII()
    {
        var shiftJis = CodePagesEncodingProvider.Instance.GetEncoding(932)!;

        VB6BuiltIns.ChrInCodePage(65, shiftJis).Should().Be("A");
        VB6BuiltIns.AscInCodePage('A', shiftJis).Should().Be(65);
        var chr = () => VB6BuiltIns.ChrInCodePage(0x81, shiftJis);
        chr.Should().Throw<NotImplementedException>().WithMessage("*932*");
        var asc = () => VB6BuiltIns.AscInCodePage('あ', shiftJis);
        asc.Should().Throw<NotImplementedException>().WithMessage("*932*");
    }
}
