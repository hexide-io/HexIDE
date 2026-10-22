using System.Globalization;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A parameter declared as a scalar type converts what it is given, ByVal or ByRef given a literal, as a store into a
/// variable of that type does, before the procedure runs (#874). Measured with <c>vb6.exe</c> under en-GB, native and in the
/// IDE or p-code, every line agreeing (the oracle's "A typed parameter given a value of another type"). A value that will
/// not convert raises in the caller, and the procedure does not run.
/// </summary>
public class TypedParameterTests : BaseVBTestFixture
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    private const string Procedures = """
        Private Sub B(ByVal x As Boolean)
            Debug.Print TypeName(x) & "|" & CStr(x)
        End Sub
        Private Sub S(ByVal x As String)
            Debug.Print TypeName(x) & "|" & CStr(x)
        End Sub
        Private Sub D(ByVal x As Date)
            Debug.Print TypeName(x) & "|" & CStr(x)
        End Sub
        Private Sub BR(x As Boolean)
            Debug.Print TypeName(x) & "|" & CStr(x)
        End Sub
        Private Sub SR(x As String)
            Debug.Print TypeName(x) & "|" & CStr(x)
        End Sub
        Private Sub DR(x As Date)
            Debug.Print TypeName(x) & "|" & CStr(x)
        End Sub

        """;

    private void Printed(string transcript) =>
        AssertDebugLog([.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))]);

    [Theory]
    [InlineData("B 5", "Boolean|True")]
    [InlineData("B 0", "Boolean|False")]
    [InlineData("B \"True\"", "Boolean|True")]
    [InlineData("B \"abc\"", "err 13")]
    [InlineData("S 5", "String|5")]
    [InlineData("S True", "String|True")]
    [InlineData("S 2.5", "String|2.5")]
    [InlineData("D \"1/2/2000\"", "Date|01/02/2000")]
    [InlineData("D 1", "Date|31/12/1899")]
    [InlineData("D 2.5", "Date|01/01/1900 12:00:00")]
    [InlineData("D True", "Date|29/12/1899")]
    [InlineData("D \"abc\"", "err 13")]
    [InlineData("BR 5", "Boolean|True")]
    [InlineData("BR 0", "Boolean|False")]
    [InlineData("BR \"True\"", "Boolean|True")]
    [InlineData("BR \"abc\"", "err 13")]
    [InlineData("SR 5", "String|5")]
    [InlineData("SR True", "String|True")]
    [InlineData("SR 2.5", "String|2.5")]
    [InlineData("DR \"1/2/2000\"", "Date|01/02/2000")]
    [InlineData("DR 1", "Date|31/12/1899")]
    [InlineData("DR \"abc\"", "err 13")]
    public async Task A_Boolean_String_or_Date_parameter_converts_what_it_is_given(string call, string vb6)
    {
        // HexIDE passed each through as it was: `S 5` arrived an Integer, and `B "abc"` ran with a String.
        using (VbLocale.Use(EnGb))
            await Run(Procedures + "On Error Resume Next\n" + call + "\nIf Err.Number <> 0 Then Debug.Print \"err \" & Err.Number\n");

        ((string)debug.Single().Value!).Should().Be(vb6);
    }

    [Fact]
    public async Task A_numeric_parameter_reads_a_String_as_a_number_and_a_default_and_a_store_convert_too()
    {
        // HexIDE read "12" into an Integer parameter as 0, and kept a default or a store into the parameter as it was.
        using (VbLocale.Use(EnGb))
            await Run(""""
                Private Sub NI(ByVal x As Integer)
                    Debug.Print "  NI " & TypeName(x) & " " & x
                End Sub
                Private Sub NIR(x As Integer)
                    Debug.Print "  NIR " & TypeName(x) & " " & x
                End Sub
                Private Sub ND(ByVal x As Double)
                    Debug.Print "  ND " & TypeName(x) & " " & x
                End Sub
                Private Sub OS(Optional x As String = 5)
                    Debug.Print "  OS " & TypeName(x) & " " & x
                End Sub
                Private Sub OD(Optional x As Date = "1/2/2003")
                    Debug.Print "  OD " & TypeName(x) & " " & x
                End Sub
                Private Sub OB(Optional x As Boolean = 1)
                    Debug.Print "  OB " & TypeName(x) & " " & x
                End Sub
                Private Sub OI(Optional x As Integer = 2.5)
                    Debug.Print "  OI " & TypeName(x) & " " & x
                End Sub
                Private Sub SS(ByVal x As String)
                    x = 5
                    Debug.Print "  SS " & TypeName(x) & " " & x
                End Sub
                Private Sub SD(ByVal x As Date)
                    x = "0.5"
                    Debug.Print "  SD " & TypeName(x) & " " & x
                End Sub
                Private Sub SB(ByVal x As Boolean)
                    On Error Resume Next
                    x = "abc"
                    Debug.Print "  SB err " & Err.Number & " " & TypeName(x) & " " & x
                End Sub
                On Error Resume Next
                Err.Clear: Debug.Print "NI ""12""": NI "12": Debug.Print "  err " & Err.Number
                Err.Clear: Debug.Print "NI ""2.5""": NI "2.5": Debug.Print "  err " & Err.Number
                Err.Clear: Debug.Print "NI ""abc""": NI "abc": Debug.Print "  err " & Err.Number
                Err.Clear: Debug.Print "NI ""40000""": NI "40000": Debug.Print "  err " & Err.Number
                Err.Clear: Debug.Print "NIR ""12""": NIR "12": Debug.Print "  err " & Err.Number
                Err.Clear: Debug.Print "ND ""1.5""": ND "1.5": Debug.Print "  err " & Err.Number
                Err.Clear: Debug.Print "ND ""&H10""": ND "&H10": Debug.Print "  err " & Err.Number
                Err.Clear: Debug.Print "OS": OS: Debug.Print "  err " & Err.Number
                Err.Clear: Debug.Print "OD": OD: Debug.Print "  err " & Err.Number
                Err.Clear: Debug.Print "OB": OB: Debug.Print "  err " & Err.Number
                Err.Clear: Debug.Print "OI": OI: Debug.Print "  err " & Err.Number
                Err.Clear: Debug.Print "SS 1": SS "1": Debug.Print "  err " & Err.Number
                Err.Clear: Debug.Print "SD 1": SD 1: Debug.Print "  err " & Err.Number
                Err.Clear: Debug.Print "SB True": SB True: Debug.Print "  err " & Err.Number

                """");

        Printed("""
            NI "12"
              NI Integer 12
              err 0
            NI "2.5"
              NI Integer 2
              err 0
            NI "abc"
              err 13
            NI "40000"
              err 6
            NIR "12"
              NIR Integer 12
              err 0
            ND "1.5"
              ND Double 1.5
              err 0
            ND "&H10"
              ND Double 16
              err 0
            OS
              OS String 5
              err 0
            OD
              OD Date 01/02/2003
              err 0
            OB
              OB Boolean True
              err 0
            OI
              OI Integer 2
              err 0
            SS 1
              SS String 5
              err 0
            SD 1
              SD Date 00:05:00
              err 0
            SB True
              SB err 13 Boolean True
              err 13
            """);
    }
}
