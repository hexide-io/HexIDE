using System.Globalization;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A store into a UDT field converts into the field's declared type, as a store into a variable of that type does (#221).
/// The transcript is what <c>vb6.exe</c> logged under en-GB, native and p-code agreeing (the oracle's "A store into a UDT
/// field"). A value that will not convert raises, and the field keeps what it held.
/// </summary>
public class UdtFieldStoreTests : BaseVBTestFixture
{
    [Fact]
    public async Task A_store_into_a_typed_field_converts_into_its_type()
    {
        // HexIDE stored each value as given: `u.l = 5` was an Integer, `u.dt = "1/2/2003"` a String, and `u.i = 40000` a
        // Long, with no error.
        using (VbLocale.Use(CultureInfo.GetCultureInfo("en-GB")))
            await Run(""""
                Private Type Inner
                    x As Integer
                End Type
                Private Type T
                    i As Integer
                    l As Long
                    d As Double
                    s As String
                    b As Boolean
                    dt As Date
                    c As Currency
                    v As Variant
                    inner As Inner
                End Type
                Private Function Show(ByVal v As Variant) As String
                    Show = TypeName(v) & " [" & CStr(v) & "]"
                End Function
                Dim u As T
                On Error Resume Next
                Err.Clear: u.i = 5: Debug.Print "i = 5: " & Show(u.i) & " err " & Err.Number
                Err.Clear: u.l = 5: Debug.Print "l = 5: " & Show(u.l) & " err " & Err.Number
                Err.Clear: u.l = "12": Debug.Print "l = ""12"": " & Show(u.l) & " err " & Err.Number
                Err.Clear: u.l = "abc": Debug.Print "l = ""abc"": " & Show(u.l) & " err " & Err.Number
                Err.Clear: u.i = 40000: Debug.Print "i = 40000: " & Show(u.i) & " err " & Err.Number
                Err.Clear: u.i = 2.5: Debug.Print "i = 2.5: " & Show(u.i) & " err " & Err.Number
                Err.Clear: u.d = 5: Debug.Print "d = 5: " & Show(u.d) & " err " & Err.Number
                Err.Clear: u.s = 5: Debug.Print "s = 5: " & Show(u.s) & " err " & Err.Number
                Err.Clear: u.b = 5: Debug.Print "b = 5: " & Show(u.b) & " err " & Err.Number
                Err.Clear: u.b = "abc": Debug.Print "b = ""abc"": " & Show(u.b) & " err " & Err.Number
                Err.Clear: u.dt = "1/2/2003": Debug.Print "dt = ""1/2/2003"": " & Show(u.dt) & " err " & Err.Number
                Err.Clear: u.dt = 1: Debug.Print "dt = 1: " & Show(u.dt) & " err " & Err.Number
                Err.Clear: u.c = 1.23456: Debug.Print "c = 1.23456: " & Show(u.c) & " err " & Err.Number
                Err.Clear: u.v = 5: Debug.Print "v = 5: " & Show(u.v) & " err " & Err.Number
                Err.Clear: u.inner.x = "7": Debug.Print "inner.x = ""7"": " & Show(u.inner.x) & " err " & Err.Number
                Err.Clear: u.inner.x = 40000: Debug.Print "inner.x = 40000: " & Show(u.inner.x) & " err " & Err.Number
                Err.Clear
                With u
                    .l = 7
                    .s = True
                End With
                Debug.Print "With .l = 7, .s = True: " & Show(u.l) & " " & Show(u.s) & " err " & Err.Number

                """");

        AssertDebugLog([.. """
            i = 5: Integer [5] err 0
            l = 5: Long [5] err 0
            l = "12": Long [12] err 0
            l = "abc": Long [12] err 13
            i = 40000: Integer [5] err 6
            i = 2.5: Integer [2] err 0
            d = 5: Double [5] err 0
            s = 5: String [5] err 0
            b = 5: Boolean [True] err 0
            b = "abc": Boolean [True] err 13
            dt = "1/2/2003": Date [01/02/2003] err 0
            dt = 1: Date [31/12/1899] err 0
            c = 1.23456: Currency [1.2346] err 0
            v = 5: Integer [5] err 0
            inner.x = "7": Integer [7] err 0
            inner.x = 40000: Integer [7] err 6
            With .l = 7, .s = True: Long [7] String [True] err 0
            """.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))]);
    }
}
