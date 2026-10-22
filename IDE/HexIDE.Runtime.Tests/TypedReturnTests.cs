using System.Globalization;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A Function or Property Get declared as a scalar type converts what is stored into its name, there and then, as a store
/// into a variable of that type does (#1444). Each transcript is what <c>vb6.exe</c> logged under en-GB, native and p-code
/// agreeing (the oracle's "A Function's return, converted where it is assigned"). A failed conversion is raised inside the
/// Function, so its own handler traps it.
/// </summary>
public class TypedReturnTests : BaseVBTestFixture
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    private void Printed(string transcript) =>
        AssertDebugLog([.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))]);

    [Fact]
    public async Task A_standard_modules_typed_Function_converts_the_value_stored_into_its_name()
    {
        // HexIDE kept what was stored for every type but the numeric ones, and raised a numeric one's error in the caller,
        // after the Function had returned: `FS(5)` was an Integer, `FD("0.5")` a String, and `FI(40000)` 0 inside FI.
        using (VbLocale.Use(EnGb))
            await Run("""
                Private Function Show(ByVal v As Variant) As String
                    Show = TypeName(v) & " " & CStr(v)
                End Function
                Private Function FD(ByVal v As Variant) As Date
                    On Error Resume Next
                    FD = v
                    Debug.Print "  in FD err " & Err.Number
                End Function
                Private Function FS(ByVal v As Variant) As String
                    On Error Resume Next
                    FS = v
                    Debug.Print "  in FS err " & Err.Number
                End Function
                Private Function FB(ByVal v As Variant) As Boolean
                    On Error Resume Next
                    FB = v
                    Debug.Print "  in FB err " & Err.Number
                End Function
                Private Function FI(ByVal v As Variant) As Integer
                    On Error Resume Next
                    FI = v
                    Debug.Print "  in FI err " & Err.Number
                End Function
                Private Function FC(ByVal v As Variant) As Currency
                    On Error Resume Next
                    FC = v
                    Debug.Print "  in FC err " & Err.Number
                End Function
                Private Function FN(ByVal v As Variant) As Date
                    FN = v
                End Function
                Private Function FV(ByVal v As Variant)
                    FV = v
                End Function
                Dim r As Variant
                On Error Resume Next
                Err.Clear: Debug.Print "FD 0.5": r = FD("0.5"): Debug.Print "  -> " & Show(r) & " (" & CDbl(r) & ") err " & Err.Number
                Err.Clear: Debug.Print "FD 1/2/2003": r = FD("1/2/2003"): Debug.Print "  -> " & Show(r) & " err " & Err.Number
                Err.Clear: Debug.Print "FD 36526": r = FD("36526"): Debug.Print "  -> " & Show(r) & " err " & Err.Number
                Err.Clear: Debug.Print "FD abc": r = FD("abc"): Debug.Print "  -> " & Show(r) & " err " & Err.Number
                Err.Clear: Debug.Print "FD 2.5": r = FD(2.5): Debug.Print "  -> " & Show(r) & " err " & Err.Number
                Err.Clear: Debug.Print "FS 5": r = FS(5): Debug.Print "  -> " & Show(r) & " err " & Err.Number
                Err.Clear: Debug.Print "FS True": r = FS(True): Debug.Print "  -> " & Show(r) & " err " & Err.Number
                Err.Clear: Debug.Print "FS 2.5": r = FS(2.5): Debug.Print "  -> " & Show(r) & " err " & Err.Number
                Err.Clear: Debug.Print "FS date": r = FS(#1/2/2003#): Debug.Print "  -> " & Show(r) & " err " & Err.Number
                Err.Clear: Debug.Print "FB 5": r = FB(5): Debug.Print "  -> " & Show(r) & " err " & Err.Number
                Err.Clear: Debug.Print "FB 0": r = FB(0): Debug.Print "  -> " & Show(r) & " err " & Err.Number
                Err.Clear: Debug.Print "FB True-string": r = FB("True"): Debug.Print "  -> " & Show(r) & " err " & Err.Number
                Err.Clear: Debug.Print "FB abc": r = FB("abc"): Debug.Print "  -> " & Show(r) & " err " & Err.Number
                Err.Clear: Debug.Print "FI 40000": r = FI(40000): Debug.Print "  -> " & Show(r) & " err " & Err.Number
                Err.Clear: Debug.Print "FI 2.5": r = FI(2.5): Debug.Print "  -> " & Show(r) & " err " & Err.Number
                Err.Clear: Debug.Print "FI 12-string": r = FI("12"): Debug.Print "  -> " & Show(r) & " err " & Err.Number
                Err.Clear: Debug.Print "FC 1.00005": r = FC(1.23456): Debug.Print "  -> " & Show(r) & " err " & Err.Number
                Err.Clear: Debug.Print "FN abc (no handler in FN)": r = FN("abc"): Debug.Print "  -> " & Show(r) & " err " & Err.Number
                Err.Clear: Debug.Print "FV 0.5 (Variant)": r = FV("0.5"): Debug.Print "  -> " & Show(r) & " err " & Err.Number

                """);

        Printed("""
            FD 0.5
              in FD err 0
              -> Date 00:05:00 (3.47222222222222E-03) err 0
            FD 1/2/2003
              in FD err 0
              -> Date 01/02/2003 err 0
            FD 36526
              in FD err 0
              -> Date 01/01/2000 err 0
            FD abc
              in FD err 13
              -> Date 00:00:00 err 13
            FD 2.5
              in FD err 0
              -> Date 01/01/1900 12:00:00 err 0
            FS 5
              in FS err 0
              -> String 5 err 0
            FS True
              in FS err 0
              -> String True err 0
            FS 2.5
              in FS err 0
              -> String 2.5 err 0
            FS date
              in FS err 0
              -> String 02/01/2003 err 0
            FB 5
              in FB err 0
              -> Boolean True err 0
            FB 0
              in FB err 0
              -> Boolean False err 0
            FB True-string
              in FB err 0
              -> Boolean True err 0
            FB abc
              in FB err 13
              -> Boolean False err 13
            FI 40000
              in FI err 6
              -> Integer 0 err 6
            FI 2.5
              in FI err 0
              -> Integer 2 err 0
            FI 12-string
              in FI err 0
              -> Integer 12 err 0
            FC 1.00005
              in FC err 0
              -> Currency 1.2346 err 0
            FN abc (no handler in FN)
              -> Currency 1.2346 err 13
            FV 0.5 (Variant)
              -> String 0.5 err 0
            """);
    }

    [Fact]
    public async Task A_classs_typed_Property_Get_and_Function_convert_the_value_stored_into_their_name()
    {
        using (VbLocale.Use(EnGb))
            await RunClasses("""
                Dim c As Class1, r As Variant
                Set c = New Class1
                On Error Resume Next
                Err.Clear: r = c.PD: Debug.Print "PD " & TypeName(r) & " " & CStr(r) & " err " & Err.Number
                Err.Clear: r = c.PS: Debug.Print "PS " & TypeName(r) & " " & CStr(r) & " err " & Err.Number
                Err.Clear: Debug.Print "PI": r = c.PI: Debug.Print "  -> " & TypeName(r) & " " & CStr(r) & " err " & Err.Number
                Err.Clear: Debug.Print "FB 5": r = c.FB(5): Debug.Print "  -> " & TypeName(r) & " " & CStr(r) & " err " & Err.Number
                Err.Clear: Debug.Print "FB abc": r = c.FB("abc"): Debug.Print "  -> " & TypeName(r) & " " & CStr(r) & " err " & Err.Number

                """,
                ("Class1", """
                    Public Property Get PD() As Date
                        PD = "0.5"
                    End Property
                    Public Property Get PS() As String
                        PS = 2.5
                    End Property
                    Public Property Get PI() As Integer
                        On Error Resume Next
                        PI = 40000
                        Debug.Print "  in PI err " & Err.Number
                    End Property
                    Public Function FB(ByVal v As Variant) As Boolean
                        On Error Resume Next
                        FB = v
                        Debug.Print "  in FB err " & Err.Number
                    End Function

                    """));

        Printed("""
            PD Date 00:05:00 err 0
            PS String 2.5 err 0
            PI
              in PI err 6
              -> Integer 0 err 6
            FB 5
              in FB err 0
              -> Boolean True err 0
            FB abc
              in FB err 13
              -> Boolean False err 13
            """);
    }
}
