using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Random and Binary files, Get, Put and Len = (#1646). Each test runs one of the programs vb6.exe ran with /make, native
/// and p-code agreeing on every line, and must print what it logged: docs/vb6-fidelity-oracle.md, "Random and Binary
/// files". A program's L prints, and its P names the test's folder. A step HexIDE refuses on purpose is cut out, with
/// what it logged, and tested as a refusal below. The en-GB guest's code page was 1252.
/// </summary>
public class RecordFileTests : BaseVBTestFixture, IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hexide-records-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private string PathOf(string name) => Path.Combine(dir, name);

    // The folder as a probe's P joins it: a backslash, which a Linux host reads as its separator (VbFiles.FullPath).
    private string Folder => dir + "\\";

    private List<string?> Printed() => debug.Select(v => v.Value as string).ToList();

    // The guest's locale, which its dates were printed and read in: CDate("1/2/2003") is 1 February.
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    // The Types and helpers of the probes for Binary_Put_writes_each_type_as_vb6_exe_did.
    private string Probe1 => $$"""

        Private Type TRec
            i As Integer
            s As String
            l As Long
        End Type

        Private Type TVar
            v As Variant
            b As Byte
        End Type

        Public Sub L(ByVal s As String)
            Debug.Print s
        End Sub

        Public Function P(ByVal name As String) As String
            P = "{{Folder}}" & name
        End Function

        Public Function E() As String
            E = " err " & Err.Number
            Err.Clear
        End Function

        Public Sub Fresh(ByVal name As String)
            On Error Resume Next
            Kill P(name)
            Err.Clear
        End Sub

        Public Function Hx(ByVal name As String) As String
            Dim f As Integer, b() As Byte, i As Long, r As String
            f = FreeFile
            Open P(name) For Binary As #f
            If LOF(f) > 0 Then
                ReDim b(0 To LOF(f) - 1)
                Get #f, 1, b
                For i = 0 To UBound(b)
                    r = r & Right$("0" & Hex$(b(i)), 2) & " "
                Next
            End If
            Close #f
            Hx = "[" & Trim$(r) & "]"
        End Function

        Public Function Where(ByVal n As Integer) As String
            Dim r As String, v As Variant
            On Error Resume Next
            v = "?": v = LOF(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            r = " LOF " & v
            v = "?": v = Loc(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            r = r & " Loc " & v
            v = "?": v = Seek(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            r = r & " Seek " & v
            v = "?": v = EOF(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            Where = r & " EOF " & v
        End Function
        """;

    // The Types and helpers of the probes for Binary_positions_reads_and_Variants_read_back_as_vb6_exe_did, Random_records_lengths_and_access_as_vb6_exe_did, EOF_and_Len_on_Binary_and_Random_as_vb6_exe_did, Arrays_and_their_descriptors_as_vb6_exe_did, Variants_and_expressions_as_vb6_exe_did, Positions_targets_and_modes_as_vb6_exe_did, A_second_handle_on_one_file_as_vb6_exe_did, Types_packed_and_nested_as_vb6_exe_did.
    private string Probe2 => $$"""

        Private Type TRec
            i As Integer
            s As String
            l As Long
        End Type

        Private Type TVar
            v As Variant
            b As Byte
        End Type

        Private Type TMix
            b As Byte
            l As Long
        End Type

        Private Type TOuter
            a As Integer
            inner As TRec
            z As Byte
        End Type

        Private Type TVarS
            v As Variant
        End Type

        Private Type TAll
            bo As Boolean
            cu As Currency
            dt As Date
            si As Single
            db As Double
        End Type

        Public Sub L(ByVal s As String)
            Debug.Print s
        End Sub

        Public Function P(ByVal name As String) As String
            P = "{{Folder}}" & name
        End Function

        Public Function E() As String
            E = " err " & Err.Number
            Err.Clear
        End Function

        Public Sub Fresh(ByVal name As String)
            On Error Resume Next
            Kill P(name)
            Err.Clear
        End Sub

        Public Function Hx(ByVal name As String) As String
            Dim f As Integer, b() As Byte, i As Long, r As String
            f = FreeFile
            Open P(name) For Binary As #f
            If LOF(f) > 0 Then
                ReDim b(0 To LOF(f) - 1)
                Get #f, 1, b
                For i = 0 To UBound(b)
                    r = r & Right$("0" & Hex$(b(i)), 2) & " "
                Next
            End If
            Close #f
            Hx = "[" & Trim$(r) & "]"
        End Function

        Public Function Where(ByVal n As Integer) As String
            Dim r As String, v As Variant
            On Error Resume Next
            v = "?": v = LOF(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            r = " LOF " & v
            v = "?": v = Loc(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            r = r & " Loc " & v
            v = "?": v = Seek(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            r = r & " Seek " & v
            v = "?": v = EOF(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            Where = r & " EOF " & v
        End Function

        Public Sub MakeHex(ByVal name As String, ByVal hx As String)
            Dim f As Integer, b() As Byte, i As Long, n As Long
            Fresh name
            hx = Replace(hx, " ", "")
            n = Len(hx) \ 2
            f = FreeFile
            Open P(name) For Binary As #f
            If n > 0 Then
                ReDim b(0 To n - 1)
                For i = 0 To n - 1
                    b(i) = CByte("&H" & Mid$(hx, i * 2 + 1, 2))
                Next
                Put #f, 1, b
            End If
            Close #f
        End Sub

        Public Function Bounds(a As Variant) As String
            Dim r As String
            On Error Resume Next
            r = LBound(a) & " To " & UBound(a)
            If Err.Number Then r = "err" & Err.Number: Err.Clear
            Bounds = r
        End Function
        """;

    // The Types and helpers of the probes for Text_reads_on_a_Binary_file_as_vb6_exe_did, What_Put_writes_for_an_expression_as_vb6_exe_did.
    private string Probe3 => $$"""

        Private Type TRec
            i As Integer
            s As String
            l As Long
        End Type

        Private Type TVar
            v As Variant
            b As Byte
        End Type

        Private Type TMix
            b As Byte
            l As Long
        End Type

        Private Type TOuter
            a As Integer
            inner As TRec
            z As Byte
        End Type

        Private Type TVarS
            v As Variant
        End Type

        Private Type TAll
            bo As Boolean
            cu As Currency
            dt As Date
            si As Single
            db As Double
        End Type

        Public Sub L(ByVal s As String)
            Debug.Print s
        End Sub

        Public Function P(ByVal name As String) As String
            P = "{{Folder}}" & name
        End Function

        Public Function E() As String
            E = " err " & Err.Number
            Err.Clear
        End Function

        Public Sub Fresh(ByVal name As String)
            On Error Resume Next
            Kill P(name)
            Err.Clear
        End Sub

        Public Function Hx(ByVal name As String) As String
            Dim f As Integer, b() As Byte, i As Long, r As String
            f = FreeFile
            Open P(name) For Binary As #f
            If LOF(f) > 0 Then
                ReDim b(0 To LOF(f) - 1)
                Get #f, 1, b
                For i = 0 To UBound(b)
                    r = r & Right$("0" & Hex$(b(i)), 2) & " "
                Next
            End If
            Close #f
            Hx = "[" & Trim$(r) & "]"
        End Function

        Public Function Where(ByVal n As Integer) As String
            Dim r As String, v As Variant
            On Error Resume Next
            v = "?": v = LOF(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            r = " LOF " & v
            v = "?": v = Loc(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            r = r & " Loc " & v
            v = "?": v = Seek(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            r = r & " Seek " & v
            v = "?": v = EOF(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            Where = r & " EOF " & v
        End Function

        Public Sub MakeHex(ByVal name As String, ByVal hx As String)
            Dim f As Integer, b() As Byte, i As Long, n As Long
            Fresh name
            hx = Replace(hx, " ", "")
            n = Len(hx) \ 2
            f = FreeFile
            Open P(name) For Binary As #f
            If n > 0 Then
                ReDim b(0 To n - 1)
                For i = 0 To n - 1
                    b(i) = CByte("&H" & Mid$(hx, i * 2 + 1, 2))
                Next
                Put #f, 1, b
            End If
            Close #f
        End Sub

        Public Function Bounds(a As Variant) As String
            Dim r As String
            On Error Resume Next
            r = LBound(a) & " To " & UBound(a)
            If Err.Number Then r = "err" & Err.Number: Err.Clear
            Bounds = r
        End Function

        Public Function VI() As Integer
            VI = 4
        End Function

        Public Function VV() As Variant
            VV = 4
        End Function

        Public Function First(ByVal name As String, ByVal n As Long) As String
            Dim f As Integer, b() As Byte, i As Long, r As String
            f = FreeFile
            Open P(name) For Binary As #f
            ReDim b(0 To n - 1)
            Get #f, 1, b
            Close #f
            For i = 0 To n - 1
                r = r & Right$("0" & Hex$(b(i)), 2) & " "
            Next
            First = "[" & Trim$(r) & "]"
        End Function

        Public Function Show(ByVal s As String) As String
            Dim i As Long, c As String, r As String
            For i = 1 To Len(s)
                c = Mid$(s, i, 1)
                If AscW(c) < 32 Or AscW(c) > 126 Then r = r & "<" & AscW(c) & ">" Else r = r & c
            Next
            Show = "[" & r & "]"
        End Function
        """;

    // The Types and helpers of the probes for Round_trips_and_record_overflows_as_vb6_exe_did.
    private string Probe4 => $$"""

        Private Type TRec
            i As Integer
            s As String
            l As Long
        End Type

        Private Type TVar
            v As Variant
            b As Byte
        End Type

        Private Type TMix
            b As Byte
            l As Long
        End Type

        Private Type TOuter
            a As Integer
            inner As TRec
            z As Byte
        End Type

        Private Type TVarS
            v As Variant
        End Type

        Private Type TAll
            bo As Boolean
            cu As Currency
            dt As Date
            si As Single
            db As Double
        End Type

        Private gi() As Integer

        Public Sub L(ByVal s As String)
            Debug.Print s
        End Sub

        Public Function P(ByVal name As String) As String
            P = "{{Folder}}" & name
        End Function

        Public Function E() As String
            E = " err " & Err.Number
            Err.Clear
        End Function

        Public Sub Fresh(ByVal name As String)
            On Error Resume Next
            Kill P(name)
            Err.Clear
        End Sub

        Public Function Hx(ByVal name As String) As String
            Dim f As Integer, b() As Byte, i As Long, r As String
            f = FreeFile
            Open P(name) For Binary As #f
            If LOF(f) > 0 Then
                ReDim b(0 To LOF(f) - 1)
                Get #f, 1, b
                For i = 0 To UBound(b)
                    r = r & Right$("0" & Hex$(b(i)), 2) & " "
                Next
            End If
            Close #f
            Hx = "[" & Trim$(r) & "]"
        End Function

        Public Function Where(ByVal n As Integer) As String
            Dim r As String, v As Variant
            On Error Resume Next
            v = "?": v = LOF(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            r = " LOF " & v
            v = "?": v = Loc(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            r = r & " Loc " & v
            v = "?": v = Seek(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            r = r & " Seek " & v
            v = "?": v = EOF(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            Where = r & " EOF " & v
        End Function

        Public Sub MakeHex(ByVal name As String, ByVal hx As String)
            Dim f As Integer, b() As Byte, i As Long, n As Long
            Fresh name
            hx = Replace(hx, " ", "")
            n = Len(hx) \ 2
            f = FreeFile
            Open P(name) For Binary As #f
            If n > 0 Then
                ReDim b(0 To n - 1)
                For i = 0 To n - 1
                    b(i) = CByte("&H" & Mid$(hx, i * 2 + 1, 2))
                Next
                Put #f, 1, b
            End If
            Close #f
        End Sub

        Public Function Bounds(a As Variant) As String
            Dim r As String
            On Error Resume Next
            r = LBound(a) & " To " & UBound(a)
            If Err.Number Then r = "err" & Err.Number: Err.Clear
            Bounds = r
        End Function

        Public Sub LockedGet(x As Integer)
            Get #1, 1, gi
            L "  Random Get into the array while its element is passed ByRef:" & E() & " bounds " & Bounds(gi)
        End Sub

        Public Sub LockedBinaryGet(x As Integer)
            Get #1, 1, gi
            L "  Binary Get into the array while its element is passed ByRef:" & E() & " gi(0) " & gi(0)
        End Sub

        Public Function Show(ByVal s As String) As String
            Dim i As Long, c As String, r As String
            For i = 1 To Len(s)
                c = Mid$(s, i, 1)
                If AscW(c) < 32 Or AscW(c) > 126 Then r = r & "<" & AscW(c) & ">" Else r = r & c
            Next
            Show = "[" & r & "]"
        End Function
        """;

    // The Types and helpers of the probes for Get_into_a_locked_array_as_vb6_exe_did.
    private string Probe5 => $$"""

        Private Type TRec
            i As Integer
            s As String
            l As Long
        End Type

        Private Type TVar
            v As Variant
            b As Byte
        End Type

        Private Type TMix
            b As Byte
            l As Long
        End Type

        Private Type TOuter
            a As Integer
            inner As TRec
            z As Byte
        End Type

        Private Type TVarS
            v As Variant
        End Type

        Private Type TAll
            bo As Boolean
            cu As Currency
            dt As Date
            si As Single
            db As Double
        End Type

        Private gi() As Integer

        Public Sub L(ByVal s As String)
            Debug.Print s
        End Sub

        Public Function P(ByVal name As String) As String
            P = "{{Folder}}" & name
        End Function

        Public Function E() As String
            E = " err " & Err.Number
            Err.Clear
        End Function

        Public Sub Fresh(ByVal name As String)
            On Error Resume Next
            Kill P(name)
            Err.Clear
        End Sub

        Public Function Hx(ByVal name As String) As String
            Dim f As Integer, b() As Byte, i As Long, r As String
            f = FreeFile
            Open P(name) For Binary As #f
            If LOF(f) > 0 Then
                ReDim b(0 To LOF(f) - 1)
                Get #f, 1, b
                For i = 0 To UBound(b)
                    r = r & Right$("0" & Hex$(b(i)), 2) & " "
                Next
            End If
            Close #f
            Hx = "[" & Trim$(r) & "]"
        End Function

        Public Function Where(ByVal n As Integer) As String
            Dim r As String, v As Variant
            On Error Resume Next
            v = "?": v = LOF(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            r = " LOF " & v
            v = "?": v = Loc(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            r = r & " Loc " & v
            v = "?": v = Seek(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            r = r & " Seek " & v
            v = "?": v = EOF(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            Where = r & " EOF " & v
        End Function

        Public Sub MakeHex(ByVal name As String, ByVal hx As String)
            Dim f As Integer, b() As Byte, i As Long, n As Long
            Fresh name
            hx = Replace(hx, " ", "")
            n = Len(hx) \ 2
            f = FreeFile
            Open P(name) For Binary As #f
            If n > 0 Then
                ReDim b(0 To n - 1)
                For i = 0 To n - 1
                    b(i) = CByte("&H" & Mid$(hx, i * 2 + 1, 2))
                Next
                Put #f, 1, b
            End If
            Close #f
        End Sub

        Public Function Bounds(a As Variant) As String
            Dim r As String
            On Error Resume Next
            r = LBound(a) & " To " & UBound(a)
            If Err.Number Then r = "err" & Err.Number: Err.Clear
            Bounds = r
        End Function

        Public Sub LockedGet(x As Integer)
            On Error Resume Next
            Get #1, 1, gi
            L "  Random Get into the array while its element is passed ByRef:" & E() & " bounds " & Bounds(gi)
        End Sub

        Public Sub LockedBinaryGet(x As Integer)
            Get #1, 1, gi
            L "  Binary Get into the array while its element is passed ByRef:" & E() & " gi(0) " & gi(0)
        End Sub

        Public Function Show(ByVal s As String) As String
            Dim i As Long, c As String, r As String
            For i = 1 To Len(s)
                c = Mid$(s, i, 1)
                If AscW(c) < 32 Or AscW(c) > 126 Then r = r & "<" & AscW(c) & ">" Else r = r & c
            Next
            Show = "[" & r & "]"
        End Function
        """;

    // The Types and helpers of the probes for EOF_after_a_Put_and_what_a_failed_field_is_left_as_vb6_exe_did, After_a_read_past_the_end_the_next_Get_or_Put_is_at_the_end_as_vb6_exe_did.
    private string Probe6 => $$"""

        Private Type TRec
            i As Integer
            s As String
            l As Long
        End Type

        Private Type TVar
            v As Variant
            b As Byte
        End Type

        Private Type TMix
            b As Byte
            l As Long
        End Type

        Private Type TOuter
            a As Integer
            inner As TRec
            z As Byte
        End Type

        Private Type TVarS
            v As Variant
        End Type

        Private Type TAll
            bo As Boolean
            cu As Currency
            dt As Date
            si As Single
            db As Double
        End Type

        Private Type TIS
            i As Integer
            s As String
        End Type

        Public Sub L(ByVal s As String)
            Debug.Print s
        End Sub

        Public Function P(ByVal name As String) As String
            P = "{{Folder}}" & name
        End Function

        Public Function E() As String
            E = " err " & Err.Number
            Err.Clear
        End Function

        Public Sub Fresh(ByVal name As String)
            On Error Resume Next
            Kill P(name)
            Err.Clear
        End Sub

        Public Function Hx(ByVal name As String) As String
            Dim f As Integer, b() As Byte, i As Long, r As String
            f = FreeFile
            Open P(name) For Binary As #f
            If LOF(f) > 0 Then
                ReDim b(0 To LOF(f) - 1)
                Get #f, 1, b
                For i = 0 To UBound(b)
                    r = r & Right$("0" & Hex$(b(i)), 2) & " "
                Next
            End If
            Close #f
            Hx = "[" & Trim$(r) & "]"
        End Function

        Public Function Where(ByVal n As Integer) As String
            Dim r As String, v As Variant
            On Error Resume Next
            v = "?": v = LOF(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            r = " LOF " & v
            v = "?": v = Loc(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            r = r & " Loc " & v
            v = "?": v = Seek(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            r = r & " Seek " & v
            v = "?": v = EOF(n): If Err.Number Then v = "err" & Err.Number: Err.Clear
            Where = r & " EOF " & v
        End Function

        Public Sub MakeHex(ByVal name As String, ByVal hx As String)
            Dim f As Integer, b() As Byte, i As Long, n As Long
            Fresh name
            hx = Replace(hx, " ", "")
            n = Len(hx) \ 2
            f = FreeFile
            Open P(name) For Binary As #f
            If n > 0 Then
                ReDim b(0 To n - 1)
                For i = 0 To n - 1
                    b(i) = CByte("&H" & Mid$(hx, i * 2 + 1, 2))
                Next
                Put #f, 1, b
            End If
            Close #f
        End Sub

        Public Function Bounds(a As Variant) As String
            Dim r As String
            On Error Resume Next
            r = LBound(a) & " To " & UBound(a)
            If Err.Number Then r = "err" & Err.Number: Err.Clear
            Bounds = r
        End Function
        """;

    // each scalar type, Variants of each, arrays and Types, written alone to a Binary file.
    [Fact]
    public async Task Binary_Put_writes_each_type_as_vb6_exe_did()
    {
        using var locale = VbLocale.Use(EnGb);
        await Run(Probe1 + Environment.NewLine + """""
            Sub Main()
                Dim t As String
                On Error Resume Next
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x1 As Integer
                x1 = 1
                Put #1, , x1
                t = E()
                L "Binary Put Integer 1:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x2 As Integer
                x2 = -2
                Put #1, , x2
                t = E()
                L "Binary Put Integer -2:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x3 As Long
                x3 = &H1020304
                Put #1, , x3
                t = E()
                L "Binary Put Long &H01020304:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x4 As Single
                x4 = 1.5
                Put #1, , x4
                t = E()
                L "Binary Put Single 1.5:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x5 As Double
                x5 = 1.5
                Put #1, , x5
                t = E()
                L "Binary Put Double 1.5:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x6 As Currency
                x6 = 1.5
                Put #1, , x6
                t = E()
                L "Binary Put Currency 1.5:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x7 As Date
                x7 = #1/2/2003#
                Put #1, , x7
                t = E()
                L "Binary Put Date #1/2/2003#:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x8 As Boolean
                x8 = True
                Put #1, , x8
                t = E()
                L "Binary Put Boolean True:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x9 As Boolean
                x9 = False
                Put #1, , x9
                t = E()
                L "Binary Put Boolean False:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x10 As Byte
                x10 = 200
                Put #1, , x10
                t = E()
                L "Binary Put Byte 200:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x11 As String
                x11 = "abc"
                Put #1, , x11
                t = E()
                L "Binary Put String ""abc"":" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x12 As String
                x12 = ""
                Put #1, , x12
                t = E()
                L "Binary Put String """":" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x13 As String
                x13 = "a" & Chr$(233)
                Put #1, , x13
                t = E()
                L "Binary Put String with e-acute:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x14 As String
                x14 = "a" & ChrW$(&H416)
                Put #1, , x14
                t = E()
                L "Binary Put String with Zhe:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x17 As Variant
                x17 = CInt(1)
                Put #1, , x17
                t = E()
                L "Binary Put Variant Integer 1:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x18 As Variant
                x18 = CLng(1)
                Put #1, , x18
                t = E()
                L "Binary Put Variant Long 1:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x19 As Variant
                x19 = 1.5
                Put #1, , x19
                t = E()
                L "Binary Put Variant Double 1.5:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x20 As Variant
                x20 = "abc"
                Put #1, , x20
                t = E()
                L "Binary Put Variant String ""abc"":" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x21 As Variant
                x21 = Empty
                Put #1, , x21
                t = E()
                L "Binary Put Variant Empty:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x22 As Variant
                x22 = Null
                Put #1, , x22
                t = E()
                L "Binary Put Variant Null:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x23 As Variant
                x23 = True
                Put #1, , x23
                t = E()
                L "Binary Put Variant Boolean True:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x24 As Variant
                x24 = CByte(200)
                Put #1, , x24
                t = E()
                L "Binary Put Variant Byte 200:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x25 As Variant
                x25 = #1/2/2003#
                Put #1, , x25
                t = E()
                L "Binary Put Variant Date:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x26 As Variant
                x26 = CCur(1.5)
                Put #1, , x26
                t = E()
                L "Binary Put Variant Currency 1.5:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x27 As Variant
                x27 = CSng(1.5)
                Put #1, , x27
                t = E()
                L "Binary Put Variant Single 1.5:" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Dim x28 As Variant
                x28 = Array(1, 2)
                Put #1, , x28
                t = E()
                L "Binary Put Variant Array(1, 2):" & t & Where(1) & " " & Hx("w.bin")
                Close #1
                Dim bf(2) As Byte
                bf(0) = 1: bf(1) = 2: bf(2) = 3
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , bf
                t = E()
                L "Binary Put fixed Byte array:" & t & " " & Hx("w.bin")
                Close #1
                Dim bd() As Byte
                ReDim bd(1 To 3)
                bd(1) = 4: bd(2) = 5: bd(3) = 6
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , bd
                t = E()
                L "Binary Put dynamic Byte array:" & t & " " & Hx("w.bin")
                Close #1
                Dim iff(1) As Integer
                iff(0) = 1: iff(1) = 2
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , iff
                t = E()
                L "Binary Put fixed Integer array:" & t & " " & Hx("w.bin")
                Close #1
                Dim idn() As Integer
                ReDim idn(1)
                idn(0) = 1: idn(1) = 2
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , idn
                t = E()
                L "Binary Put dynamic Integer array:" & t & " " & Hx("w.bin")
                Close #1
                Dim i2() As Integer
                ReDim i2(1, 2)
                i2(0, 0) = 1: i2(1, 0) = 2: i2(0, 1) = 3: i2(1, 2) = 9
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , i2
                t = E()
                L "Binary Put 2-D dynamic Integer array:" & t & " " & Hx("w.bin")
                Close #1
                Dim sa(1) As String
                sa(0) = "ab": sa(1) = "c"
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , sa
                t = E()
                L "Binary Put fixed String array:" & t & " " & Hx("w.bin")
                Close #1
                Dim r As TRec
                r.i = 1: r.s = "xy": r.l = 2
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , r
                t = E()
                L "Binary Put TRec(1, ""xy"", 2):" & t & " " & Hx("w.bin")
                Close #1
                Dim rv As TVar
                rv.v = CInt(7): rv.b = 9
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , rv
                t = E()
                L "Binary Put TVar(Integer 7, 9):" & t & " " & Hx("w.bin")
                Close #1
                L "done"
            End Sub

            Main
            """"");

        Printed().Should().Equal(
            "Binary Put Integer 1: err 0 LOF 2 Loc 2 Seek 3 EOF False [01 00]",
            "Binary Put Integer -2: err 0 LOF 2 Loc 2 Seek 3 EOF False [FE FF]",
            "Binary Put Long &H01020304: err 0 LOF 4 Loc 4 Seek 5 EOF False [04 03 02 01]",
            "Binary Put Single 1.5: err 0 LOF 4 Loc 4 Seek 5 EOF False [00 00 C0 3F]",
            "Binary Put Double 1.5: err 0 LOF 8 Loc 8 Seek 9 EOF False [00 00 00 00 00 00 F8 3F]",
            "Binary Put Currency 1.5: err 0 LOF 8 Loc 8 Seek 9 EOF False [98 3A 00 00 00 00 00 00]",
            "Binary Put Date #1/2/2003#: err 0 LOF 8 Loc 8 Seek 9 EOF False [00 00 00 00 E0 5E E2 40]",
            "Binary Put Boolean True: err 0 LOF 2 Loc 2 Seek 3 EOF False [FF FF]",
            "Binary Put Boolean False: err 0 LOF 2 Loc 2 Seek 3 EOF False [00 00]",
            "Binary Put Byte 200: err 0 LOF 1 Loc 1 Seek 2 EOF False [C8]",
            "Binary Put String \"abc\": err 0 LOF 3 Loc 3 Seek 4 EOF False [61 62 63]",
            "Binary Put String \"\": err 0 LOF 0 Loc 0 Seek 1 EOF False []",
            "Binary Put String with e-acute: err 0 LOF 2 Loc 2 Seek 3 EOF False [61 E9]",
            "Binary Put String with Zhe: err 0 LOF 2 Loc 2 Seek 3 EOF False [61 3F]",
            "Binary Put Variant Integer 1: err 0 LOF 4 Loc 4 Seek 5 EOF False [02 00 01 00]",
            "Binary Put Variant Long 1: err 0 LOF 6 Loc 6 Seek 7 EOF False [03 00 01 00 00 00]",
            "Binary Put Variant Double 1.5: err 0 LOF 10 Loc 10 Seek 11 EOF False [05 00 00 00 00 00 00 00 F8 3F]",
            "Binary Put Variant String \"abc\": err 0 LOF 7 Loc 7 Seek 8 EOF False [08 00 03 00 61 62 63]",
            "Binary Put Variant Empty: err 0 LOF 2 Loc 2 Seek 3 EOF False [00 00]",
            "Binary Put Variant Null: err 0 LOF 2 Loc 2 Seek 3 EOF False [01 00]",
            "Binary Put Variant Boolean True: err 0 LOF 4 Loc 4 Seek 5 EOF False [0B 00 FF FF]",
            "Binary Put Variant Byte 200: err 0 LOF 3 Loc 3 Seek 4 EOF False [11 00 C8]",
            "Binary Put Variant Date: err 0 LOF 10 Loc 10 Seek 11 EOF False [07 00 00 00 00 00 E0 5E E2 40]",
            "Binary Put Variant Currency 1.5: err 0 LOF 10 Loc 10 Seek 11 EOF False [06 00 98 3A 00 00 00 00 00 00]",
            "Binary Put Variant Single 1.5: err 0 LOF 6 Loc 6 Seek 7 EOF False [04 00 00 00 C0 3F]",
            "Binary Put Variant Array(1, 2): err 0 LOF 20 Loc 20 Seek 21 EOF False [0C 20 01 00 02 00 00 00 00 00 00 00 02 00 01 00 02 00 02 00]",
            "Binary Put fixed Byte array: err 0 [01 02 03]",
            "Binary Put dynamic Byte array: err 0 [04 05 06]",
            "Binary Put fixed Integer array: err 0 [01 00 02 00]",
            "Binary Put dynamic Integer array: err 0 [01 00 02 00]",
            "Binary Put 2-D dynamic Integer array: err 0 [01 00 02 00 03 00 00 00 00 00 09 00]",
            "Binary Put fixed String array: err 0 [02 00 61 62 01 00 63]",
            "Binary Put TRec(1, \"xy\", 2): err 0 [01 00 02 00 78 79 02 00 00 00]",
            "Binary Put TVar(Integer 7, 9): err 0 [02 00 07 00 09]",
            "done");
    }

    // byte positions, Get of each type, past the end, Input and Line Input on Binary.
    [Fact]
    public async Task Binary_positions_reads_and_Variants_read_back_as_vb6_exe_did()
    {
        using var locale = VbLocale.Use(EnGb);
        await Run(Probe2 + Environment.NewLine + """
            Sub Main()
                Dim t As String
                On Error Resume Next
                Dim ia As Integer, la As Long, s As String, vv As Variant, bb() As Byte, ok As Boolean
                Dim r As TRec, idn() As Integer
                ReDim idn(1)
                idn(0) = 1: idn(1) = 2
                Fresh "p.bin"
                Open P("p.bin") For Binary As #1
                L "Binary new file:" & E() & Where(1)
                ia = 513
                Put #1, 5, ia
                L "Put #1, 5, Integer 513:" & E() & Where(1) & " " & Hx("p.bin")
                la = 7
                Put #1, , la
                L "then Put #1, , Long 7:" & E() & Where(1) & " " & Hx("p.bin")
                Seek #1, 2
                L "Seek #1, 2:" & E() & Where(1)
                ia = -1
                Put #1, , ia
                L "then Put Integer -1:" & E() & Where(1) & " " & Hx("p.bin")
                Put #1, 0, ia
                L "Put #1, 0:" & E() & Where(1)
                Close #1
                Fresh "r.bin"
                Open P("r.bin") For Binary As #1
                s = Chr$(1) & Chr$(2) & Chr$(3) & Chr$(4) & Chr$(5) & Chr$(6) & Chr$(7) & Chr$(8) & Chr$(9) & Chr$(10)
                Put #1, 1, s
                Close #1
                L "r.bin:" & E() & " " & Hx("r.bin")
                Open P("r.bin") For Binary As #1
                L "open r.bin:" & E() & Where(1)
                Get #1, 1, ia
                L "Get #1, 1, Integer: " & ia & E() & Where(1)
                Get #1, , la
                L "then Get Long: " & la & E() & Where(1)
                s = Space$(3)
                Get #1, 2, s
                L "Get #1, 2, a 3-character String: " & Asc(Mid$(s, 1, 1)) & " " & Asc(Mid$(s, 3, 1)) & " Len " & Len(s) & E() & Where(1)
                s = ""
                Get #1, 2, s
                L "Get #1, 2, an empty String: Len " & Len(s) & E() & Where(1)
                la = -5
                Get #1, 9, la
                L "Get #1, 9, Long, 2 bytes left: " & la & E() & Where(1)
                ia = -5
                Get #1, 20, ia
                L "Get #1, 20, Integer, past the end: " & ia & E() & Where(1)
                ia = -5
                Get #1, 11, ia
                L "Get #1, 11, Integer, just past: " & ia & E() & Where(1)
                ReDim bb(0 To 3)
                Get #1, 1, bb
                L "Get #1, 1, Byte array 0 To 3: " & bb(0) & " " & bb(3) & E() & Where(1)
                Get #1, 0, ia
                L "Get #1, 0:" & E() & Where(1)
                Seek #1, 1
                s = Input(3, #1)
                L "Input(3) on Binary: " & Asc(Mid$(s, 1, 1)) & " " & Len(s) & E() & Where(1)
                Seek #1, 1
                Line Input #1, s
                L "Line Input on Binary: Len " & Len(s) & E() & Where(1)
                Print #1, "zz"
                L "Print # on Binary:" & E() & Where(1)
                Close #1
                L "r.bin after:" & E() & " " & Hx("r.bin")
                Fresh "v.bin"
                Open P("v.bin") For Binary As #1
                vv = "hello"
                Put #1, , vv
                vv = CLng(42)
                Put #1, , vv
                vv = Null
                Put #1, , vv
                r.i = 3: r.s = "pq": r.l = 4
                Put #1, , r
                Seek #1, 1
                vv = Empty
                Get #1, , vv
                L "Get Variant: " & TypeName(vv) & " " & vv & E() & Where(1)
                Get #1, , vv
                L "Get Variant: " & TypeName(vv) & " " & vv & E() & Where(1)
                Get #1, , vv
                L "Get Variant: " & TypeName(vv) & E() & Where(1)
                Dim r2 As TRec
                Get #1, , r2
                L "Get TRec: " & r2.i & " [" & r2.s & "] " & r2.l & E() & Where(1)
                Close #1
                Fresh "d.bin"
                Open P("d.bin") For Binary As #1
                Put #1, , idn
                Seek #1, 1
                Dim id2() As Integer
                Get #1, , id2
                L "Get unallocated dynamic array:" & E() & Where(1)
                ReDim id2(1)
                Seek #1, 1
                Get #1, , id2
                L "Get allocated dynamic array (0 To 1): " & id2(0) & " " & id2(1) & E() & Where(1)
                Close #1
                Fresh "rs.bin"
                Open P("rs.bin") For Binary As #1
                r.s = "xy"
                Put #1, , r
                Seek #1, 1
                r2.s = "": r2.i = 0
                Get #1, , r2
                L "Get TRec, its String empty before: [" & r2.s & "]" & E() & Where(1)
                Close #1
                L "done"
            End Sub

            Main
            """);

        Printed().Should().Equal(
            "Binary new file: err 0 LOF 0 Loc 0 Seek 1 EOF False",
            "Put #1, 5, Integer 513: err 0 LOF 6 Loc 6 Seek 7 EOF False [00 00 00 00 01 02]",
            "then Put #1, , Long 7: err 0 LOF 10 Loc 10 Seek 11 EOF False [00 00 00 00 01 02 07 00 00 00]",
            "Seek #1, 2: err 0 LOF 10 Loc 1 Seek 2 EOF False",
            "then Put Integer -1: err 0 LOF 10 Loc 3 Seek 4 EOF False [00 FF FF 00 01 02 07 00 00 00]",
            "Put #1, 0: err 63 LOF 10 Loc 3 Seek 4 EOF False",
            "r.bin: err 0 [01 02 03 04 05 06 07 08 09 0A]",
            "open r.bin: err 0 LOF 10 Loc 0 Seek 1 EOF False",
            "Get #1, 1, Integer: 513 err 0 LOF 10 Loc 2 Seek 3 EOF False",
            "then Get Long: 100992003 err 0 LOF 10 Loc 6 Seek 7 EOF False",
            "Get #1, 2, a 3-character String: 2 4 Len 3 err 0 LOF 10 Loc 4 Seek 5 EOF False",
            "Get #1, 2, an empty String: Len 0 err 0 LOF 10 Loc 1 Seek 2 EOF False",
            "Get #1, 9, Long, 2 bytes left: 2569 err 0 LOF 10 Loc 12 Seek 13 EOF True",
            "Get #1, 20, Integer, past the end: 0 err 0 LOF 10 Loc 21 Seek 22 EOF True",
            "Get #1, 11, Integer, just past: 0 err 0 LOF 10 Loc 12 Seek 13 EOF True",
            "Get #1, 1, Byte array 0 To 3: 1 4 err 0 LOF 10 Loc 4 Seek 5 EOF False",
            "Get #1, 0: err 63 LOF 10 Loc 4 Seek 5 EOF False",
            "Input(3) on Binary: 1 3 err 0 LOF 10 Loc 3 Seek 4 EOF False",
            "Line Input on Binary: Len 10 err 0 LOF 10 Loc 10 Seek 11 EOF True",
            "Print # on Binary: err 54 LOF 10 Loc 10 Seek 11 EOF True",
            "r.bin after: err 0 [01 02 03 04 05 06 07 08 09 0A]",
            "Get Variant: String hello err 0 LOF 27 Loc 9 Seek 10 EOF False",
            "Get Variant: Long 42 err 0 LOF 27 Loc 15 Seek 16 EOF False",
            "Get Variant: Null err 0 LOF 27 Loc 17 Seek 18 EOF False",
            "Get TRec: 3 [pq] 4 err 0 LOF 27 Loc 27 Seek 28 EOF False",
            "Get unallocated dynamic array: err 0 LOF 4 Loc 0 Seek 1 EOF False",
            "Get allocated dynamic array (0 To 1): 1 2 err 0 LOF 4 Loc 4 Seek 5 EOF False",
            "Get TRec, its String empty before: [xy] err 0 LOF 10 Loc 10 Seek 11 EOF False",
            "done");
    }

    // records, Len, 59, gaps, Get past the end, Access and a second handle.
    [Fact]
    public async Task Random_records_lengths_and_access_as_vb6_exe_did()
    {
        using var locale = VbLocale.Use(EnGb);
        await Run(Probe2 + Environment.NewLine + """
            Sub Main()
                Dim t As String
                On Error Resume Next
                Dim ra As Integer, rl As Long, rs As String, rv2 As Variant, rd As Double
                Dim r As TRec
                Fresh "q.dat"
                Open P("q.dat") For Random As #1 Len = 8
                L "Random Len 8 new:" & E() & Where(1)
                ra = 1
                Put #1, 1, ra
                L "Put #1, 1, Integer 1:" & E() & Where(1) & " " & Hx("q.dat")
                rl = 2
                Put #1, 3, rl
                L "Put #1, 3, Long 2:" & E() & Where(1) & " " & Hx("q.dat")
                ra = 5
                Put #1, , ra
                L "Put #1, , Integer 5:" & E() & Where(1) & " " & Hx("q.dat")
                rs = "abc"
                Put #1, 6, rs
                L "Put #1, 6, String abc:" & E() & Where(1) & " " & Hx("q.dat")
                rs = "abcdefgh"
                Put #1, 7, rs
                L "Put #1, 7, an 8-character String:" & E() & Where(1)
                rs = "abcdef"
                Put #1, 7, rs
                L "Put #1, 7, a 6-character String:" & E() & Where(1) & " " & Hx("q.dat")
                rv2 = CInt(9)
                Put #1, 8, rv2
                L "Put #1, 8, Variant Integer 9:" & E() & Where(1) & " " & Hx("q.dat")
                rv2 = "abcd"
                Put #1, 9, rv2
                L "Put #1, 9, Variant String abcd:" & E() & Where(1) & " " & Hx("q.dat")
                rv2 = "abcde"
                Put #1, 10, rv2
                L "Put #1, 10, Variant String abcde:" & E() & Where(1)
                rd = 1.5
                Put #1, 10, rd
                L "Put #1, 10, Double:" & E() & Where(1)
                Put #1, 0, ra
                L "Put #1, 0:" & E() & Where(1)
                ra = -7
                Get #1, 2, ra
                L "Get #1, 2, Integer, a gap record: " & ra & E() & Where(1)
                rl = -7
                Get #1, 3, rl
                L "Get #1, 3, Long: " & rl & E() & Where(1)
                rs = "zzz"
                Get #1, 6, rs
                L "Get #1, 6, String: [" & rs & "]" & E() & Where(1)
                rv2 = Empty
                Get #1, 9, rv2
                L "Get #1, 9, Variant: " & TypeName(rv2) & " [" & rv2 & "]" & E() & Where(1)
                ra = -7
                Get #1, 50, ra
                L "Get #1, 50, Integer, past the end: " & ra & E() & Where(1)
                Get #1, 0, ra
                L "Get #1, 0:" & E() & Where(1)
                rs = Input(2, #1)
                L "Input on Random:" & E() & Where(1)
                Line Input #1, rs
                L "Line Input on Random:" & E() & Where(1)
                Print #1, "x"
                L "Print # on Random:" & E() & Where(1)
                Seek #1, 2
                L "Seek #1, 2 on Random:" & E() & Where(1)
                Close #1
                Fresh "q2.dat"
                Open P("q2.dat") For Random As #1
                ra = 3
                Put #1, 2, ra
                L "Random, no Len, Put #1, 2:" & E() & Where(1)
                Close #1
                L "q2.dat LOF " & FileLen(P("q2.dat"))
                Fresh "q3.dat"
                Open P("q3.dat") For Random As #1 Len = 12
                r.i = 1: r.s = "xy": r.l = 2
                Put #1, 1, r
                L "Random Len 12, Put TRec(1, ""xy"", 2):" & E() & Where(1) & " " & Hx("q3.dat")
                r.s = "abcdefg"
                Put #1, 2, r
                L "Random Len 12, Put TRec with a 7-character String:" & E() & Where(1)
                Close #1
                Fresh "a.bin"
                Open P("a.bin") For Binary Access Read As #1
                L "Binary Access Read, a missing file:" & E() & Where(1)
                Close #1
                Fresh "a.bin"
                Open P("a.bin") For Binary As #1
                Put #1, , ra
                Close #1
                Open P("a.bin") For Binary Access Read As #1
                Put #1, 1, ra
                L "Put on Access Read:" & E() & Where(1)
                Get #1, 1, ra
                L "Get on Access Read: " & ra & E()
                Close #1
                Open P("a.bin") For Binary Access Write As #1
                Get #1, 1, ra
                L "Get on Access Write:" & E() & Where(1)
                Close #1
                Open P("a.bin") For Binary As #1
                Open P("a.bin") For Binary As #2
                L "second Binary on one file:" & E()
                ra = 77
                Put #2, 1, ra
                Get #1, 1, ra
                L "Put on #2, Get on #1: " & ra & E()
                Close #1
                Close #2
                Open P("a.bin") For Binary Lock Read Write As #1
                Open P("a.bin") For Binary As #2
                L "second Binary beside Lock Read Write:" & E()
                Close
                Open P("a.bin") For Binary As #1
                Open P("a.bin") For Input As #2
                L "Input beside Binary:" & E()
                Close
                Open P("a.bin") For Binary As #1
                Open P("a.bin") For Random As #2
                L "Random beside Binary:" & E()
                Close
                Open P("a.bin") For Binary As #1 Len = 3
                L "Binary Len = 3:" & E() & Where(1)
                Close
                L "done"
            End Sub

            Main
            """);

        Printed().Should().Equal(
            "Random Len 8 new: err 0 LOF 0 Loc 0 Seek 1 EOF False",
            "Put #1, 1, Integer 1: err 0 LOF 2 Loc 1 Seek 2 EOF False [01 00]",
            "Put #1, 3, Long 2: err 0 LOF 20 Loc 3 Seek 4 EOF False [01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 02 00 00 00]",
            "Put #1, , Integer 5: err 0 LOF 26 Loc 4 Seek 5 EOF False [01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 02 00 00 00 00 00 00 00 05 00]",
            "Put #1, 6, String abc: err 0 LOF 45 Loc 6 Seek 7 EOF False [01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 02 00 00 00 00 00 00 00 05 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 03 00 61 62 63]",
            "Put #1, 7, an 8-character String: err 59 LOF 45 Loc 6 Seek 7 EOF False",
            "Put #1, 7, a 6-character String: err 0 LOF 56 Loc 7 Seek 8 EOF False [01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 02 00 00 00 00 00 00 00 05 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 03 00 61 62 63 00 00 00 06 00 61 62 63 64 65 66]",
            "Put #1, 8, Variant Integer 9: err 0 LOF 60 Loc 8 Seek 9 EOF False [01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 02 00 00 00 00 00 00 00 05 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 03 00 61 62 63 00 00 00 06 00 61 62 63 64 65 66 02 00 09 00]",
            "Put #1, 9, Variant String abcd: err 0 LOF 72 Loc 9 Seek 10 EOF False [01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 02 00 00 00 00 00 00 00 05 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 03 00 61 62 63 00 00 00 06 00 61 62 63 64 65 66 02 00 09 00 00 00 00 00 08 00 04 00 61 62 63 64]",
            "Put #1, 10, Variant String abcde: err 59 LOF 72 Loc 9 Seek 10 EOF False",
            "Put #1, 10, Double: err 0 LOF 80 Loc 10 Seek 11 EOF False",
            "Put #1, 0: err 63 LOF 80 Loc 10 Seek 11 EOF False",
            "Get #1, 2, Integer, a gap record: 0 err 0 LOF 80 Loc 2 Seek 3 EOF False",
            "Get #1, 3, Long: 2 err 0 LOF 80 Loc 3 Seek 4 EOF False",
            "Get #1, 6, String: [abc] err 0 LOF 80 Loc 6 Seek 7 EOF False",
            "Get #1, 9, Variant: String [abcd] err 0 LOF 80 Loc 9 Seek 10 EOF False",
            "Get #1, 50, Integer, past the end: 0 err 0 LOF 80 Loc 50 Seek 51 EOF True",
            "Get #1, 0: err 63 LOF 80 Loc 50 Seek 51 EOF True",
            "Input on Random: err 54 LOF 80 Loc 50 Seek 51 EOF True",
            "Line Input on Random: err 54 LOF 80 Loc 50 Seek 51 EOF True",
            "Print # on Random: err 54 LOF 80 Loc 50 Seek 51 EOF True",
            "Seek #1, 2 on Random: err 0 LOF 80 Loc 1 Seek 2 EOF False",
            "Random, no Len, Put #1, 2: err 0 LOF 130 Loc 2 Seek 3 EOF False",
            "q2.dat LOF 130",
            "Random Len 12, Put TRec(1, \"xy\", 2): err 0 LOF 10 Loc 1 Seek 2 EOF False [01 00 02 00 78 79 02 00 00 00]",
            "Random Len 12, Put TRec with a 7-character String: err 59 LOF 10 Loc 1 Seek 2 EOF False",
            "Binary Access Read, a missing file: err 0 LOF 0 Loc 0 Seek 1 EOF False",
            "Put on Access Read: err 75 LOF 2 Loc 0 Seek 1 EOF False",
            "Get on Access Read: 3 err 0",
            "Get on Access Write: err 75 LOF 2 Loc 0 Seek 1 EOF False",
            "second Binary on one file: err 0",
            "Put on #2, Get on #1: 77 err 0",
            "second Binary beside Lock Read Write: err 70",
            "Input beside Binary: err 55",
            "Random beside Binary: err 0",
            "Binary Len = 3: err 0 LOF 2 Loc 0 Seek 1 EOF False",
            "done");
    }

    // EOF after each kind of read, Seek past the end, and Len from 1 to 65536.
    [Fact]
    public async Task EOF_and_Len_on_Binary_and_Random_as_vb6_exe_did()
    {
        using var locale = VbLocale.Use(EnGb);
        await Run(Probe2 + Environment.NewLine + """
            Sub Main()
                Dim t As String
                On Error Resume Next
                Dim ia As Integer, la As Long, bb As Byte, s As String
                MakeHex "r.bin", "01 02 03 04 05 06 07 08 09 0A"
                Open P("r.bin") For Binary As #1
                L "Binary open 10 bytes:" & E() & Where(1)
                Get #1, 9, ia
                L "Get #1, 9, Integer, to the end exactly: " & ia & E() & Where(1)
                Get #1, 10, bb
                L "Get #1, 10, Byte, the last: " & bb & E() & Where(1)
                bb = 99
                Get #1, 11, bb
                L "Get #1, 11, Byte, just past: " & bb & E() & Where(1)
                Seek #1, 10
                L "Seek #1, 10:" & E() & Where(1)
                Seek #1, 11
                L "Seek #1, 11:" & E() & Where(1)
                Seek #1, 100
                L "Seek #1, 100:" & E() & Where(1)
                bb = 7
                Put #1, , bb
                L "Put Byte at 100:" & E() & Where(1)
                Get #1, 2, bb
                L "then Get #1, 2, Byte: " & bb & E() & Where(1)
                Seek #1, 1
                s = Input(10, #1)
                L "Input(10) from 1: Len " & Len(s) & E() & Where(1)
                Seek #1, 99
                s = Input(1, #1)
                L "Input(1) at 99, a NUL: " & Asc(s) & E() & Where(1)
                s = Input(5, #1)
                L "Input(5) at 100, 1 byte left: [" & s & "]" & E() & Where(1)
                Close #1
                MakeHex "e.bin", ""
                Open P("e.bin") For Binary As #1
                bb = 9
                Get #1, , bb
                L "empty Binary, Get Byte: " & bb & E() & Where(1)
                Close #1
                MakeHex "q.dat", "01 00 00 00 02 00 00 00 03 00"
                Open P("q.dat") For Random As #1 Len = 4
                L "Random Len 4 on 10 bytes:" & E() & Where(1)
                Get #1, 2, la
                L "Get #1, 2, Long: " & la & E() & Where(1)
                la = -1
                Get #1, 3, la
                L "Get #1, 3, Long, 2 bytes there: " & la & E() & Where(1)
                Get #1, 1, la
                L "Get #1, 1, Long: " & la & E() & Where(1)
                Get #1, , ia
                L "Get #1, , Integer: " & ia & E() & Where(1)
                la = -1
                Get #1, 4, la
                L "Get #1, 4, Long, past: " & la & E() & Where(1)
                Close #1
                Fresh "len.dat"
                Open P("len.dat") For Random As #1 Len = 1
                L "Random Len = 1:" & E() & Where(1)
                ia = 5
                Put #1, 2, ia
                L "Len = 1, Put #1, 2, Integer:" & E() & Where(1)
                Close #1
                L "Len = 1 file:" & E() & " " & FileLen(P("len.dat"))
                Fresh "len.dat"
                Open P("len.dat") For Random As #1 Len = -1
                L "Random Len = -1:" & E() & Where(1)
                ia = 5
                Put #1, 2, ia
                L "Len = -1, Put #1, 2, Integer:" & E() & Where(1)
                Close #1
                L "Len = -1 file:" & E() & " " & FileLen(P("len.dat"))
                Fresh "len.dat"
                Open P("len.dat") For Random As #1 Len = 32767
                L "Random Len = 32767:" & E() & Where(1)
                ia = 5
                Put #1, 2, ia
                L "Len = 32767, Put #1, 2, Integer:" & E() & Where(1)
                Close #1
                L "Len = 32767 file:" & E() & " " & FileLen(P("len.dat"))
                Fresh "len.dat"
                Open P("len.dat") For Random As #1 Len = 32768
                L "Random Len = 32768:" & E() & Where(1)
                ia = 5
                Put #1, 2, ia
                L "Len = 32768, Put #1, 2, Integer:" & E() & Where(1)
                Close #1
                L "Len = 32768 file:" & E() & " " & FileLen(P("len.dat"))
                Fresh "len.dat"
                Open P("len.dat") For Random As #1 Len = 65535
                L "Random Len = 65535:" & E() & Where(1)
                ia = 5
                Put #1, 2, ia
                L "Len = 65535, Put #1, 2, Integer:" & E() & Where(1)
                Close #1
                L "Len = 65535 file:" & E() & " " & FileLen(P("len.dat"))
                Fresh "len.dat"
                Open P("len.dat") For Random As #1 Len = 65536
                L "Random Len = 65536:" & E() & Where(1)
                ia = 5
                Put #1, 2, ia
                L "Len = 65536, Put #1, 2, Integer:" & E() & Where(1)
                Close #1
                L "Len = 65536 file:" & E() & " " & FileLen(P("len.dat"))
                Fresh "len.dat"
                Open P("len.dat") For Random As #1 Len = -32768
                L "Random Len = -32768:" & E() & Where(1)
                ia = 5
                Put #1, 2, ia
                L "Len = -32768, Put #1, 2, Integer:" & E() & Where(1)
                Close #1
                L "Len = -32768 file:" & E() & " " & FileLen(P("len.dat"))
                Fresh "len.dat"
                Open P("len.dat") For Random As #1 Len = 2
                L "Random Len = 2:" & E() & Where(1)
                ia = 5
                Put #1, 2, ia
                L "Len = 2, Put #1, 2, Integer:" & E() & Where(1)
                Close #1
                L "Len = 2 file:" & E() & " " & FileLen(P("len.dat"))
                L "done"
            End Sub

            Main
            """);

        Printed().Should().Equal(
            "Binary open 10 bytes: err 0 LOF 10 Loc 0 Seek 1 EOF False",
            "Get #1, 9, Integer, to the end exactly: 2569 err 0 LOF 10 Loc 10 Seek 11 EOF False",
            "Get #1, 10, Byte, the last: 10 err 0 LOF 10 Loc 10 Seek 11 EOF False",
            "Get #1, 11, Byte, just past: 0 err 0 LOF 10 Loc 11 Seek 12 EOF True",
            "Seek #1, 10: err 0 LOF 10 Loc 9 Seek 10 EOF False",
            "Seek #1, 11: err 0 LOF 10 Loc 10 Seek 11 EOF False",
            "Seek #1, 100: err 0 LOF 10 Loc 99 Seek 100 EOF False",
            "Put Byte at 100: err 0 LOF 100 Loc 100 Seek 101 EOF False",
            "then Get #1, 2, Byte: 2 err 0 LOF 100 Loc 2 Seek 3 EOF False",
            "Input(10) from 1: Len 10 err 0 LOF 100 Loc 10 Seek 11 EOF False",
            "Input(1) at 99, a NUL: 0 err 0 LOF 100 Loc 99 Seek 100 EOF False",
            "Input(5) at 100, 1 byte left: [\u0007] err 0 LOF 100 Loc 100 Seek 101 EOF True",
            "empty Binary, Get Byte: 0 err 0 LOF 0 Loc 1 Seek 2 EOF True",
            "Random Len 4 on 10 bytes: err 0 LOF 10 Loc 0 Seek 1 EOF False",
            "Get #1, 2, Long: 2 err 0 LOF 10 Loc 2 Seek 3 EOF False",
            "Get #1, 3, Long, 2 bytes there: 3 err 0 LOF 10 Loc 3 Seek 4 EOF True",
            "Get #1, 1, Long: 1 err 0 LOF 10 Loc 1 Seek 2 EOF False",
            "Get #1, , Integer: 2 err 0 LOF 10 Loc 2 Seek 3 EOF False",
            "Get #1, 4, Long, past: 0 err 0 LOF 10 Loc 4 Seek 5 EOF True",
            "Random Len = 1: err 0 LOF 0 Loc 0 Seek 1 EOF False",
            "Len = 1, Put #1, 2, Integer: err 59 LOF 0 Loc 0 Seek 1 EOF False",
            "Len = 1 file: err 0 0",
            "Random Len = -1: err 0 LOF 0 Loc 0 Seek 1 EOF False",
            "Len = -1, Put #1, 2, Integer: err 0 LOF 130 Loc 2 Seek 3 EOF False",
            "Len = -1 file: err 0 130",
            "Random Len = 32767: err 0 LOF 0 Loc 0 Seek 1 EOF False",
            "Len = 32767, Put #1, 2, Integer: err 0 LOF 32769 Loc 2 Seek 3 EOF False",
            "Len = 32767 file: err 0 32769",
            "Random Len = 32768: err 6 LOF err52 Loc err52 Seek err52 EOF err52",
            "Len = 32768, Put #1, 2, Integer: err 52 LOF err52 Loc err52 Seek err52 EOF err52",
            "Random Len = 65535: err 6 LOF err52 Loc err52 Seek err52 EOF err52",
            "Len = 65535, Put #1, 2, Integer: err 52 LOF err52 Loc err52 Seek err52 EOF err52",
            "Random Len = 65536: err 6 LOF err52 Loc err52 Seek err52 EOF err52",
            "Len = 65536, Put #1, 2, Integer: err 52 LOF err52 Loc err52 Seek err52 EOF err52",
            "Random Len = -32768: err 5 LOF err52 Loc err52 Seek err52 EOF err52",
            "Len = -32768, Put #1, 2, Integer: err 52 LOF err52 Loc err52 Seek err52 EOF err52",
            "Random Len = 2: err 0 LOF 0 Loc 0 Seek 1 EOF False",
            "Len = 2, Put #1, 2, Integer: err 0 LOF 4 Loc 2 Seek 3 EOF False",
            "Len = 2 file: err 0 4",
            "done");
    }

    // fixed and dynamic arrays in Random and Binary files, and in Variants.
    [Fact]
    public async Task Arrays_and_their_descriptors_as_vb6_exe_did()
    {
        using var locale = VbLocale.Use(EnGb);
        await Run(Probe2 + Environment.NewLine + """
            Sub Main()
                Dim t As String
                On Error Resume Next
                Dim fi(1) As Integer, di() As Integer, d2() As Integer, db() As Byte, ds() As String, gi() As Integer, gf(1) As Integer
                Dim v As Variant, vs() As String
                fi(0) = 1: fi(1) = 2
                ReDim di(1): di(0) = 3: di(1) = 4
                ReDim d2(1, 1): d2(0, 0) = 5: d2(1, 0) = 6: d2(0, 1) = 7: d2(1, 1) = 8
                ReDim db(1 To 2): db(1) = 9: db(2) = 10
                ReDim ds(1): ds(0) = "a": ds(1) = "bc"
                Fresh "ra.dat"
                Open P("ra.dat") For Random As #1 Len = 32
                Put #1, 1, fi
                L "Random Put fixed Integer array:" & E() & Where(1) & " " & Hx("ra.dat")
                Close #1
                Fresh "ra.dat"
                Open P("ra.dat") For Random As #1 Len = 32
                Put #1, 1, di
                L "Random Put dynamic Integer array 0 To 1:" & E() & Where(1) & " " & Hx("ra.dat")
                Put #1, 2, d2
                L "Random Put dynamic 2-D Integer array:" & E() & Where(1) & " " & Hx("ra.dat")
                Put #1, 3, db
                L "Random Put dynamic Byte array 1 To 2:" & E() & Where(1) & " " & Hx("ra.dat")
                Put #1, 4, ds
                L "Random Put dynamic String array:" & E() & Where(1) & " " & Hx("ra.dat")
                Get #1, 1, gi
                L "Random Get into unallocated dynamic array:" & E() & " bounds " & Bounds(gi)
                ReDim gi(0 To 5)
                Get #1, 1, gi
                L "Random Get into dynamic 0 To 5: bounds " & Bounds(gi) & " gi(0) " & gi(0) & " gi(1) " & gi(1) & E() & Where(1)
                ReDim gi(3 To 3)
                Get #1, 1, gi
                L "Random Get into dynamic 3 To 3: bounds " & Bounds(gi) & " gi(3) " & gi(3) & E() & Where(1)
                Get #1, 1, gf
                L "Random Get into fixed (1): " & gf(0) & " " & gf(1) & E() & Where(1)
                Close #1
                Fresh "ra.dat"
                Open P("ra.dat") For Random As #1 Len = 4
                Put #1, 1, di
                L "Random Len 4, Put dynamic Integer array (14 bytes):" & E() & Where(1)
                Put #1, 1, fi
                L "Random Len 4, Put fixed Integer array (4 bytes):" & E() & Where(1) & " " & Hx("ra.dat")
                Close #1
                Fresh "va.bin"
                Open P("va.bin") For Binary As #1
                v = di
                Put #1, , v
                L "Binary Put Variant holding Integer() 0 To 1:" & E() & " " & Hx("va.bin")
                Close #1
                Fresh "va.bin"
                Open P("va.bin") For Binary As #1
                v = ds
                Put #1, , v
                L "Binary Put Variant holding String() 0 To 1:" & E() & " " & Hx("va.bin")
                Close #1
                Fresh "va.bin"
                Open P("va.bin") For Binary As #1
                v = Array("a", 2)
                Put #1, , v
                Seek #1, 1
                v = Empty
                Get #1, , v
                L "Binary Variant Array(""a"", 2) back: " & TypeName(v) & " " & Bounds(v) & " [" & v(0) & "] " & TypeName(v(1)) & E() & Where(1)
                Close #1
                Fresh "va.bin"
                Open P("va.bin") For Binary As #1
                v = d2
                Put #1, , v
                L "Binary Put Variant holding 2-D Integer():" & E() & " " & Hx("va.bin")
                Seek #1, 1
                v = Empty
                Get #1, , v
                L "back: " & TypeName(v) & " " & v(1, 1) & E() & Where(1)
                Close #1
                Fresh "da.bin"
                Open P("da.bin") For Binary As #1
                Put #1, , di
                Seek #1, 1
                Erase gi
                Get #1, , gi
                L "Binary Get into unallocated dynamic array:" & E() & " bounds " & Bounds(gi) & Where(1)
                Close #1
                L "done"
            End Sub

            Main
            """);

        Printed().Should().Equal(
            "Random Put fixed Integer array: err 0 LOF 4 Loc 1 Seek 2 EOF False [01 00 02 00]",
            "Random Put dynamic Integer array 0 To 1: err 0 LOF 14 Loc 1 Seek 2 EOF False [01 00 02 00 00 00 00 00 00 00 03 00 04 00]",
            "Random Put dynamic 2-D Integer array: err 0 LOF 58 Loc 2 Seek 3 EOF False [01 00 02 00 00 00 00 00 00 00 03 00 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 02 00 02 00 00 00 00 00 00 00 02 00 00 00 00 00 00 00 05 00 06 00 07 00 08 00]",
            "Random Put dynamic Byte array 1 To 2: err 0 LOF 76 Loc 3 Seek 4 EOF False [01 00 02 00 00 00 00 00 00 00 03 00 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 02 00 02 00 00 00 00 00 00 00 02 00 00 00 00 00 00 00 05 00 06 00 07 00 08 00 00 00 00 00 00 00 01 00 02 00 00 00 01 00 00 00 09 0A]",
            "Random Put dynamic String array: err 0 LOF 113 Loc 4 Seek 5 EOF False [01 00 02 00 00 00 00 00 00 00 03 00 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 02 00 02 00 00 00 00 00 00 00 02 00 00 00 00 00 00 00 05 00 06 00 07 00 08 00 00 00 00 00 00 00 01 00 02 00 00 00 01 00 00 00 09 0A 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 01 00 02 00 00 00 00 00 00 00 01 00 61 02 00 62 63]",
            "Random Get into unallocated dynamic array: err 0 bounds 0 To 1",
            "Random Get into dynamic 0 To 5: bounds 0 To 1 gi(0) 3 gi(1) 4 err 0 LOF 113 Loc 1 Seek 2 EOF False",
            "Random Get into fixed (1): 1 2 err 9 LOF 113 Loc 1 Seek 2 EOF False",
            "Random Len 4, Put dynamic Integer array (14 bytes): err 59 LOF 0 Loc 0 Seek 1 EOF False",
            "Random Len 4, Put fixed Integer array (4 bytes): err 0 LOF 4 Loc 1 Seek 2 EOF False [01 00 02 00]",
            "Binary Put Variant holding Integer() 0 To 1: err 0 [02 20 01 00 02 00 00 00 00 00 00 00 03 00 04 00]",
            "Binary Put Variant holding String() 0 To 1: err 0 [08 20 01 00 02 00 00 00 00 00 00 00 01 00 61 02 00 62 63]",
            "Binary Variant Array(\"a\", 2) back: Variant() 0 To 1 [a] Integer err 0 LOF 21 Loc 21 Seek 22 EOF False",
            "Binary Put Variant holding 2-D Integer(): err 0 [02 20 02 00 02 00 00 00 00 00 00 00 02 00 00 00 00 00 00 00 05 00 06 00 07 00 08 00]",
            "back: Integer() 8 err 0 LOF 28 Loc 28 Seek 29 EOF False",
            "Binary Get into unallocated dynamic array: err 0 bounds err9 LOF 4 Loc 0 Seek 1 EOF False",
            "done");
    }

    // Variant types read from bytes, Booleans, round trips, and expressions put.
    [Fact]
    public async Task Variants_and_expressions_as_vb6_exe_did()
    {
        using var locale = VbLocale.Use(EnGb);
        await Run(Probe2 + Environment.NewLine + """
            Sub Main()
                Dim t As String
                On Error Resume Next
                Dim v As Variant, b As Boolean, ia As Integer, s As String, dt As Date, cu As Currency, sg As Single, db As Double, c As Collection
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                v = CDec(1.5)
                Put #1, , v
                L "Put Variant CDec(1.5):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Set v = Nothing
                Put #1, , v
                L "Put Variant Nothing:" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Set c = New Collection
                Set v = c
                Put #1, , v
                L "Put Variant holding a Collection:" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                v = "a" & Chr$(233) & ChrW$(&H416)
                Put #1, , v
                L "Put Variant String a, e-acute, Zhe:" & E() & " " & Hx("w.bin")
                Close #1
                MakeHex "g.bin", "09 00 00 00 00 00"
                Open P("g.bin") For Binary As #1
                v = "kept"
                Get #1, , v
                s = TypeName(v): If Err.Number = 0 Then If IsArray(v) Then s = s & " " & Bounds(v) Else If Not IsObject(v) And Not IsNull(v) And Not IsEmpty(v) And Not IsError(v) Then s = s & " " & CStr(v)
                L "Get Variant from VarType 9: " & s & E() & Where(1)
                Close #1
                MakeHex "g.bin", "0D 00 00 00 00 00"
                Open P("g.bin") For Binary As #1
                v = "kept"
                Get #1, , v
                s = TypeName(v): If Err.Number = 0 Then If IsArray(v) Then s = s & " " & Bounds(v) Else If Not IsObject(v) And Not IsNull(v) And Not IsEmpty(v) And Not IsError(v) Then s = s & " " & CStr(v)
                L "Get Variant from VarType 13: " & s & E() & Where(1)
                Close #1
                MakeHex "g.bin", "0E 00 00 00 01 00 00 00 0F 00 00 00 00 00 00 00"
                Open P("g.bin") For Binary As #1
                v = "kept"
                Get #1, , v
                s = TypeName(v): If Err.Number = 0 Then If IsArray(v) Then s = s & " " & Bounds(v) Else If Not IsObject(v) And Not IsNull(v) And Not IsEmpty(v) And Not IsError(v) Then s = s & " " & CStr(v)
                L "Get Variant from VarType 14: " & s & E() & Where(1)
                Close #1
                MakeHex "g.bin", "63 00 01 00"
                Open P("g.bin") For Binary As #1
                v = "kept"
                Get #1, , v
                s = TypeName(v): If Err.Number = 0 Then If IsArray(v) Then s = s & " " & Bounds(v) Else If Not IsObject(v) And Not IsNull(v) And Not IsEmpty(v) And Not IsError(v) Then s = s & " " & CStr(v)
                L "Get Variant from VarType 99: " & s & E() & Where(1)
                Close #1
                MakeHex "g.bin", "02 40 01 00"
                Open P("g.bin") For Binary As #1
                v = "kept"
                Get #1, , v
                s = TypeName(v): If Err.Number = 0 Then If IsArray(v) Then s = s & " " & Bounds(v) Else If Not IsObject(v) And Not IsNull(v) And Not IsEmpty(v) And Not IsError(v) Then s = s & " " & CStr(v)
                L "Get Variant from VarType &H4002: " & s & E() & Where(1)
                Close #1
                MakeHex "g.bin", "08 00 03 00 61 62"
                Open P("g.bin") For Binary As #1
                v = "kept"
                Get #1, , v
                s = TypeName(v): If Err.Number = 0 Then If IsArray(v) Then s = s & " " & Bounds(v) Else If Not IsObject(v) And Not IsNull(v) And Not IsEmpty(v) And Not IsError(v) Then s = s & " " & CStr(v)
                L "Get Variant from VarType 8 length 3 with 2 bytes: " & s & E() & Where(1)
                Close #1
                MakeHex "g.bin", "02 00 07"
                Open P("g.bin") For Binary As #1
                v = "kept"
                Get #1, , v
                s = TypeName(v): If Err.Number = 0 Then If IsArray(v) Then s = s & " " & Bounds(v) Else If Not IsObject(v) And Not IsNull(v) And Not IsEmpty(v) And Not IsError(v) Then s = s & " " & CStr(v)
                L "Get Variant from VarType 2 with 1 byte: " & s & E() & Where(1)
                Close #1
                MakeHex "g.bin", "02 20 01 00 02 00 00 00 05 00 00 00 07 00 08 00"
                Open P("g.bin") For Binary As #1
                v = "kept"
                Get #1, , v
                s = TypeName(v): If Err.Number = 0 Then If IsArray(v) Then s = s & " " & Bounds(v) Else If Not IsObject(v) And Not IsNull(v) And Not IsEmpty(v) And Not IsError(v) Then s = s & " " & CStr(v)
                L "Get Variant from VarType &H2002: " & s & E() & Where(1)
                Close #1
                MakeHex "g.bin", "11 00 C8"
                Open P("g.bin") For Binary As #1
                v = "kept"
                Get #1, , v
                s = TypeName(v): If Err.Number = 0 Then If IsArray(v) Then s = s & " " & Bounds(v) Else If Not IsObject(v) And Not IsNull(v) And Not IsEmpty(v) And Not IsError(v) Then s = s & " " & CStr(v)
                L "Get Variant from VarType 17: " & s & E() & Where(1)
                Close #1
                MakeHex "g.bin", "03 00 01 00 00 00"
                Open P("g.bin") For Binary As #1
                v = "kept"
                Get #1, , v
                s = TypeName(v): If Err.Number = 0 Then If IsArray(v) Then s = s & " " & Bounds(v) Else If Not IsObject(v) And Not IsNull(v) And Not IsEmpty(v) And Not IsError(v) Then s = s & " " & CStr(v)
                L "Get Variant from VarType 3: " & s & E() & Where(1)
                Close #1
                MakeHex "g.bin", "00 00"
                Open P("g.bin") For Binary As #1
                v = "kept"
                Get #1, , v
                s = TypeName(v): If Err.Number = 0 Then If IsArray(v) Then s = s & " " & Bounds(v) Else If Not IsObject(v) And Not IsNull(v) And Not IsEmpty(v) And Not IsError(v) Then s = s & " " & CStr(v)
                L "Get Variant from VarType 0: " & s & E() & Where(1)
                Close #1
                MakeHex "g.bin", "01 00"
                Open P("g.bin") For Binary As #1
                v = "kept"
                Get #1, , v
                s = TypeName(v): If Err.Number = 0 Then If IsArray(v) Then s = s & " " & Bounds(v) Else If Not IsObject(v) And Not IsNull(v) And Not IsEmpty(v) And Not IsError(v) Then s = s & " " & CStr(v)
                L "Get Variant from VarType 1: " & s & E() & Where(1)
                Close #1
                MakeHex "g.bin", "61 E9 62"
                Open P("g.bin") For Binary As #1
                s = Space$(3)
                Get #1, , s
                L "Get String from 61 E9 62: " & AscW(Mid$(s, 2, 1)) & E() & Where(1)
                Close #1
                Fresh "rt.bin"
                Open P("rt.bin") For Binary As #1
                dt = #1/2/2003 12:34:56 PM#: cu = -1.5: sg = 1E+30: db = -0.5
                Put #1, , dt
                Put #1, , cu
                Put #1, , sg
                Put #1, , db
                L "Date, Currency, Single, Double:" & E() & " " & Hx("rt.bin")
                dt = 0: cu = 0: sg = 0: db = 0
                Seek #1, 1
                Get #1, , dt
                Get #1, , cu
                Get #1, , sg
                Get #1, , db
                L "back: " & Format$(dt, "yyyy-mm-dd hh:nn:ss") & " " & cu & " " & sg & " " & db & E() & Where(1)
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                ia = 2
                Put #1, , 5
                L "Binary Put 5:" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                ia = 2
                Put #1, , 5&
                L "Binary Put 5&:" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                ia = 2
                Put #1, , 40000
                L "Binary Put 40000:" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                ia = 2
                Put #1, , 1.5
                L "Binary Put 1.5:" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                ia = 2
                Put #1, , 1.5!
                L "Binary Put 1.5!:" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                ia = 2
                Put #1, , "ab"
                L "Binary Put ""ab"":" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                ia = 2
                Put #1, , True
                L "Binary Put True:" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                ia = 2
                Put #1, , ia + 1
                L "Binary Put ia + 1:" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                ia = 2
                Put #1, , Len("abc")
                L "Binary Put Len(""abc""):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                ia = 2
                Put #1, , #1/2/2003#
                L "Binary Put #1/2/2003#:" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                ia = 2
                Put #1, , CByte(7)
                L "Binary Put CByte(7):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                ia = 2
                Put #1, , Null
                L "Binary Put Null:" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                ia = 2
                Put #1, , Empty
                L "Binary Put Empty:" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                ia = 2
                Put #1, , 1.5@
                L "Binary Put 1.5@:" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                ia = 2
                Put #1, , (ia)
                L "Binary Put (ia):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.dat"
                Open P("w.dat") For Random As #1 Len = 8
                Put #1, , "ab"
                L "Random Put ""ab"":" & E() & " " & Hx("w.dat")
                Close #1
                Fresh "w.dat"
                Open P("w.dat") For Random As #1 Len = 8
                Put #1, , 5
                L "Random Put 5:" & E() & " " & Hx("w.dat")
                Close #1
                L "done"
            End Sub

            Main
            """);

        Printed().Should().Equal(
            "Put Variant CDec(1.5): err 0 [0E 00 0E 00 01 00 00 00 00 00 0F 00 00 00 00 00 00 00]",
            "Put Variant Nothing: err 13 [09 00]",
            "Put Variant holding a Collection: err 13 [09 00]",
            "Put Variant String a, e-acute, Zhe: err 0 [08 00 03 00 61 E9 3F]",
            "Get Variant from VarType 9: String err 13 LOF 6 Loc 0 Seek 1 EOF False",
            "Get Variant from VarType 13: Empty err 13 LOF 6 Loc 0 Seek 1 EOF False",
            "Get Variant from VarType 14: Decimal 1813388729421943762059264 err 0 LOF 16 Loc 18 Seek 19 EOF True",
            "Get Variant from VarType 99: Empty err 458 LOF 4 Loc 0 Seek 1 EOF False",
            "Get Variant from VarType &H4002: Empty err 458 LOF 4 Loc 0 Seek 1 EOF False",
            "Get Variant from VarType 8 length 3 with 2 bytes: String ab\u0000 err 0 LOF 6 Loc 7 Seek 8 EOF True",
            "Get Variant from VarType 2 with 1 byte: Integer 7 err 0 LOF 3 Loc 4 Seek 5 EOF True",
            "Get Variant from VarType &H2002: Integer() 5 To 6 err 0 LOF 16 Loc 16 Seek 17 EOF False",
            "Get Variant from VarType 17: Byte 200 err 0 LOF 3 Loc 3 Seek 4 EOF False",
            "Get Variant from VarType 3: Long 1 err 0 LOF 6 Loc 6 Seek 7 EOF False",
            "Get Variant from VarType 0: Empty err 0 LOF 2 Loc 2 Seek 3 EOF False",
            "Get Variant from VarType 1: Null err 0 LOF 2 Loc 2 Seek 3 EOF False",
            "Get String from 61 E9 62: 233 err 0 LOF 3 Loc 3 Seek 4 EOF False",
            "Date, Currency, Single, Double: err 0 [A5 5A BB C6 F0 5E E2 40 68 C5 FF FF FF FF FF FF CA F2 49 71 00 00 00 00 00 00 E0 BF]",
            "back: 2003-01-02 12:34:56 -1.5 1E+30 -0.5 err 0 LOF 28 Loc 28 Seek 29 EOF False",
            "Binary Put 5: err 0 [05 00]",
            "Binary Put 5&: err 0 [05 00 00 00]",
            "Binary Put 40000: err 0 [40 9C 00 00]",
            "Binary Put 1.5: err 0 [00 00 00 00 00 00 F8 3F]",
            "Binary Put 1.5!: err 0 [00 00 C0 3F]",
            "Binary Put \"ab\": err 0 [61 62]",
            "Binary Put True: err 0 [FF FF]",
            "Binary Put ia + 1: err 0 [03 00]",
            "Binary Put Len(\"abc\"): err 0 [03 00 00 00]",
            "Binary Put #1/2/2003#: err 0 [00 00 00 00 E0 5E E2 40]",
            "Binary Put CByte(7): err 0 [07]",
            "Binary Put Null: err 0 [01 00]",
            "Binary Put Empty: err 0 [00 00]",
            "Binary Put 1.5@: err 0 [98 3A 00 00 00 00 00 00]",
            "Binary Put (ia): err 0 [02 00]",
            "Random Put \"ab\": err 0 [02 00 61 62]",
            "Random Put 5: err 0 [05 00]",
            "done");
    }

    // record numbers, elements and fields, Get and Put on other modes, Write # and Input #.
    [Fact]
    public async Task Positions_targets_and_modes_as_vb6_exe_did()
    {
        using var locale = VbLocale.Use(EnGb);
        await Run(Probe2 + Environment.NewLine + """
            Sub Main()
                Dim t As String
                On Error Resume Next
                Dim ia As Integer, bf(2) As Byte, r As TRec, s As String, t2 As String
                Fresh "p.bin"
                Open P("p.bin") For Binary As #1
                ia = 1
                Put #1, 2.5, ia
                L "Put #1, 2.5:" & E() & Where(1)
                Put #1, 3.5, ia
                L "Put #1, 3.5:" & E() & Where(1)
                Put #1, "6", ia
                L "Put #1, ""6"":" & E() & Where(1)
                Put #1, -1, ia
                L "Put #1, -1:" & E() & Where(1)
                bf(1) = 77
                Put #1, 1, bf(1)
                L "Put #1, 1, bf(1):" & E() & Where(1) & " " & Hx("p.bin")
                Get #1, 1, bf(2)
                L "Get #1, 1, bf(2): " & bf(2) & E() & Where(1)
                r.s = "hi"
                Put #1, 1, r.s
                L "Put #1, 1, r.s:" & E() & Where(1) & " " & Hx("p.bin")
                r.i = 0
                Get #1, 1, r.i
                L "Get #1, 1, r.i: " & r.i & E() & Where(1)
                s = "abcdef"
                Put #1, 1, s
                t2 = "xy"
                Get #1, 1, t2
                L "Get 2-character String: " & t2 & E() & Where(1)
                Close #1
                Get #9, 1, ia
                L "Get on a number not open:" & E()
                Put #9, 1, ia
                L "Put on a number not open:" & E()
                Fresh "o.txt"
                Open P("o.txt") For Output As #1
                Put #1, 1, ia
                L "Put on Output:" & E()
                Get #1, 1, ia
                L "Get on Output:" & E()
                Close #1
                Open P("o.txt") For Append As #1
                Put #1, 1, ia
                L "Put on Append:" & E()
                Close #1
                Open P("o.txt") For Input As #1
                Get #1, 1, ia
                L "Get on Input:" & E()
                Put #1, 1, ia
                L "Put on Input:" & E()
                Close #1
                Fresh "wr.bin"
                Open P("wr.bin") For Binary As #1
                Write #1, "a", 1
                L "Write # on Binary:" & E() & Where(1) & " " & Hx("wr.bin")
                Seek #1, 1
                s = "": ia = 0
                Input #1, s, ia
                L "Input # on Binary: [" & s & "] " & ia & E() & Where(1)
                Close #1
                Fresh "wr.dat"
                Open P("wr.dat") For Random As #1
                Write #1, "a"
                L "Write # on Random:" & E()
                Input #1, s
                L "Input # on Random:" & E()
                Close #1
                Fresh "sk.dat"
                Open P("sk.dat") For Random As #1 Len = 2
                Seek #1, 3
                ia = 9
                Put #1, , ia
                L "Random Seek #1, 3, then Put #1, , Integer:" & E() & Where(1) & " " & Hx("sk.dat")
                Seek #1, 0
                L "Random Seek #1, 0:" & E() & Where(1)
                Close #1
                Fresh "ac.bin"
                Open P("ac.bin") For Binary Access Write As #1
                L "Binary Access Write, a missing file:" & E() & Where(1)
                Close #1
                L "  it exists: " & (Dir(P("ac.bin")) <> "")
                Fresh "ac.dat"
                Open P("ac.dat") For Random Access Read As #1
                L "Random Access Read, a missing file:" & E()
                Close #1
                Fresh "ac.dat"
                Open P("ac.dat") For Random Access Read Write As #1 Len = 2
                ia = 3
                Put #1, 1, ia
                Get #1, 1, ia
                L "Random Access Read Write: " & ia & E()
                Close #1
                MakeHex "rs.dat", "0A 00 61 62 63 64 65 66"
                Open P("rs.dat") For Random As #1 Len = 8
                s = "kept"
                Get #1, 1, s
                L "Random Get String whose prefix says 10 in a record of 8: [" & s & "]" & E() & Where(1)
                Close #1
                MakeHex "rs.dat", "03 00 61 62 63"
                Open P("rs.dat") For Random As #1 Len = 8
                s = "kept"
                Get #1, 1, s
                L "Random Get String from a short last record: [" & s & "]" & E() & Where(1)
                Close #1
                L "done"
            End Sub

            Main
            """);

        Printed().Should().Equal(
            "Put #1, 2.5: err 0 LOF 3 Loc 3 Seek 4 EOF False",
            "Put #1, 3.5: err 0 LOF 5 Loc 5 Seek 6 EOF False",
            "Put #1, \"6\": err 0 LOF 7 Loc 7 Seek 8 EOF False",
            "Put #1, -1: err 63 LOF 7 Loc 7 Seek 8 EOF False",
            "Put #1, 1, bf(1): err 0 LOF 7 Loc 1 Seek 2 EOF False [4D 01 00 01 00 01 00]",
            "Get #1, 1, bf(2): 77 err 0 LOF 7 Loc 1 Seek 2 EOF False",
            "Put #1, 1, r.s: err 0 LOF 7 Loc 2 Seek 3 EOF False [68 69 00 01 00 01 00]",
            "Get #1, 1, r.i: 26984 err 0 LOF 7 Loc 2 Seek 3 EOF False",
            "Get 2-character String: ab err 0 LOF 7 Loc 2 Seek 3 EOF False",
            "Get on a number not open: err 52",
            "Put on a number not open: err 52",
            "Put on Output: err 54",
            "Get on Output: err 54",
            "Put on Append: err 54",
            "Get on Input: err 54",
            "Put on Input: err 54",
            "Write # on Binary: err 54 LOF 0 Loc 0 Seek 1 EOF False []",
            "Input # on Binary: [] 0 err 62 LOF 0 Loc 0 Seek 1 EOF True",
            "Write # on Random: err 54",
            "Input # on Random: err 54",
            "Random Seek #1, 3, then Put #1, , Integer: err 0 LOF 6 Loc 3 Seek 4 EOF False [00 00 00 00 09 00]",
            "Random Seek #1, 0: err 63 LOF 6 Loc 3 Seek 4 EOF False",
            "Binary Access Write, a missing file: err 0 LOF 0 Loc 0 Seek 1 EOF False",
            "  it exists: True",
            "Random Access Read, a missing file: err 0",
            "Random Access Read Write: 3 err 0",
            "Random Get String whose prefix says 10 in a record of 8: [] err 59 LOF 8 Loc 0 Seek 1 EOF False",
            "Random Get String from a short last record: [abc] err 0 LOF 5 Loc 1 Seek 2 EOF True",
            "done");
    }

    // every pair of the five modes, and the Lock and Access clauses.
    [Fact]
    public async Task A_second_handle_on_one_file_as_vb6_exe_did()
    {
        using var locale = VbLocale.Use(EnGb);
        await Run(Probe2 + Environment.NewLine + """
            Sub Main()
                Dim t As String
                On Error Resume Next
                MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Input As #1
                Open P("sh.bin") For Input As #2
                L "Input then Input:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Input As #1
                Open P("sh.bin") For Output As #2
                L "Input then Output:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Input As #1
                Open P("sh.bin") For Append As #2
                L "Input then Append:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Input As #1
                Open P("sh.bin") For Binary As #2
                L "Input then Binary:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Input As #1
                Open P("sh.bin") For Random As #2
                L "Input then Random:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Output As #1
                Open P("sh.bin") For Input As #2
                L "Output then Input:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Output As #1
                Open P("sh.bin") For Output As #2
                L "Output then Output:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Output As #1
                Open P("sh.bin") For Append As #2
                L "Output then Append:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Output As #1
                Open P("sh.bin") For Binary As #2
                L "Output then Binary:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Output As #1
                Open P("sh.bin") For Random As #2
                L "Output then Random:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Append As #1
                Open P("sh.bin") For Input As #2
                L "Append then Input:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Append As #1
                Open P("sh.bin") For Output As #2
                L "Append then Output:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Append As #1
                Open P("sh.bin") For Append As #2
                L "Append then Append:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Append As #1
                Open P("sh.bin") For Binary As #2
                L "Append then Binary:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Append As #1
                Open P("sh.bin") For Random As #2
                L "Append then Random:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Binary As #1
                Open P("sh.bin") For Input As #2
                L "Binary then Input:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Binary As #1
                Open P("sh.bin") For Output As #2
                L "Binary then Output:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Binary As #1
                Open P("sh.bin") For Append As #2
                L "Binary then Append:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Binary As #1
                Open P("sh.bin") For Binary As #2
                L "Binary then Binary:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Binary As #1
                Open P("sh.bin") For Random As #2
                L "Binary then Random:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Random As #1
                Open P("sh.bin") For Input As #2
                L "Random then Input:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Random As #1
                Open P("sh.bin") For Output As #2
                L "Random then Output:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Random As #1
                Open P("sh.bin") For Append As #2
                L "Random then Append:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Random As #1
                Open P("sh.bin") For Binary As #2
                L "Random then Binary:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Random As #1
                Open P("sh.bin") For Random As #2
                L "Random then Random:" & E()
                Close
                If FileLen(P("sh.bin")) = 0 Then MakeHex "sh.bin", "41 42"
                Open P("sh.bin") For Binary Shared As #1
                Open P("sh.bin") For Binary As #2
                L "Binary Shared then Binary:" & E()
                Close
                Open P("sh.bin") For Binary Shared As #1
                Open P("sh.bin") For Binary Access Read As #2
                L "Binary Shared then Binary Access Read:" & E()
                Close
                Open P("sh.bin") For Binary Shared As #1
                Open P("sh.bin") For Binary Access Write As #2
                L "Binary Shared then Binary Access Write:" & E()
                Close
                Open P("sh.bin") For Binary Shared As #1
                Open P("sh.bin") For Binary Lock Read As #2
                L "Binary Shared then Binary Lock Read:" & E()
                Close
                Open P("sh.bin") For Binary Lock Read As #1
                Open P("sh.bin") For Binary As #2
                L "Binary Lock Read then Binary:" & E()
                Close
                Open P("sh.bin") For Binary Lock Read As #1
                Open P("sh.bin") For Binary Access Read As #2
                L "Binary Lock Read then Binary Access Read:" & E()
                Close
                Open P("sh.bin") For Binary Lock Read As #1
                Open P("sh.bin") For Binary Access Write As #2
                L "Binary Lock Read then Binary Access Write:" & E()
                Close
                Open P("sh.bin") For Binary Lock Read As #1
                Open P("sh.bin") For Binary Lock Read As #2
                L "Binary Lock Read then Binary Lock Read:" & E()
                Close
                Open P("sh.bin") For Binary Lock Write As #1
                Open P("sh.bin") For Binary As #2
                L "Binary Lock Write then Binary:" & E()
                Close
                Open P("sh.bin") For Binary Lock Write As #1
                Open P("sh.bin") For Binary Access Read As #2
                L "Binary Lock Write then Binary Access Read:" & E()
                Close
                Open P("sh.bin") For Binary Lock Write As #1
                Open P("sh.bin") For Binary Access Write As #2
                L "Binary Lock Write then Binary Access Write:" & E()
                Close
                Open P("sh.bin") For Binary Lock Write As #1
                Open P("sh.bin") For Binary Lock Read As #2
                L "Binary Lock Write then Binary Lock Read:" & E()
                Close
                Open P("sh.bin") For Binary Lock Read Write As #1
                Open P("sh.bin") For Binary As #2
                L "Binary Lock Read Write then Binary:" & E()
                Close
                Open P("sh.bin") For Binary Lock Read Write As #1
                Open P("sh.bin") For Binary Access Read As #2
                L "Binary Lock Read Write then Binary Access Read:" & E()
                Close
                Open P("sh.bin") For Binary Lock Read Write As #1
                Open P("sh.bin") For Binary Access Write As #2
                L "Binary Lock Read Write then Binary Access Write:" & E()
                Close
                Open P("sh.bin") For Binary Lock Read Write As #1
                Open P("sh.bin") For Binary Lock Read As #2
                L "Binary Lock Read Write then Binary Lock Read:" & E()
                Close
                Open P("sh.bin") For Binary Access Read As #1
                Open P("sh.bin") For Binary Lock Write As #2
                L "Binary Access Read then Binary Lock Write:" & E()
                Close
                Open P("sh.bin") For Binary Access Read As #1
                Open P("sh.bin") For Binary Lock Read As #2
                L "Binary Access Read then Binary Lock Read:" & E()
                Close
                Open P("sh.bin") For Binary Access Write As #1
                Open P("sh.bin") For Binary Lock Write As #2
                L "Binary Access Write then Binary Lock Write:" & E()
                Close
                Open P("sh.bin") For Binary Access Write As #1
                Open P("sh.bin") For Binary Lock Read As #2
                L "Binary Access Write then Binary Lock Read:" & E()
                Close
                Open P("sh.bin") For Binary Lock Read Write As #1
                Open P("sh.bin") For Input As #2
                L "Binary Lock Read Write then Input:" & E()
                Close
                Open P("sh.bin") For Input As #1
                Open P("sh.bin") For Input As #2
                L "Input then Input:" & E()
                Close
                L "done"
            End Sub

            Main
            """);

        Printed().Should().Equal(
            "Input then Input: err 0",
            "Input then Output: err 55",
            "Input then Append: err 55",
            "Input then Binary: err 55",
            "Input then Random: err 55",
            "Output then Input: err 55",
            "Output then Output: err 55",
            "Output then Append: err 55",
            "Output then Binary: err 55",
            "Output then Random: err 55",
            "Append then Input: err 55",
            "Append then Output: err 55",
            "Append then Append: err 55",
            "Append then Binary: err 55",
            "Append then Random: err 55",
            "Binary then Input: err 55",
            "Binary then Output: err 55",
            "Binary then Append: err 55",
            "Binary then Binary: err 0",
            "Binary then Random: err 0",
            "Random then Input: err 55",
            "Random then Output: err 55",
            "Random then Append: err 55",
            "Random then Binary: err 0",
            "Random then Random: err 0",
            "Binary Shared then Binary: err 0",
            "Binary Shared then Binary Access Read: err 0",
            "Binary Shared then Binary Access Write: err 0",
            "Binary Shared then Binary Lock Read: err 70",
            "Binary Lock Read then Binary: err 0",
            "Binary Lock Read then Binary Access Read: err 70",
            "Binary Lock Read then Binary Access Write: err 0",
            "Binary Lock Read then Binary Lock Read: err 70",
            "Binary Lock Write then Binary: err 0",
            "Binary Lock Write then Binary Access Read: err 0",
            "Binary Lock Write then Binary Access Write: err 70",
            "Binary Lock Write then Binary Lock Read: err 70",
            "Binary Lock Read Write then Binary: err 70",
            "Binary Lock Read Write then Binary Access Read: err 70",
            "Binary Lock Read Write then Binary Access Write: err 70",
            "Binary Lock Read Write then Binary Lock Read: err 70",
            "Binary Access Read then Binary Lock Write: err 0",
            "Binary Access Read then Binary Lock Read: err 70",
            "Binary Access Write then Binary Lock Write: err 70",
            "Binary Access Write then Binary Lock Read: err 0",
            "Binary Lock Read Write then Input: err 55",
            "Input then Input: err 0",
            "done");
    }

    // packed Types, nested Types, Variant fields, and Random lengths.
    [Fact]
    public async Task Types_packed_and_nested_as_vb6_exe_did()
    {
        using var locale = VbLocale.Use(EnGb);
        await Run(Probe2 + Environment.NewLine + """
            Sub Main()
                Dim t As String
                On Error Resume Next
                Dim m As TMix, o As TOuter, vs As TVarS, al As TAll, o2 As TOuter, vs2 As TVarS, al2 As TAll
                m.b = 1: m.l = 2
                Fresh "u.bin"
                Open P("u.bin") For Binary As #1
                Put #1, , m
                L "Binary Put TMix(1, 2):" & E() & Where(1) & " " & Hx("u.bin")
                Close #1
                o.a = 1: o.inner.i = 2: o.inner.s = "q": o.inner.l = 3: o.z = 4
                Fresh "u.bin"
                Open P("u.bin") For Binary As #1
                Put #1, , o
                L "Binary Put TOuter(1, (2, q, 3), 4):" & E() & Where(1) & " " & Hx("u.bin")
                Seek #1, 1
                Get #1, , o2
                L "back: " & o2.a & " " & o2.inner.i & " " & o2.inner.s & " " & o2.inner.l & " " & o2.z & E() & Where(1)
                Close #1
                vs.v = "ab"
                Fresh "u.bin"
                Open P("u.bin") For Binary As #1
                Put #1, , vs
                L "Binary Put TVarS(""ab""):" & E() & Where(1) & " " & Hx("u.bin")
                Seek #1, 1
                Get #1, , vs2
                L "back: " & TypeName(vs2.v) & " " & vs2.v & E()
                Close #1
                al.bo = True: al.cu = 1.5: al.dt = #1/2/2003#: al.si = 1.5: al.db = 1.5
                Fresh "u.bin"
                Open P("u.bin") For Binary As #1
                Put #1, , al
                L "Binary Put TAll:" & E() & Where(1) & " " & Hx("u.bin")
                Seek #1, 1
                Get #1, , al2
                L "back: " & al2.bo & " " & al2.cu & " " & al2.dt & " " & al2.si & " " & al2.db & E()
                Close #1
                Fresh "u.dat"
                Open P("u.dat") For Random As #1 Len = 6
                Put #1, 1, m
                L "Random Len 6 Put TMix:" & E() & Where(1) & " " & Hx("u.dat")
                Close #1
                Fresh "u.dat"
                Open P("u.dat") For Random As #1 Len = 4
                Put #1, 1, m
                L "Random Len 4 Put TMix (5 bytes):" & E() & Where(1)
                Close #1
                Fresh "u.dat"
                Open P("u.dat") For Random As #1 Len = 5
                Put #1, 1, m
                L "Random Len 5 Put TMix:" & E() & Where(1)
                Close #1
                L "done"
            End Sub

            Main
            """);

        Printed().Should().Equal(
            "Binary Put TMix(1, 2): err 0 LOF 5 Loc 5 Seek 6 EOF False [01 02 00 00 00]",
            "Binary Put TOuter(1, (2, q, 3), 4): err 0 LOF 12 Loc 12 Seek 13 EOF False [01 00 02 00 01 00 71 03 00 00 00 04]",
            "back: 1 2 q 3 4 err 0 LOF 12 Loc 12 Seek 13 EOF False",
            "Binary Put TVarS(\"ab\"): err 0 LOF 6 Loc 6 Seek 7 EOF False [08 00 02 00 61 62]",
            "back: String ab err 0",
            "Binary Put TAll: err 0 LOF 30 Loc 30 Seek 31 EOF False [FF FF 98 3A 00 00 00 00 00 00 00 00 00 00 E0 5E E2 40 00 00 C0 3F 00 00 00 00 00 00 F8 3F]",
            "back: True 1.5 02/01/2003 1.5 1.5 err 0",
            "Random Len 6 Put TMix: err 0 LOF 5 Loc 1 Seek 2 EOF False [01 02 00 00 00]",
            "Random Len 4 Put TMix (5 bytes): err 59 LOF 0 Loc 0 Seek 1 EOF False",
            "Random Len 5 Put TMix: err 0 LOF 5 Loc 1 Seek 2 EOF False",
            "done");
    }

    // Input #, Line Input and Input on Binary, Ctrl+Z, sharing fallbacks and more Len values.
    [Fact]
    public async Task Text_reads_on_a_Binary_file_as_vb6_exe_did()
    {
        using var locale = VbLocale.Use(EnGb);
        await Run(Probe3 + Environment.NewLine + """
            Sub Main()
                Dim t As String
                On Error Resume Next
                Dim s As String, n As Integer, t2 As String, b() As Byte
                MakeHex "t.bin", "31 2C 61 62 0D 0A 78 79 0D 0A"
                Open P("t.bin") For Binary As #1
                Input #1, n, s
                L "Input # on Binary: " & n & " [" & s & "]" & E() & Where(1)
                Line Input #1, s
                L "then Line Input: [" & s & "]" & E() & Where(1)
                Seek #1, 1
                Line Input #1, s
                L "Line Input from 1: [" & s & "]" & E() & Where(1)
                Line Input #1, s
                L "Line Input, the last line with its CR LF: [" & s & "]" & E() & Where(1)
                s = "kept"
                Line Input #1, s
                L "Line Input at the end: [" & s & "]" & E() & Where(1)
                s = "kept"
                s = Input(2, #1)
                L "Input(2) at the end: [" & s & "]" & E() & Where(1)
                n = 5
                Input #1, n
                L "Input # at the end: " & n & E() & Where(1)
                Close #1
                MakeHex "z.bin", "61 62 1A 63 64"
                Open P("z.bin") For Binary As #1
                Line Input #1, s
                L "Binary Line Input over a Ctrl+Z: [" & Show(s) & "]" & E() & Where(1)
                Seek #1, 1
                s = Input(5, #1)
                L "Binary Input(5) over a Ctrl+Z: " & Show(s) & E() & Where(1)
                Close #1
                MakeHex "s.bin", "61 62 63"
                Open P("s.bin") For Binary As #1
                s = Space$(5)
                Get #1, 1, s
                L "Binary Get 5-character String, 3 bytes there: " & Show(s) & E() & Where(1)
                Close #1
                Fresh "ar.bin"
                Open P("ar.bin") For Binary Access Read As #1
                Close #1
                L "Binary Access Read made the missing file: " & (Dir(P("ar.bin")) <> "")
                Fresh "ar.dat"
                Open P("ar.dat") For Random Access Read As #1
                Close #1
                L "Random Access Read made the missing file: " & (Dir(P("ar.dat")) <> "")
                MakeHex "sh.bin", "41 42"
                Dim ia As Integer
                Open P("sh.bin") For Binary Lock Read As #1
                Open P("sh.bin") For Binary As #2
                Get #2, 1, ia
                L "plain beside Lock Read, Get:" & E()
                ia = 7
                Put #2, 1, ia
                L "plain beside Lock Read, Put:" & E()
                Close
                Open P("sh.bin") For Binary Lock Write As #1
                Open P("sh.bin") For Binary As #2
                Put #2, 1, ia
                L "plain beside Lock Write, Put:" & E()
                Get #2, 1, ia
                L "plain beside Lock Write, Get: " & ia & E()
                Close
                Fresh "len.dat"
                Open P("len.dat") For Random As #1 Len = 0
                L "Random Len = 0:" & E() & Where(1)
                ia = 5
                Put #1, 2, ia
                L "Len = 0, Put #1, 2, Integer:" & E() & Where(1)
                Close #1
                Fresh "len.dat"
                Open P("len.dat") For Random As #1 Len = -2
                L "Random Len = -2:" & E() & Where(1)
                ia = 5
                Put #1, 2, ia
                L "Len = -2, Put #1, 2, Integer:" & E() & Where(1)
                Close #1
                Fresh "len.dat"
                Open P("len.dat") For Random As #1 Len = 1.5
                L "Random Len = 1.5:" & E() & Where(1)
                ia = 5
                Put #1, 2, ia
                L "Len = 1.5, Put #1, 2, Integer:" & E() & Where(1)
                Close #1
                Fresh "len.dat"
                Open P("len.dat") For Random As #1 Len = 2.5
                L "Random Len = 2.5:" & E() & Where(1)
                ia = 5
                Put #1, 2, ia
                L "Len = 2.5, Put #1, 2, Integer:" & E() & Where(1)
                Close #1
                Fresh "len.dat"
                Open P("len.dat") For Random As #1 Len = "8"
                L "Random Len = ""8"":" & E() & Where(1)
                ia = 5
                Put #1, 2, ia
                L "Len = ""8"", Put #1, 2, Integer:" & E() & Where(1)
                Close #1
                L "done"
            End Sub

            Main
            """);

        Printed().Should().Equal(
            "Input # on Binary: 1 [ab] err 0 LOF 10 Loc 6 Seek 7 EOF False",
            "then Line Input: [xy] err 0 LOF 10 Loc 10 Seek 11 EOF False",
            "Line Input from 1: [1,ab] err 0 LOF 10 Loc 6 Seek 7 EOF False",
            "Line Input, the last line with its CR LF: [xy] err 0 LOF 10 Loc 10 Seek 11 EOF False",
            "Line Input at the end: [kept] err 62 LOF 10 Loc 10 Seek 11 EOF True",
            "Input(2) at the end: [] err 0 LOF 10 Loc 10 Seek 11 EOF True",
            "Input # at the end: 5 err 62 LOF 10 Loc 10 Seek 11 EOF True",
            "Binary Line Input over a Ctrl+Z: [[ab<26>cd]] err 0 LOF 5 Loc 5 Seek 6 EOF True",
            "Binary Input(5) over a Ctrl+Z: [ab<26>cd] err 0 LOF 5 Loc 5 Seek 6 EOF False",
            "Binary Get 5-character String, 3 bytes there: [abc<0><0>] err 0 LOF 3 Loc 5 Seek 6 EOF True",
            "Binary Access Read made the missing file: True",
            "Random Access Read made the missing file: True",
            "plain beside Lock Read, Get: err 75",
            "plain beside Lock Read, Put: err 0",
            "plain beside Lock Write, Put: err 75",
            "plain beside Lock Write, Get: 7 err 0",
            "Random Len = 0: err 5 LOF err52 Loc err52 Seek err52 EOF err52",
            "Len = 0, Put #1, 2, Integer: err 52 LOF err52 Loc err52 Seek err52 EOF err52",
            "Random Len = -2: err 5 LOF err52 Loc err52 Seek err52 EOF err52",
            "Len = -2, Put #1, 2, Integer: err 52 LOF err52 Loc err52 Seek err52 EOF err52",
            "Random Len = 1.5: err 0 LOF 0 Loc 0 Seek 1 EOF False",
            "Len = 1.5, Put #1, 2, Integer: err 0 LOF 4 Loc 2 Seek 3 EOF False",
            "Random Len = 2.5: err 0 LOF 0 Loc 0 Seek 1 EOF False",
            "Len = 2.5, Put #1, 2, Integer: err 0 LOF 4 Loc 2 Seek 3 EOF False",
            "Random Len = \"8\": err 0 LOF 0 Loc 0 Seek 1 EOF False",
            "Len = \"8\", Put #1, 2, Integer: err 0 LOF 10 Loc 2 Seek 3 EOF False",
            "done");
    }

    // intrinsics and expressions, typed or a Variant, descriptor order and long Strings.
    [Fact]
    public async Task What_Put_writes_for_an_expression_as_vb6_exe_did()
    {
        using var locale = VbLocale.Use(EnGb);
        await Run(Probe3 + Environment.NewLine + """""
            Sub Main()
                Dim t As String
                On Error Resume Next
                Dim v As Variant, va(1) As Variant, sa() As String, d2() As Integer, s As String, i As Long
                v = CInt(3)
                va(0) = CInt(6)
                sa = Split("ab,c", ",")
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Mid("abc", 1, 1)
                L "Binary Put Mid(""abc"", 1, 1):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Mid$("abc", 1, 1)
                L "Binary Put Mid$(""abc"", 1, 1):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Left("ab", 1)
                L "Binary Put Left(""ab"", 1):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Trim(" a")
                L "Binary Put Trim("" a""):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Chr(65)
                L "Binary Put Chr(65):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Chr$(65)
                L "Binary Put Chr$(65):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Str(5)
                L "Binary Put Str(5):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Format(5)
                L "Binary Put Format(5):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , CVar(5)
                L "Binary Put CVar(5):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , IIf(True, 5, 6)
                L "Binary Put IIf(True, 5, 6):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Choose(1, 5)
                L "Binary Put Choose(1, 5):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Space(1)
                L "Binary Put Space(1):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , String(1, "a")
                L "Binary Put String(1, ""a""):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , UCase("a")
                L "Binary Put UCase(""a""):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Hex(255)
                L "Binary Put Hex(255):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , v + 1
                L "Binary Put v + 1:" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , VV()
                L "Binary Put VV():" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , VI()
                L "Binary Put VI():" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , sa(0)
                L "Binary Put sa(0):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Array(1)(0)
                L "Binary Put Array(1)(0):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , va(0)
                L "Binary Put va(0):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , CStr(5)
                L "Binary Put CStr(5):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Abs(-2)
                L "Binary Put Abs(-2):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Int(2.5)
                L "Binary Put Int(2.5):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Val("2")
                L "Binary Put Val(""2""):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Len("ab")
                L "Binary Put Len(""ab""):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Asc("a")
                L "Binary Put Asc(""a""):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Weekday(#1/2/2003#)
                L "Binary Put Weekday(#1/2/2003#):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , DateSerial(2003, 1, 2)
                L "Binary Put DateSerial(2003, 1, 2):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , CDate("1/2/2003")
                L "Binary Put CDate(""1/2/2003""):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , LCase$("A")
                L "Binary Put LCase$(""A""):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Sgn(-3)
                L "Binary Put Sgn(-3):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Fix(2.5)
                L "Binary Put Fix(2.5):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Year(#1/2/2003#)
                L "Binary Put Year(#1/2/2003#):" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , -v
                L "Binary Put -v:" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , v & ""
                L "Binary Put v & """":" & E() & " " & Hx("w.bin")
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Now
                L "Binary Put Now, first 2:" & E() & " " & First("w.bin", 2) & " LOF " & LOF(1)
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Date
                L "Binary Put Date, first 2:" & E() & " " & First("w.bin", 2) & " LOF " & LOF(1)
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Time
                L "Binary Put Time, first 2:" & E() & " " & First("w.bin", 2) & " LOF " & LOF(1)
                Close #1
                Fresh "w.bin"
                Open P("w.bin") For Binary As #1
                Put #1, , Timer
                L "Binary Put Timer: LOF " & LOF(1) & E()
                Close #1
                ReDim d2(1 To 2, 3 To 5)
                d2(1, 3) = 1: d2(2, 3) = 2: d2(1, 4) = 3: d2(2, 5) = 9
                Fresh "d.dat"
                Open P("d.dat") For Random As #1 Len = 64
                Put #1, 1, d2
                L "Random Put Integer(1 To 2, 3 To 5):" & E() & " " & Hx("d.dat")
                Close #1
                Fresh "d.bin"
                Open P("d.bin") For Binary As #1
                v = d2
                Put #1, , v
                L "Binary Put Variant holding Integer(1 To 2, 3 To 5):" & E() & " " & Hx("d.bin")
                Close #1
                Open P("d.dat") For Random As #1 Len = 64
                Erase d2
                Get #1, 1, d2
                L "Random Get back: " & LBound(d2, 1) & " To " & UBound(d2, 1) & ", " & LBound(d2, 2) & " To " & UBound(d2, 2) & " d2(2, 5) " & d2(2, 5) & E()
                Close #1
                s = String$(70000, "x")
                Fresh "l.bin"
                Open P("l.bin") For Binary As #1
                v = s
                Put #1, , v
                L "Binary Put Variant String of 70000: LOF " & LOF(1) & E() & " " & First("l.bin", 8)
                Seek #1, 1
                v = Empty
                Get #1, , v
                L "back: Len " & Len(v) & E() & Where(1)
                Close #1
                Dim r As TRec, r2 As TRec
                r.i = 1: r.s = s: r.l = 2
                Fresh "l.bin"
                Open P("l.bin") For Binary As #1
                Put #1, , r
                L "Binary Put TRec with a String of 70000: LOF " & LOF(1) & E() & " " & First("l.bin", 10)
                Seek #1, 1
                Get #1, , r2
                L "back: Len " & Len(r2.s) & " l " & r2.l & E() & Where(1)
                Close #1
                s = String$(40000, "y")
                v = s
                Fresh "l.bin"
                Open P("l.bin") For Binary As #1
                Put #1, , v
                L "Binary Put Variant String of 40000: LOF " & LOF(1) & E() & " " & First("l.bin", 6)
                Close #1
                s = String$(65535, "y")
                v = s
                Fresh "l.bin"
                Open P("l.bin") For Binary As #1
                Put #1, , v
                L "Binary Put Variant String of 65535: LOF " & LOF(1) & E() & " " & First("l.bin", 6)
                Close #1
                MakeHex "rv.dat", "08 00 0A 00 61 62 63 64"
                Open P("rv.dat") For Random As #1 Len = 8
                v = "kept"
                Get #1, 1, v
                L "Random Get Variant whose String says 10 in a record of 8: " & TypeName(v) & " [" & v & "]" & E() & Where(1)
                Close #1
                Dim fs(1) As String, fs2(1) As String
                fs(0) = "ab": fs(1) = "c"
                Fresh "fs.bin"
                Open P("fs.bin") For Binary As #1
                Put #1, , fs
                Seek #1, 1
                Get #1, , fs2
                L "Binary fixed String array back: [" & fs2(0) & "] [" & fs2(1) & "]" & E() & Where(1)
                Close #1
                MakeHex "ve.bin", "02 00 2A 00"
                Open P("ve.bin") For Binary As #1
                Get #1, , va(1)
                L "Get into a Variant array element: " & TypeName(va(1)) & " " & va(1) & E() & Where(1)
                Close #1
                L "done"
            End Sub

            Main
            """"");

        Printed().Should().Equal(
            "Binary Put Mid(\"abc\", 1, 1): err 0 [08 00 01 00 61]",
            "Binary Put Mid$(\"abc\", 1, 1): err 0 [61]",
            "Binary Put Left(\"ab\", 1): err 0 [08 00 01 00 61]",
            "Binary Put Trim(\" a\"): err 0 [08 00 01 00 61]",
            "Binary Put Chr(65): err 0 [08 00 01 00 41]",
            "Binary Put Chr$(65): err 0 [41]",
            "Binary Put Str(5): err 0 [08 00 02 00 20 35]",
            "Binary Put Format(5): err 0 [08 00 01 00 35]",
            "Binary Put CVar(5): err 0 [02 00 05 00]",
            "Binary Put IIf(True, 5, 6): err 0 [02 00 05 00]",
            "Binary Put Choose(1, 5): err 0 [02 00 05 00]",
            "Binary Put Space(1): err 0 [08 00 01 00 20]",
            "Binary Put String(1, \"a\"): err 0 [08 00 01 00 61]",
            "Binary Put UCase(\"a\"): err 0 [08 00 01 00 41]",
            "Binary Put Hex(255): err 0 [08 00 02 00 46 46]",
            "Binary Put v + 1: err 0 [02 00 04 00]",
            "Binary Put VV(): err 0 [02 00 04 00]",
            "Binary Put VI(): err 0 [04 00]",
            "Binary Put sa(0): err 0 [61 62]",
            "Binary Put Array(1)(0): err 0 [02 00 01 00]",
            "Binary Put va(0): err 0 [02 00 06 00]",
            "Binary Put CStr(5): err 0 [35]",
            "Binary Put Abs(-2): err 0 [02 00]",
            "Binary Put Int(2.5): err 0 [00 00 00 00 00 00 00 40]",
            "Binary Put Val(\"2\"): err 0 [00 00 00 00 00 00 00 40]",
            "Binary Put Len(\"ab\"): err 0 [02 00 00 00]",
            "Binary Put Asc(\"a\"): err 0 [61 00]",
            "Binary Put Weekday(#1/2/2003#): err 0 [02 00 05 00]",
            "Binary Put DateSerial(2003, 1, 2): err 0 [07 00 00 00 00 00 E0 5E E2 40]",
            "Binary Put CDate(\"1/2/2003\"): err 0 [00 00 00 00 A0 62 E2 40]",
            "Binary Put LCase$(\"A\"): err 0 [61]",
            "Binary Put Sgn(-3): err 0 [FF FF]",
            "Binary Put Fix(2.5): err 0 [00 00 00 00 00 00 00 40]",
            "Binary Put Year(#1/2/2003#): err 0 [02 00 D3 07]",
            "Binary Put -v: err 0 [02 00 FD FF]",
            "Binary Put v & \"\": err 0 [08 00 01 00 33]",
            "Binary Put Now, first 2: err 0 [07 00] LOF 10",
            "Binary Put Date, first 2: err 0 [07 00] LOF 10",
            "Binary Put Time, first 2: err 0 [07 00] LOF 10",
            "Binary Put Timer: LOF 4 err 0",
            "Random Put Integer(1 To 2, 3 To 5): err 0 [02 00 03 00 00 00 03 00 00 00 02 00 00 00 01 00 00 00 01 00 02 00 03 00 00 00 00 00 09 00]",
            "Binary Put Variant holding Integer(1 To 2, 3 To 5): err 0 [02 20 02 00 03 00 00 00 03 00 00 00 02 00 00 00 01 00 00 00 01 00 02 00 03 00 00 00 00 00 09 00]",
            "Random Get back: 1 To 2, 3 To 5 d2(2, 5) 9 err 0",
            "Binary Put Variant String of 70000: LOF 70008 err 0 [08 00 FF FF 70 11 01 00]",
            "back: Len 70000 err 0 LOF 70008 Loc 70008 Seek 70009 EOF False",
            "Binary Put TRec with a String of 70000: LOF 70012 err 0 [01 00 FF FF 70 11 01 00 78 78]",
            "back: Len 70000 l 2 err 0 LOF 70012 Loc 70012 Seek 70013 EOF False",
            "Binary Put Variant String of 40000: LOF 40004 err 0 [08 00 40 9C 79 79]",
            "Binary Put Variant String of 65535: LOF 65543 err 0 [08 00 FF FF FF FF]",
            "Random Get Variant whose String says 10 in a record of 8: Empty [] err 59 LOF 8 Loc 0 Seek 1 EOF False",
            "Binary fixed String array back: [ab] [c] err 0 LOF 7 Loc 7 Seek 8 EOF False",
            "Get into a Variant array element: Integer 42 err 0 LOF 4 Loc 4 Seek 5 EOF False",
            "done");
    }

    // fixed arrays round trip, arrays with no bounds, Len on Binary, lone CR and record overflows.
    [Fact]
    public async Task Round_trips_and_record_overflows_as_vb6_exe_did()
    {
        using var locale = VbLocale.Use(EnGb);
        await Run(Probe4 + Environment.NewLine + """
            Sub Main()
                Dim t As String
                On Error Resume Next
                Dim fi(1) As Integer, gf(1) As Integer, du() As Integer, dg() As Integer, v As Variant, s As String, ia As Integer, la As Long
                Dim m As TMix, dt As Date
                fi(0) = 1: fi(1) = 2
                Fresh "f.dat"
                Open P("f.dat") For Random As #1 Len = 32
                Put #1, 1, fi
                t = E()
                gf(0) = -1: gf(1) = -1
                Get #1, 1, gf
                L "Random fixed array round trip: " & gf(0) & " " & gf(1) & t & E() & Where(1)
                Get #1, 1, dg
                L "Random Get, into an unallocated dynamic array, a record holding a fixed array:" & E() & " bounds " & Bounds(dg) & Where(1)
                Close #1
                Fresh "u.dat"
                Open P("u.dat") For Random As #1 Len = 32
                Put #1, 1, du
                L "Random Put unallocated dynamic array:" & E() & Where(1) & " " & Hx("u.dat")
                Close #1
                Fresh "u.bin"
                Open P("u.bin") For Binary As #1
                Put #1, , du
                L "Binary Put unallocated dynamic array:" & E() & Where(1) & " " & Hx("u.bin")
                Close #1
                Fresh "u.bin"
                Open P("u.bin") For Binary As #1
                v = du
                Put #1, , v
                L "Binary Put Variant holding an unallocated Integer():" & E() & Where(1) & " " & Hx("u.bin")
                Close #1
                Fresh "n.bin"
                Open P("n.bin") For Binary As #1
                Set v = Nothing
                Put #1, , v
                L "Binary Put Variant Nothing:" & E() & Where(1) & " " & Hx("n.bin")
                ia = 5
                Put #1, , ia
                L "then Put #1, , Integer:" & E() & Where(1) & " " & Hx("n.bin")
                Close #1
                Fresh "l.bin"
                Open P("l.bin") For Binary As #1 Len = 0
                L "Binary Len = 0:" & E() & Where(1)
                Close #1
                Fresh "l.bin"
                Open P("l.bin") For Binary As #1 Len = -2
                L "Binary Len = -2:" & E() & Where(1)
                Close #1
                Fresh "l.bin"
                Open P("l.bin") For Binary As #1 Len = 40000
                L "Binary Len = 40000:" & E() & Where(1)
                Close #1
                ReDim gi(0 To 5)
                Fresh "k.dat"
                Open P("k.dat") For Random As #1 Len = 32
                ReDim du(1): du(0) = 3: du(1) = 4
                Put #1, 1, du
                LockedGet gi(0)
                Close #1
                ReDim gi(0 To 1)
                Fresh "k.bin"
                Open P("k.bin") For Binary As #1
                Put #1, 1, du
                LockedBinaryGet gi(0)
                Close #1
                MakeHex "cr.bin", "61 62 0D 63 64 0D"
                Open P("cr.bin") For Binary As #1
                Line Input #1, s
                L "Binary Line Input, a lone CR inside: " & Show(s) & E() & Where(1)
                Line Input #1, s
                L "Binary Line Input, a lone CR last: " & Show(s) & E() & Where(1)
                Close #1
                MakeHex "cr.bin", "61 62 0A 63 64"
                Open P("cr.bin") For Binary As #1
                Line Input #1, s
                L "Binary Line Input, a lone LF inside: " & Show(s) & E() & Where(1)
                Close #1
                MakeHex "g.dat", "01 02 03 04 05 06"
                Open P("g.dat") For Random As #1 Len = 2
                la = -1
                Get #1, 1, la
                L "Random Len 2, Get Long: " & la & E() & Where(1)
                m.b = 9: m.l = 9
                Get #1, 2, m
                L "Random Len 2, Get TMix: " & m.b & " " & m.l & E() & Where(1)
                v = "kept"
                Get #1, 1, v
                L "Random Len 2, Get Variant from 01 02: " & TypeName(v) & E() & Where(1)
                Close #1
                Fresh "e.dat"
                Open P("e.dat") For Random As #1 Len = 1
                v = Empty
                Put #1, 1, v
                L "Random Len 1, Put Variant Empty:" & E() & Where(1)
                Close #1
                L "done"
            End Sub

            Main
            """);

        Printed().Should().Equal(
            "Random fixed array round trip: 1 2 err 0 err 0 LOF 4 Loc 1 Seek 2 EOF True",
            "Random Get, into an unallocated dynamic array, a record holding a fixed array: err 0 bounds 0 To 1 LOF 4 Loc 1 Seek 2 EOF True",
            "Random Put unallocated dynamic array: err 0 LOF 2 Loc 1 Seek 2 EOF False [00 00]",
            "Binary Put unallocated dynamic array: err 0 LOF 0 Loc 0 Seek 1 EOF False []",
            "Binary Put Variant holding an unallocated Integer(): err 0 LOF 4 Loc 4 Seek 5 EOF False [02 20 00 00]",
            "Binary Put Variant Nothing: err 13 LOF 2 Loc 0 Seek 1 EOF False [09 00]",
            "then Put #1, , Integer: err 0 LOF 2 Loc 2 Seek 3 EOF False [05 00]",
            "Binary Len = 0: err 5 LOF err52 Loc err52 Seek err52 EOF err52",
            "Binary Len = -2: err 5 LOF err52 Loc err52 Seek err52 EOF err52",
            "Binary Len = 40000: err 6 LOF err52 Loc err52 Seek err52 EOF err52",
            "  Binary Get into the array while its element is passed ByRef: err 0 gi(0) 3",
            "Binary Line Input, a lone CR inside: [ab] err 0 LOF 6 Loc 3 Seek 4 EOF False",
            "Binary Line Input, a lone CR last: [cd] err 0 LOF 6 Loc 6 Seek 7 EOF True",
            "Binary Line Input, a lone LF inside: [ab<10>cd] err 0 LOF 5 Loc 5 Seek 6 EOF True",
            "Random Len 2, Get Long: -1 err 59 LOF 6 Loc 0 Seek 1 EOF False",
            "Random Len 2, Get TMix: 3 9 err 59 LOF 6 Loc 0 Seek 1 EOF False",
            "Random Len 2, Get Variant from 01 02: Empty err 458 LOF 6 Loc 0 Seek 1 EOF False",
            "Random Len 1, Put Variant Empty: err 59 LOF 0 Loc 0 Seek 1 EOF False",
            "done");
    }

    // a Random Get into a dynamic array whose element is passed ByRef.
    [Fact]
    public async Task Get_into_a_locked_array_as_vb6_exe_did()
    {
        using var locale = VbLocale.Use(EnGb);
        await Run(Probe5 + Environment.NewLine + """
            Sub Main()
                Dim t As String
                On Error Resume Next
                Dim du() As Integer
                ReDim gi(0 To 5)
                Fresh "k.dat"
                Open P("k.dat") For Random As #1 Len = 32
                ReDim du(1): du(0) = 3: du(1) = 4
                Put #1, 1, du
                LockedGet gi(0)
                L "after the call:" & E() & " bounds " & Bounds(gi) & Where(1)
                Close #1
                L "done"
            End Sub

            Main
            """);

        Printed().Should().Equal(
            "  Random Get into the array while its element is passed ByRef: err 10 bounds 0 To 5",
            "after the call: err 0 bounds 0 To 5 LOF 14 Loc 1 Seek 2 EOF True",
            "done");
    }

    // EOF through a Put, a Type field and a Variant element past the record, and Input # at the end.
    [Fact]
    public async Task EOF_after_a_Put_and_what_a_failed_field_is_left_as_vb6_exe_did()
    {
        using var locale = VbLocale.Use(EnGb);
        await Run(Probe6 + Environment.NewLine + """
            Sub Main()
                Dim t As String
                On Error Resume Next
                Dim bb As Byte, ia As Integer, u As TIS, va(1) As Variant, n As Integer, s As String
                MakeHex "e.bin", "01 02"
                Open P("e.bin") For Binary As #1
                Get #1, 2, ia
                L "Binary Get Integer, 1 byte there:" & E() & Where(1)
                bb = 9
                Put #1, 5, bb
                L "then Put #1, 5, Byte:" & E() & Where(1)
                Get #1, 6, ia
                L "then Get past the end:" & E() & Where(1)
                Put #1, , bb
                L "then Put #1, , Byte:" & E() & Where(1)
                Close #1
                MakeHex "e.dat", "01 00"
                Open P("e.dat") For Random As #1 Len = 2
                Get #1, 3, ia
                L "Random Get past the end:" & E() & Where(1)
                ia = 4
                Put #1, 1, ia
                L "then Put #1, 1, Integer:" & E() & Where(1)
                Close #1
                MakeHex "u.dat", "07 00 0A 00 61 62"
                Open P("u.dat") For Random As #1 Len = 6
                u.i = 0: u.s = "kept"
                Get #1, 1, u
                L "Random Get Type (Integer, String) whose String says 10 in a record of 6: " & u.i & " [" & u.s & "]" & E() & Where(1)
                Close #1
                MakeHex "va.dat", "02 00 05 00 08 00"
                Open P("va.dat") For Random As #1 Len = 6
                va(0) = "k0": va(1) = "k1"
                Get #1, 1, va
                L "Random Get Variant(1), its second a String past the record: " & TypeName(va(0)) & " " & va(0) & ", " & TypeName(va(1)) & E() & Where(1)
                Close #1
                MakeHex "f.bin", "31 2C 61 62"
                Open P("f.bin") For Binary As #1
                Input #1, n, s
                L "Binary Input #, a last field with no line end: " & n & " [" & s & "]" & E() & Where(1)
                Close #1
                MakeHex "f.bin", "31 2C 61 62 2C"
                Open P("f.bin") For Binary As #1
                Input #1, n, s
                L "Binary Input #, a last field and a comma: " & n & " [" & s & "]" & E() & Where(1)
                Close #1
                MakeHex "f.bin", "31 2C 22 61 62 22"
                Open P("f.bin") For Binary As #1
                Input #1, n, s
                L "Binary Input #, a last quoted field: " & n & " [" & s & "]" & E() & Where(1)
                Close #1
                L "done"
            End Sub

            Main
            """);

        Printed().Should().Equal(
            "Binary Get Integer, 1 byte there: err 0 LOF 2 Loc 3 Seek 4 EOF True",
            "then Put #1, 5, Byte: err 0 LOF 5 Loc 5 Seek 6 EOF True",
            "then Get past the end: err 0 LOF 5 Loc 7 Seek 8 EOF True",
            "then Put #1, , Byte: err 0 LOF 6 Loc 8 Seek 9 EOF True",
            "Random Get past the end: err 0 LOF 2 Loc 3 Seek 4 EOF True",
            "then Put #1, 1, Integer: err 0 LOF 2 Loc 1 Seek 2 EOF True",
            "Random Get Type (Integer, String) whose String says 10 in a record of 6: 7 [] err 59 LOF 6 Loc 0 Seek 1 EOF False",
            "Random Get Variant(1), its second a String past the record: Integer 5, Empty err 59 LOF 6 Loc 0 Seek 1 EOF False",
            "Binary Input #, a last field with no line end: 1 [ab] err 0 LOF 4 Loc 4 Seek 5 EOF True",
            "Binary Input #, a last field and a comma: 1 [ab] err 0 LOF 5 Loc 5 Seek 6 EOF False",
            "Binary Input #, a last quoted field: 1 [ab] err 0 LOF 6 Loc 6 Seek 7 EOF True",
            "done");
    }

    // the file pointer stopping at the end while Seek counts on.
    [Fact]
    public async Task After_a_read_past_the_end_the_next_Get_or_Put_is_at_the_end_as_vb6_exe_did()
    {
        using var locale = VbLocale.Use(EnGb);
        await Run(Probe6 + Environment.NewLine + """
            Sub Main()
                Dim t As String
                On Error Resume Next
                Dim bb As Byte, ia As Integer, la As Long, b4(3) As Byte
                MakeHex "r.dat", "01 00"
                Open P("r.dat") For Random As #1 Len = 2
                Get #1, 3, ia
                L "Random Len 2 on 2 bytes, Get #1, 3:" & E() & Where(1)
                ia = 7
                Put #1, , ia
                L "then Put #1, , Integer:" & E() & Where(1) & " " & Hx("r.dat")
                Close #1
                MakeHex "r.dat", "01 00 02"
                Open P("r.dat") For Random As #1 Len = 2
                Get #1, 2, ia
                L "Random Len 2 on 3 bytes, Get #1, 2: " & ia & E() & Where(1)
                ia = 7
                Put #1, , ia
                L "then Put #1, , Integer:" & E() & Where(1) & " " & Hx("r.dat")
                ia = 8
                Put #1, , ia
                L "then Put #1, , Integer again:" & E() & Where(1) & " " & Hx("r.dat")
                Close #1
                MakeHex "b.bin", "01 02"
                Open P("b.bin") For Binary As #1
                Open P("b.bin") For Binary As #2
                Get #1, 2, la
                L "Binary Get #1, 2, Long, 1 byte there:" & E() & Where(1)
                b4(0) = &HAA: b4(1) = &HBB: b4(2) = &HCC: b4(3) = &HDD
                Put #2, 3, b4
                L "#2 puts 4 bytes at 3:" & E() & " " & Hx("b.bin")
                ia = 0
                Get #1, , ia
                L "then Get #1, , Integer: " & Hex$(ia) & E() & Where(1)
                Close #1
                Close #2
                MakeHex "b.bin", "01 02"
                Open P("b.bin") For Binary As #1
                Get #1, 10, la
                L "Binary Get #1, 10, Long, past the end:" & E() & Where(1)
                bb = 9
                Put #1, , bb
                L "then Put #1, , Byte:" & E() & Where(1) & " " & Hx("b.bin")
                Close #1
                MakeHex "b.bin", "01 02 03"
                Open P("b.bin") For Binary As #1
                Get #1, 2, la
                L "Binary Get #1, 2, Long, 2 bytes there:" & E() & Where(1)
                Seek #1, 3
                L "then Seek #1, 3:" & E() & Where(1)
                bb = 9
                Put #1, , bb
                L "then Put #1, , Byte:" & E() & Where(1) & " " & Hx("b.bin")
                Close #1
                L "done"
            End Sub

            Main
            """);

        Printed().Should().Equal(
            "Random Len 2 on 2 bytes, Get #1, 3: err 0 LOF 2 Loc 3 Seek 4 EOF True",
            "then Put #1, , Integer: err 0 LOF 6 Loc 4 Seek 5 EOF True [01 00 00 00 07 00]",
            "Random Len 2 on 3 bytes, Get #1, 2: 2 err 0 LOF 3 Loc 2 Seek 3 EOF True",
            "then Put #1, , Integer: err 0 LOF 5 Loc 3 Seek 4 EOF True [01 00 02 07 00]",
            "then Put #1, , Integer again: err 0 LOF 7 Loc 4 Seek 5 EOF True [01 00 02 07 00 08 00]",
            "Binary Get #1, 2, Long, 1 byte there: err 0 LOF 2 Loc 5 Seek 6 EOF True",
            "#2 puts 4 bytes at 3: err 0 [01 02 AA BB CC DD]",
            "then Get #1, , Integer: BBAA err 0 LOF 6 Loc 7 Seek 8 EOF False",
            "Binary Get #1, 10, Long, past the end: err 0 LOF 2 Loc 13 Seek 14 EOF True",
            "then Put #1, , Byte: err 0 LOF 10 Loc 14 Seek 15 EOF True [01 02 00 00 00 00 00 00 00 09]",
            "Binary Get #1, 2, Long, 2 bytes there: err 0 LOF 3 Loc 5 Seek 6 EOF True",
            "then Seek #1, 3: err 0 LOF 3 Loc 2 Seek 3 EOF False",
            "then Put #1, , Byte: err 0 LOF 3 Loc 3 Seek 4 EOF False [01 02 09]",
            "done");
    }

    // The bytes on disk, read by C# rather than by the Get under test, so a Put and a Get that agreed with each other but
    // not with vb6.exe would fail here. Each expected byte string is the hex vb6.exe's probe logged.
    [Fact]
    public async Task The_bytes_on_disk_are_vb6_exe_s()
    {
        await Run($$"""
            Private Type TRec
                i As Integer
                s As String
                l As Long
            End Type

            Sub Main()
                Dim r As TRec, v As Variant, d() As Integer, n As Integer
                r.i = 1: r.s = "xy": r.l = 2
                Open "{{PathOf("t.bin")}}" For Binary As #1
                Put #1, , r
                Close #1
                Open "{{PathOf("v.bin")}}" For Binary As #1
                v = Array("a", 2)
                Put #1, , v
                v = "a" & Chr$(233) & ChrW$(&H416)
                Put #1, , v
                Close #1
                ReDim d(1 To 2, 3 To 5)
                d(1, 3) = 1: d(2, 3) = 2: d(1, 4) = 3: d(2, 5) = 9
                Open "{{PathOf("d.dat")}}" For Random As #1 Len = 64
                Put #1, 1, d
                n = 5
                Put #1, 2, n
                Close #1
            End Sub

            Main
            """);

        Hex(File.ReadAllBytes(PathOf("t.bin"))).Should().Be("01 00 02 00 78 79 02 00 00 00");
        Hex(File.ReadAllBytes(PathOf("v.bin"))).Should().Be("0C 20 01 00 02 00 00 00 00 00 00 00 08 00 01 00 61 02 00 02 00 08 00 03 00 61 E9 3F");
        Hex(File.ReadAllBytes(PathOf("d.dat"))).Should().Be(
            "02 00 03 00 00 00 03 00 00 00 02 00 00 00 01 00 00 00 01 00 02 00 03 00 00 00 00 00 09 00 "
            + string.Join(" ", Enumerable.Repeat("00", 34)) + " 05 00");
    }

    // VB6 keeps a Boolean's bits, so 01 00 reads as a True whose CInt is 1 and which is not `= True` (measured). HexIDE's
    // Boolean can't hold that, so it is refused rather than read as -1. 00 00 and FF FF read as False and True.
    [Theory]
    [InlineData(new byte[] { 0x01, 0x00 })]
    [InlineData(new byte[] { 0xFF, 0x00 })]
    public async Task A_Boolean_whose_bits_are_neither_0_nor_minus_1_is_refused(byte[] bits)
    {
        File.WriteAllBytes(PathOf("b.bin"), bits);
        var run = () => Run($"Dim b As Boolean\nOpen \"{PathOf("b.bin")}\" For Binary As #1\nGet #1, , b\nClose #1\n");

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*Boolean stored as*");
    }

    [Fact]
    public async Task A_Boolean_of_0_or_minus_1_reads_as_False_or_True()
    {
        File.WriteAllBytes(PathOf("b.bin"), [0x00, 0x00, 0xFF, 0xFF]);
        await Run($"Dim b As Boolean\nOpen \"{PathOf("b.bin")}\" For Binary As #1\nGet #1, , b\nDebug.Print b & \" \" & CInt(b)\nGet #1, , b\nDebug.Print b & \" \" & CInt(b)\nClose #1\n");

        Printed().Should().Equal("False 0", "True -1");
    }

    // VB6 takes a Date past the year 9999 and a Decimal with a scale past 28 (measured: 9000372404224, and a Decimal). HexIDE
    // can hold neither, so each is refused. An Error Variant, type 10, waits on CVErr.
    [Theory]
    [InlineData("Dim d As Date", "d", new byte[] { 0x00, 0x00, 0x00, 0x00, 0x20, 0x5F, 0xA0, 0x42 }, "*Date whose serial*")]
    [InlineData("Dim v As Variant", "v", new byte[] { 0x0E, 0x00, 0x00, 0x00, 0x1D, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0F, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, "*scale 29*")]
    [InlineData("Dim v As Variant", "v", new byte[] { 0x0A, 0x00, 0x05, 0x00, 0x0A, 0x80 }, "*Error Variant*")]
    [InlineData("Dim v As Variant", "v", new byte[] { 0x0B, 0x00, 0x01, 0x00 }, "*Boolean stored as*")]
    public async Task What_HexIDE_cannot_hold_is_refused(string declaration, string name, byte[] bytes, string message)
    {
        File.WriteAllBytes(PathOf("g.bin"), bytes);
        var run = () => Run($"{declaration}\nOpen \"{PathOf("g.bin")}\" For Binary As #1\nGet #1, , {name}\nClose #1\n");

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage(message);
    }

    // A literal, a Const or a call can't be got into, and an object variable can't be got or put: VB6's compile errors
    // (measured).
    [Theory]
    [InlineData("Get #1, , 5", "*Variable required*")]
    [InlineData("Const K = 1\nGet #1, , K", "*Variable required*")]
    [InlineData("Dim c As Collection\nGet #1, , c", "*Can't Get or Put an object reference*")]
    [InlineData("Dim c As Collection\nPut #1, , c", "*Can't Get or Put an object reference*")]
    public async Task What_vb6_exe_refuses_to_build_is_its_compile_error(string statements, string message)
    {
        var run = () => Run($"Open \"{PathOf("z.bin")}\" For Binary As #1\n{statements}\nClose #1\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage(message);
    }

    private static string Hex(byte[] bytes) => string.Join(" ", bytes.Select(b => b.ToString("X2")));
}
