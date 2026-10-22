using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A UDT field passed to a ByRef parameter is passed by reference, as a variable is (#1451). The transcript is what
/// <c>vb6.exe</c> logged under en-GB, native and p-code agreeing (the oracle's "A UDT field passed ByRef"): a store through
/// the parameter lands in the field at once, converted into the field's type, so the field reads it while the procedure is
/// still running and keeps it when the procedure fails afterwards.
/// </summary>
public class ByRefFieldTests : BaseVBTestFixture
{
    [Fact]
    public async Task A_field_passed_ByRef_is_the_field_itself()
    {
        // HexIDE copied the field into the parameter and never wrote the copy back, so every row but the ByVal and
        // parenthesised ones kept 7.
        await Run(""""
            Private Type Inner
                y As Long
            End Type
            Private Type T
                l As Long
                s As String
                v As Variant
                inner As Inner
            End Type
            Private g As T
            Private Function Show(ByVal v As Variant) As String
                Show = TypeName(v) & " [" & CStr(v) & "]"
            End Function
            Private Sub PL(x As Long)
                x = "9"
            End Sub
            Private Sub PS(x As String)
                x = x & "!"
            End Sub
            Private Sub PV(x As Variant)
                x = 12
            End Sub
            Private Sub PVS(x As Variant)
                x = "hello"
            End Sub
            Private Sub PU(x)
                Debug.Print "  PU sees " & Show(x)
                x = 2.5
            End Sub
            Private Sub PE(x As Long)
                x = 5
                Err.Raise 5
            End Sub
            Private Sub PA(x As Long)
                x = 3
                Debug.Print "  inside, g.l is " & g.l
            End Sub
            Private Sub PBV(ByVal x As Long)
                x = 99
            End Sub
            Dim u As T
            On Error Resume Next
            u.l = 7: Err.Clear: PL u.l: Debug.Print "PL u.l: " & Show(u.l) & " err " & Err.Number
            u.s = "a": Err.Clear: PS u.s: Debug.Print "PS u.s: " & Show(u.s) & " err " & Err.Number
            u.l = 7: Err.Clear: PV u.l: Debug.Print "PV u.l: " & Show(u.l) & " err " & Err.Number
            u.l = 7: Err.Clear: PVS u.l: Debug.Print "PVS u.l: " & Show(u.l) & " err " & Err.Number
            u.v = 7: Err.Clear: PVS u.v: Debug.Print "PVS u.v: " & Show(u.v) & " err " & Err.Number
            u.l = 7: Err.Clear: PU u.l: Debug.Print "PU u.l: " & Show(u.l) & " err " & Err.Number
            u.inner.y = 1: Err.Clear: PL u.inner.y: Debug.Print "PL u.inner.y: " & Show(u.inner.y) & " err " & Err.Number
            u.l = 7: Err.Clear: PE u.l: Debug.Print "PE u.l: " & Show(u.l) & " err " & Err.Number
            g.l = 1: Err.Clear: PA g.l: Debug.Print "PA g.l: " & Show(g.l) & " err " & Err.Number
            u.l = 7: Err.Clear: PBV u.l: Debug.Print "PBV u.l: " & Show(u.l) & " err " & Err.Number
            u.l = 7: Err.Clear: PL (u.l): Debug.Print "PL (u.l): " & Show(u.l) & " err " & Err.Number

            """");

        AssertDebugLog([.. """
            PL u.l: Long [9] err 0
            PS u.s: String [a!] err 0
            PV u.l: Long [12] err 0
            PVS u.l: Long [7] err 13
            PVS u.v: String [hello] err 0
              PU sees Long [7]
            PU u.l: Long [2] err 0
            PL u.inner.y: Long [9] err 0
            PE u.l: Long [5] err 5
              inside, g.l is 3
            PA g.l: Long [3] err 0
            PBV u.l: Long [7] err 0
            PL (u.l): Long [7] err 0
            """.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))]);
    }
}
