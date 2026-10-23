using System.IO;
using System.Linq;
using System.Text;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// LOF, Loc, Seek and the Input function on sequential files (#1645). Each program is a slice of a probe vb6.exe ran with
/// /make, native and p-code agreeing, and each expected line is what it logged: docs/vb6-fidelity-oracle.md, "LOF, Loc,
/// Seek and the Input function". The probe made its files with Binary Put, which HexIDE has not got (#1646), so each test
/// writes the same bytes itself. The en-GB guest's code page was 1252.
/// </summary>
public class FilePositionTests : BaseVBTestFixture, IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hexide-positions-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private string PathOf(string name) => Path.Combine(dir, name);

    private string Folder => dir + Path.DirectorySeparatorChar;

    private static byte[] Latin1(string s) => Encoding.Latin1.GetBytes(s);

    private void Make(string name, string text) => File.WriteAllBytes(PathOf(name), Latin1(text));

    private void Make(string name, byte[] bytes) => File.WriteAllBytes(PathOf(name), bytes);

    private List<string?> Printed() => debug.Select(v => v.Value as string).ToList();

    private const string Helpers = """
        Public Function Show(ByVal s As String) As String
            Dim i As Long, c As String, r As String
            For i = 1 To Len(s)
                c = Mid$(s, i, 1)
                If AscW(c) < 32 Or AscW(c) > 126 Then r = r & "<" & AscW(c) & ">" Else r = r & c
            Next
            Show = "[" & r & "]"
        End Function

        Public Function E() As String
            E = " err " & Err.Number
            Err.Clear
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

    // a.txt: Input, Line Input, Seek and the positions after each.
    [Fact]
    public async Task Reading_a_file_by_characters_lines_and_positions()
    {
        Make("a.txt", "ab\r\nc,d\r\n\"q\"\r\n");
        await Run(Helpers + $$"""

            Sub Main()
                Dim s As String, t As String, i As Long
                On Error Resume Next
                Open "{{PathOf("a.txt")}}" For Input As #1
                Debug.Print "open:" & E() & Where(1)
                Debug.Print "types: " & TypeName(LOF(1)) & " " & TypeName(Loc(1)) & " " & TypeName(Seek(1)) & " " & TypeName(Input(1, #1)) & E()
                Close #1
                Open "{{PathOf("a.txt")}}" For Input As #1
                s = Input(3, #1)
                Debug.Print "Input(3, #1): " & Show(s) & E() & Where(1)
                s = Input(5, 1)
                Debug.Print "Input(5, 1): " & Show(s) & E() & Where(1)
                Line Input #1, s
                Debug.Print "then Line Input: " & Show(s) & E() & Where(1)
                s = "kept"
                s = Input(100, #1)
                Debug.Print "Input(100) past the end: " & Show(s) & E() & Where(1)
                s = Input(LOF(1) - Seek(1) + 1, #1)
                Debug.Print "the rest: " & Show(s) & E() & Where(1)
                s = "kept"
                s = Input(1, #1)
                Debug.Print "Input(1) at the end: " & Show(s) & E() & Where(1)
                Close #1
                Open "{{PathOf("a.txt")}}" For Input As #1
                s = Input(LOF(1), 1)
                Debug.Print "Input(LOF(1), 1): " & Show(s) & E() & Where(1)
                Close #1
                Open "{{PathOf("a.txt")}}" For Input As #1
                s = "kept"
                s = Input(0, #1)
                Debug.Print "Input(0): " & Show(s) & E() & Where(1)
                s = "kept"
                s = Input(-1, #1)
                Debug.Print "Input(-1): " & Show(s) & E() & Where(1)
                s = Input(1.6, #1)
                Debug.Print "Input(1.6): " & Show(s) & E() & Where(1)
                Seek #1, 5
                Debug.Print "Seek #1, 5:" & E() & Where(1)
                s = Input(3, #1)
                Debug.Print "then Input(3): " & Show(s) & E() & Where(1)
                Seek #1, 1
                Line Input #1, s
                Debug.Print "Seek #1, 1 then Line Input: " & Show(s) & E() & Where(1)
                Seek #1, 0
                Debug.Print "Seek #1, 0:" & E() & Where(1)
                Seek #1, -1
                Debug.Print "Seek #1, -1:" & E() & Where(1)
                Seek #1, 14
                Debug.Print "Seek #1, 14:" & E() & Where(1)
                Seek #1, 15
                Debug.Print "Seek #1, 15:" & E() & Where(1)
                s = "kept"
                s = Input(1, #1)
                Debug.Print "then Input(1): " & Show(s) & E() & Where(1)
                Seek #1, 100
                Debug.Print "Seek #1, 100:" & E() & Where(1)
                Line Input #1, s
                Debug.Print "then Line Input: " & Show(s) & E() & Where(1)
                Seek #1, 3
                s = Input(1, #1)
                Line Input #1, t
                Debug.Print "Seek 3, Input(1), Line Input: " & Show(s) & " " & Show(t) & E() & Where(1)
                Close #1
                Open "{{PathOf("a.txt")}}" For Input As #5
                Open "{{PathOf("a.txt")}}" For Input As #6
                s = Input(3, #5)
                Debug.Print "two handles:" & Where(5) & " /" & E() & Where(6)
                Close #5
                Close #6
            End Sub

            Main
            """);

        Printed().Should().Equal(
            "open: err 0 LOF 14 Loc 1 Seek 1 EOF False",
            "types: Long Long Long String err 0",
            "Input(3, #1): [ab<13>] err 0 LOF 14 Loc 1 Seek 4 EOF False",
            "Input(5, 1): [<10>c,d<13>] err 0 LOF 14 Loc 1 Seek 9 EOF False",
            "then Line Input: [<10>\"q\"] err 0 LOF 14 Loc 1 Seek 15 EOF True",
            "Input(100) past the end: [kept] err 62 LOF 14 Loc 1 Seek 15 EOF True",
            "the rest: [] err 0 LOF 14 Loc 1 Seek 15 EOF True",
            "Input(1) at the end: [kept] err 62 LOF 14 Loc 1 Seek 15 EOF True",
            "Input(LOF(1), 1): [ab<13><10>c,d<13><10>\"q\"<13><10>] err 0 LOF 14 Loc 1 Seek 15 EOF True",
            "Input(0): [] err 0 LOF 14 Loc 1 Seek 1 EOF False",
            "Input(-1): [kept] err 5 LOF 14 Loc 1 Seek 1 EOF False",
            "Input(1.6): [ab] err 0 LOF 14 Loc 1 Seek 3 EOF False",
            "Seek #1, 5: err 0 LOF 14 Loc 1 Seek 5 EOF False",
            "then Input(3): [c,d] err 0 LOF 14 Loc 1 Seek 8 EOF False",
            "Seek #1, 1 then Line Input: [ab] err 0 LOF 14 Loc 1 Seek 5 EOF False",
            "Seek #1, 0: err 63 LOF 14 Loc 1 Seek 5 EOF False",
            "Seek #1, -1: err 63 LOF 14 Loc 1 Seek 5 EOF False",
            "Seek #1, 14: err 0 LOF 14 Loc 1 Seek 14 EOF False",
            "Seek #1, 15: err 0 LOF 14 Loc 1 Seek 15 EOF True",
            "then Input(1): [kept] err 62 LOF 14 Loc 1 Seek 15 EOF True",
            "Seek #1, 100: err 0 LOF 14 Loc 1 Seek 100 EOF True",
            "then Line Input: [kept] err 62 LOF 14 Loc 1 Seek 100 EOF True",
            "Seek 3, Input(1), Line Input: [<13>] [<10>c,d] err 0 LOF 14 Loc 1 Seek 10 EOF False",
            "two handles: LOF 14 Loc 1 Seek 4 EOF False / err 0 LOF 14 Loc 1 Seek 1 EOF False");
    }

    // Input(100) from the start, and counts 2.5, 0.5, 1.5 and "2".
    [Fact]
    public async Task Input_past_the_end_and_a_count_that_is_not_whole()
    {
        Make("a.txt", "ab\r\nc,d\r\n\"q\"\r\n");
        await Run(Helpers + $$"""

            Sub Main()
                Dim s As String, t As String, i As Long
                On Error Resume Next
                Open "{{PathOf("a.txt")}}" For Input As #1
                s = "kept"
                s = Input(100, #1)
                Debug.Print "Input(100) from the start: " & E() & Show(s) & Where(1)
                Line Input #1, s
                Debug.Print "then Line Input: " & E() & Show(s) & Where(1)
                Close #1
                Open "{{PathOf("a.txt")}}" For Input As #1
                s = Input(2.5, #1)
                Debug.Print "Input(2.5): " & E() & Show(s) & Where(1)
                s = Input(0.5, #1)
                Debug.Print "Input(0.5): " & E() & Show(s) & Where(1)
                s = Input(1.5, #1)
                Debug.Print "Input(1.5): " & E() & Show(s) & Where(1)
                s = Input("2", #1)
                Debug.Print "Input(""2""): " & E() & Show(s) & Where(1)
                Close #1
            End Sub

            Main
            """);

        Printed().Should().Equal(
            "Input(100) from the start:  err 62[kept] LOF 14 Loc 1 Seek 15 EOF True",
            "then Line Input:  err 62[kept] LOF 14 Loc 1 Seek 15 EOF True",
            "Input(2.5):  err 0[ab] LOF 14 Loc 1 Seek 3 EOF False",
            "Input(0.5):  err 0[] LOF 14 Loc 1 Seek 3 EOF False",
            "Input(1.5):  err 0[<13><10>] LOF 14 Loc 1 Seek 5 EOF False",
            "Input(\"2\"):  err 0[c,] LOF 14 Loc 1 Seek 7 EOF False");
    }

    // z.txt, and six more files with a Ctrl+Z: Input, Line Input, and Seek past it.
    [Fact]
    public async Task A_Ctrl_Z_ends_the_file_and_moves_Seek_as_vb6_exe_does()
    {
        Make("z.txt", "ab\u001Acd");
        Make("z1.txt", "\u001A");
        Make("z2.txt", "a\u001A");
        Make("z3.txt", "ab\u001A");
        Make("z9.txt", "abc\u001Adefgh");
        Make("z5.txt", "ab\u001Acd");
        Make("zlong.txt", new string('x', 200) + "\u001Acd");
        await Run(Helpers + $$"""

            Sub Main()
                Dim s As String, t As String, i As Long
                On Error Resume Next
                Open "{{PathOf("z.txt")}}" For Input As #1
                Debug.Print "z.txt open:" & E() & Where(1)
                s = Input(2, #1)
                Debug.Print "z.txt Input(2): " & Show(s) & E() & Where(1)
                s = "kept"
                s = Input(1, #1)
                Debug.Print "z.txt Input(1) at the Ctrl+Z: " & Show(s) & E() & Where(1)
                Close #1
                Open "{{PathOf("z.txt")}}" For Input As #1
                s = "kept"
                s = Input(5, #1)
                Debug.Print "z.txt Input(5): " & Show(s) & E() & Where(1)
                Close #1
                For i = 1 To 6
                    t = Choose(i, "z1.txt", "z2.txt", "z3.txt", "z9.txt", "z5.txt", "zlong.txt")
                    Open "{{Folder}}" & t For Input As #1
                    Debug.Print t & " open: " & E() & Where(1)
                    s = "kept"
                    s = Input(LOF(1), #1)
                    Debug.Print t & " Input(LOF): " & E() & Show(s) & Where(1)
                    Close #1
                    Open "{{Folder}}" & t For Input As #1
                    s = "kept"
                    Line Input #1, s
                    Debug.Print t & " Line Input: " & E() & Show(s) & Where(1)
                    s = "kept"
                    Line Input #1, s
                    Debug.Print t & " second Line Input: " & E() & Show(s) & Where(1)
                    Close #1
                Next
                Open "{{PathOf("z5.txt")}}" For Input As #1
                Seek #1, 4
                Debug.Print "z5 Seek 4: " & E() & Where(1)
                s = Input(2, #1)
                Debug.Print "z5 then Input(2): " & E() & Show(s) & Where(1)
                Close #1
                Open "{{PathOf("z5.txt")}}" For Input As #1
                s = Input(2, #1)
                s = "kept"
                s = Input(1, #1)
                Debug.Print "z5 Input(2), Input(1): " & E() & Show(s) & Where(1)
                s = "kept"
                s = Input(1, #1)
                Debug.Print "z5 again Input(1): " & E() & Show(s) & Where(1)
                Close #1
            End Sub

            Main
            """);

        Printed().Should().Equal(
            "z.txt open: err 0 LOF 5 Loc 1 Seek 1 EOF False",
            "z.txt Input(2): [ab] err 0 LOF 5 Loc 1 Seek 3 EOF True",
            "z.txt Input(1) at the Ctrl+Z: [kept] err 62 LOF 5 Loc 1 Seek 9 EOF True",
            "z.txt Input(5): [kept] err 62 LOF 5 Loc 1 Seek 9 EOF True",
            "z1.txt open:  err 0 LOF 1 Loc 1 Seek 1 EOF True",
            "z1.txt Input(LOF):  err 62[kept] LOF 1 Loc 1 Seek 3 EOF True",
            "z1.txt Line Input:  err 62[kept] LOF 1 Loc 1 Seek 3 EOF True",
            "z1.txt second Line Input:  err 62[kept] LOF 1 Loc 1 Seek 3 EOF True",
            "z2.txt open:  err 0 LOF 2 Loc 1 Seek 1 EOF False",
            "z2.txt Input(LOF):  err 62[kept] LOF 2 Loc 1 Seek 5 EOF True",
            "z2.txt Line Input:  err 0[a] LOF 2 Loc 1 Seek 5 EOF True",
            "z2.txt second Line Input:  err 62[kept] LOF 2 Loc 1 Seek 5 EOF True",
            "z3.txt open:  err 0 LOF 3 Loc 1 Seek 1 EOF False",
            "z3.txt Input(LOF):  err 62[kept] LOF 3 Loc 1 Seek 7 EOF True",
            "z3.txt Line Input:  err 0[ab] LOF 3 Loc 1 Seek 7 EOF True",
            "z3.txt second Line Input:  err 62[kept] LOF 3 Loc 1 Seek 7 EOF True",
            "z9.txt open:  err 0 LOF 9 Loc 1 Seek 1 EOF False",
            "z9.txt Input(LOF):  err 62[kept] LOF 9 Loc 1 Seek 14 EOF True",
            "z9.txt Line Input:  err 0[abc] LOF 9 Loc 1 Seek 14 EOF True",
            "z9.txt second Line Input:  err 62[kept] LOF 9 Loc 1 Seek 14 EOF True",
            "z5.txt open:  err 0 LOF 5 Loc 1 Seek 1 EOF False",
            "z5.txt Input(LOF):  err 62[kept] LOF 5 Loc 1 Seek 9 EOF True",
            "z5.txt Line Input:  err 0[ab] LOF 5 Loc 1 Seek 9 EOF True",
            "z5.txt second Line Input:  err 62[kept] LOF 5 Loc 1 Seek 9 EOF True",
            "zlong.txt open:  err 0 LOF 203 Loc 1 Seek 1 EOF False",
            "zlong.txt Input(LOF):  err 62[kept] LOF 203 Loc 4 Seek 405 EOF True",
            "zlong.txt Line Input:  err 0[xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx] LOF 203 Loc 4 Seek 405 EOF True",
            "zlong.txt second Line Input:  err 62[kept] LOF 203 Loc 4 Seek 405 EOF True",
            "z5 Seek 4:  err 0 LOF 5 Loc 1 Seek 4 EOF False",
            "z5 then Input(2):  err 0[cd] LOF 5 Loc 1 Seek 6 EOF True",
            "z5 Input(2), Input(1):  err 62[kept] LOF 5 Loc 1 Seek 9 EOF True",
            "z5 again Input(1):  err 62[kept] LOF 5 Loc 1 Seek 9 EOF True");
    }

    // hi.txt in code page 1252, Input$, an empty file, and Loc through 300 bytes.
    [Fact]
    public async Task Characters_in_the_ANSI_code_page_an_empty_file_and_Loc_in_128_byte_blocks()
    {
        Make("hi.txt", Latin1("x\u00E9y"));
        Make("empty.txt", "");
        Make("big.txt", new string('x', 298) + "\r\n");
        await Run(Helpers + $$"""

            Sub Main()
                Dim s As String, t As String, i As Long
                On Error Resume Next
                Open "{{PathOf("hi.txt")}}" For Input As #1
                s = Input(3, #1)
                Debug.Print "hi.txt Input(3): " & Show(s) & E() & Where(1)
                Close #1
                Open "{{PathOf("hi.txt")}}" For Input As #1
                s = Input$(2, #1)
                Debug.Print "hi.txt Input$(2): " & Show(s) & " " & TypeName(Input$(1, #1)) & E() & Where(1)
                Close #1
                Open "{{PathOf("empty.txt")}}" For Input As #1
                s = "kept"
                s = Input(1, #1)
                Debug.Print "empty Input(1): " & Show(s) & E() & Where(1)
                Close #1
                Open "{{PathOf("big.txt")}}" For Input As #1
                s = Input(127, #1)
                Debug.Print "big after 127:" & E() & Where(1)
                s = Input(1, #1)
                Debug.Print "big after 128:" & E() & Where(1)
                s = Input(1, #1)
                Debug.Print "big after 129:" & E() & Where(1)
                s = Input(127, #1)
                Debug.Print "big after 256:" & E() & Where(1)
                s = Input(1, #1)
                Debug.Print "big after 257:" & E() & Where(1)
                Close #1
            End Sub

            Main
            """);

        Printed().Should().Equal(
            "hi.txt Input(3): [x<233>y] err 0 LOF 3 Loc 1 Seek 4 EOF True",
            "hi.txt Input$(2): [x<233>] String err 0 LOF 3 Loc 1 Seek 4 EOF True",
            "empty Input(1): [kept] err 62 LOF 0 Loc 1 Seek 1 EOF True",
            "big after 127: err 0 LOF 300 Loc 1 Seek 128 EOF False",
            "big after 128: err 0 LOF 300 Loc 1 Seek 129 EOF False",
            "big after 129: err 0 LOF 300 Loc 2 Seek 130 EOF False",
            "big after 256: err 0 LOF 300 Loc 2 Seek 257 EOF False",
            "big after 257: err 0 LOF 300 Loc 3 Seek 258 EOF False");
    }

    // LOF, Loc, Seek, the Seek statement and Input on a number not open.
    [Fact]
    public async Task A_number_not_open_is_52()
    {
        await Run(Helpers + $$"""

            Sub Main()
                Dim s As String, t As String, i As Long
                On Error Resume Next
                i = LOF(40)
                Debug.Print "LOF(40):" & E()
                i = Loc(40)
                Debug.Print "Loc(40):" & E()
                i = Seek(40)
                Debug.Print "Seek(40):" & E()
                Seek #40, 1
                Debug.Print "Seek #40, 1:" & E()
                s = Input(1, #40)
                Debug.Print "Input(1, #40):" & E()
            End Sub

            Main
            """);

        Printed().Should().Equal(
            "LOF(40): err 52",
            "Loc(40): err 52",
            "Seek(40): err 52",
            "Seek #40, 1: err 52",
            "Input(1, #40): err 52");
    }

    // o.txt, o2.txt and ap.txt, then e.txt, ap.txt again and ap2.txt: positions, and the bytes written.
    [Fact]
    public async Task Output_and_Append_count_where_they_write_and_Seek_moves_it()
    {
        Make("ap.txt", "xy\r\n");
        Make("e.txt", "");
        Make("ap2.txt", new string('q', 300));
        await Run(Helpers + $$"""

            Sub Main()
                Dim s As String, t As String, i As Long
                On Error Resume Next
                Open "{{PathOf("o.txt")}}" For Output As #2
                Debug.Print "Output open:" & E() & Where(2)
                Print #2, "abc"
                s = "kept"
                s = Input(1, #2)
                Debug.Print "Input on Output: " & Show(s) & E()
                Print #2, String$(300, "y")
                Close #2
                Open "{{PathOf("o2.txt")}}" For Output As #2
                Print #2, "abcdef";
                Seek #2, 2
                Debug.Print "Output Seek #2, 2:" & E() & Where(2)
                Print #2, "XY";
                Debug.Print "then Print XY:" & E() & Where(2)
                Seek #2, 10
                Print #2, "Z";
                Debug.Print "Seek #2, 10 then Print Z:" & E() & Where(2)
                Close #2
                Open "{{PathOf("ap.txt")}}" For Append As #3
                Debug.Print "Append open:" & E() & Where(3)
                Print #3, "z"
                Debug.Print "Append after Print z:" & E() & Where(3)
                s = "kept"
                s = Input(1, #3)
                Debug.Print "Input on Append: " & Show(s) & E()
                Seek #3, 1
                Print #3, "Q";
                Debug.Print "Append Seek #3, 1 then Print Q:" & E() & Where(3)
                Close #3
                Open "{{PathOf("e.txt")}}" For Output As #2
                Seek #2, 5
                Print #2, "a";
                Debug.Print "empty Output Seek 5, Print a: " & E() & Where(2)
                Close #2
                Open "{{PathOf("ap.txt")}}" For Output As #9
                Print #9, "xy" & vbCrLf;
                Close #9
                Open "{{PathOf("ap.txt")}}" For Append As #3
                Seek #3, 5
                Debug.Print "Append Seek #3, 5: " & E() & Where(3)
                Print #3, "z";
                Debug.Print "then Print z: " & E() & Where(3)
                Close #3
                Open "{{PathOf("ap2.txt")}}" For Output As #9
                Print #9, String$(300, "q");
                Close #9
                Open "{{PathOf("ap2.txt")}}" For Append As #3
                Debug.Print "Append to 300 bytes: " & E() & Where(3)
                Print #3, String$(200, "r");
                Debug.Print "after 200 more: " & E() & Where(3)
                Close #3
            End Sub

            Main
            """);

        Printed().Should().Equal(
            "Output open: err 0 LOF 0 Loc 0 Seek 1 EOF True",
            "Input on Output: [kept] err 54",
            "Output Seek #2, 2: err 0 LOF 6 Loc 0 Seek 2 EOF True",
            "then Print XY: err 0 LOF 6 Loc 0 Seek 4 EOF True",
            "Seek #2, 10 then Print Z: err 0 LOF 10 Loc 0 Seek 11 EOF True",
            "Append open: err 0 LOF 4 Loc 0 Seek 1 EOF True",
            "Append after Print z: err 0 LOF 7 Loc 0 Seek 4 EOF True",
            "Input on Append: [kept] err 54",
            "Append Seek #3, 1 then Print Q: err 0 LOF 7 Loc 0 Seek 2 EOF True",
            "empty Output Seek 5, Print a:  err 0 LOF 5 Loc 0 Seek 6 EOF True",
            "Append Seek #3, 5:  err 0 LOF 4 Loc 0 Seek 5 EOF True",
            "then Print z:  err 0 LOF 5 Loc 0 Seek 6 EOF True",
            "Append to 300 bytes:  err 0 LOF 300 Loc 0 Seek 1 EOF True",
            "after 200 more:  err 0 LOF 500 Loc 1 Seek 201 EOF True");
        Show(File.ReadAllBytes(PathOf("o2.txt"))).Should().Be("[aXYdef<0><0><0>Z]");
        Show(File.ReadAllBytes(PathOf("e.txt"))).Should().Be("[<0><0><0><0>a]");
        Show(File.ReadAllBytes(PathOf("ap.txt"))).Should().Be("[xy<13><10>z]");
        File.ReadAllBytes(PathOf("ap2.txt")).Length.Should().Be(500);
    }

    // What vb6.exe refuses to build: a # file number anywhere but Input's second argument, and Input with one argument.
    [Theory]
    [InlineData("Debug.Print LOF(#1)")]
    [InlineData("Debug.Print EOF(#1)")]
    [InlineData("Debug.Print Loc(#1)")]
    [InlineData("Debug.Print Seek(#1)")]
    [InlineData("Debug.Print Input(1)")]
    public async Task A_form_vb6_exe_refuses_is_a_syntax_error(string statement)
    {
        Make("a.txt", "abc");
        var run = () => Run($"Open \"{PathOf("a.txt")}\" For Input As #1\n{statement}\nClose #1\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Syntax error*");
    }

    // The Seek statement takes its file number without the #, as Close does (measured).
    [Fact]
    public async Task Seek_takes_a_file_number_without_its_hash()
    {
        Make("a.txt", "abc");
        await Run($"Open \"{PathOf("a.txt")}\" For Input As #1\nSeek 1, 2\nDebug.Print Input(2, #1)\nClose #1\n");

        Printed().Should().Equal("bc");
    }

    // The bytes as the probe's Show wrote them.
    private static string Show(byte[] bytes) =>
        "[" + string.Concat(bytes.Select(b => b is < 32 or > 126 ? $"<{b}>" : ((char)b).ToString())) + "]";
}
