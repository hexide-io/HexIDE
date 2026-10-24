using System.IO;
using System.Linq;
using System.Text;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Sequential files (#1145): Open For Input, Output and Append, Line Input #, Print #, Close, FreeFile and EOF. Every
/// expected value is what vb6.exe printed or wrote for the same statements, native and p-code alike: the probe and its
/// log are in docs/vb6-fidelity-oracle.md, "Sequential files".
/// </summary>
public class SequentialFileTests : BaseVBTestFixture, IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hexide-files-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private string PathOf(string name) => Path.Combine(dir, name);

    private List<string?> Printed() => debug.Select(v => v.Value as string).ToList();

    [Fact]
    public async Task Print_of_an_array_raises_13()
    {
        // #879: measured on vb6.exe, native and in the IDE.
        await Run($$"""
            Dim f As Integer
            f = FreeFile
            Open "{{PathOf("array.txt")}}" For Output As #f
            On Error Resume Next
            Print #f, Array(1)
            Debug.Print Err.Number
            Close #f
            """);

        AssertDebugLog([new Vb6Value(13L)]);
    }

    [Fact]
    public async Task Multiple_file_numbers_on_one_line_copy_and_close_both_files()
    {
        File.WriteAllText(PathOf("source.txt"), "alpha\r\nbeta\r\n");
        await Run($$"""
            Dim s As String
            Open "{{PathOf("source.txt")}}" For Input As #1: Open "{{PathOf("copy.txt")}}" For Output As #2
            Do While Not EOF(1)
                Line Input #1, s: Print #2, s
            Loop
            Print #2, "tail";: Close #1, #2
            """);

        File.ReadAllText(PathOf("copy.txt")).Should().Be("alpha\r\nbeta\r\ntail");
    }

    // Reads a file whose bytes are given with Line Input #, as the probe did: each line in brackets, CR and LF shown.
    private async Task<string> ReadBack(string bytes)
    {
        File.WriteAllBytes(PathOf("in.txt"), Encoding.Latin1.GetBytes(bytes));
        debug.Clear();
        await Run($$"""
            Dim f As Integer, s As String, out As String
            f = FreeFile
            Open "{{PathOf("in.txt")}}" For Input As #f
            Do While Not EOF(f)
                Line Input #f, s
                out = out & "[" & Replace(Replace(s, vbCr, "<CR>"), vbLf, "<LF>") & "]"
            Loop
            Close #f
            Debug.Print out
            """);
        return Printed().Single()!;
    }

    [Theory]
    [InlineData("a\r\nb\r\n", "[a][b]")]
    [InlineData("a\r\nb", "[a][b]")]
    [InlineData("a\r\n\r\n", "[a][]")]
    [InlineData("a\rb\rc", "[a][b][c]")]
    [InlineData("a\nb\nc", "[a<LF>b<LF>c]")]
    [InlineData("a\n\rb", "[a<LF>][b]")]
    [InlineData("a\r\nb\u001Ac\r\nd", "[a][b]")]
    [InlineData("", "")]
    public async Task Line_Input_ends_a_line_where_vb6_exe_did(string bytes, string lines)
    {
        (await ReadBack(bytes)).Should().Be(lines);
    }

    [Fact]
    public async Task A_line_is_read_in_the_ANSI_code_page()
    {
        // Byte E9 is é in Windows-1252, the ANSI code page of the probe's guest, of CI and of this project's machines.
        (await ReadBack("café\r\n")).Should().Be("[café]");
    }

    [Fact]
    public async Task Print_moves_with_Spc_and_Tab_as_vb6_exe_does()
    {
        // Measured with Debug.Print's cases, which Print # wrote line for line the same (#1380).
        await Run($$"""
            Dim f As Integer
            f = FreeFile
            Open "{{PathOf("tab.txt")}}" For Output As #f
            Print #f, Spc(3); "x"; Tab(10); "y"
            Print #f, Tab(5); "x"
            Print #f, "a"; Tab; "b"
            Print #f, "abc"; Tab(2); "d"
            Print #f, "x"; Tab(0); "y"
            Close #f
            """);

        File.ReadAllText(PathOf("tab.txt")).Split("\r\n").Should().Equal(
            "   x     y", "    x", "a             b", "abc", " d", "x", "y", "");
    }

    [Fact]
    public async Task Print_writes_the_bytes_vb6_exe_wrote()
    {
        await Run($$"""
            Dim f As Integer, v As Variant
            f = FreeFile
            Open "{{PathOf("print.txt")}}" For Output As #f
            Print #f, "a"
            Print #f, "a";
            Print #f, "b"
            Print #f, "a", "b"
            Print #f, 5
            Print #f, -5
            Print #f, "x"; 5; "y"
            Print #f, 1.5
            Print #f, True
            Print #f,
            Print #f, "a",
            Print #f, "c"
            Print #f, "a";
            Print #f, ; "z"
            v = Null
            Print #f, v
            v = Empty
            Print #f, "e"; v; "e"
            Print #f, CCur(2.5), CSng(0.1), CDbl(1E+20)
            Close #f
            """);

        // The probe's hex dump, less its date line, which follows the guest's locale, and ChrW$(&H416)'s "?", since ChrW
        // is #1134.
        var expected = "61 0D 0A 61 62 0D 0A 61 20 20 20 20 20 20 20 20 20 20 20 20 20 62 0D 0A 20 35 20 0D 0A 2D 35 20 0D 0A "
                       + "78 20 35 20 79 0D 0A 20 31 2E 35 20 0D 0A 54 72 75 65 0D 0A 0D 0A 61 20 20 20 20 20 20 20 20 20 20 20 "
                       + "20 20 63 0D 0A 61 7A 0D 0A 4E 75 6C 6C 0D 0A 65 65 0D 0A 20 32 2E 35 20 20 20 20 20 20 20 20 "
                       + "20 20 20 30 2E 31 20 20 20 20 20 20 20 20 20 20 20 31 45 2B 32 30 20 0D 0A";
        string.Join(" ", File.ReadAllBytes(PathOf("print.txt")).Select(b => b.ToString("X2"))).Should().Be(expected);
    }

    // A character the code page has no byte for is written as Windows writes it: its best fit, Ā as "A", Ł as "L" and α as
    // "a", and "?" only where there is none, as for Ж (measured on 1252, #1511). Every one of them was "?".
    [Fact]
    public async Task A_character_the_code_page_lacks_is_written_as_its_best_fit()
    {
        await Run($$"""
            Dim f As Integer
            f = FreeFile
            Open "{{PathOf("print.txt")}}" For Output As #f
            Print #f, ChrW(&H100) & ChrW(&H416) & ChrW(&H2019) & ChrW(&H141) & ChrW(&H3B1) & ChrW(&H20AC);
            Close #f
            f = FreeFile
            Open "{{PathOf("append.txt")}}" For Append As #f
            Print #f, ChrW(&H100);
            Close #f
            """);

        string.Join(" ", File.ReadAllBytes(PathOf("print.txt")).Select(b => b.ToString("X2"))).Should().Be("41 3F 92 4C 61 80");
        string.Join(" ", File.ReadAllBytes(PathOf("append.txt")).Select(b => b.ToString("X2"))).Should().Be("41");
    }

    [Fact]
    public async Task A_date_is_printed_as_it_reads_with_a_space_after()
    {
        await Run($$"""
            Dim d As Date
            d = #1/2/2003#
            Open "{{PathOf("date.txt")}}" For Output As #1
            Print #1, d;
            Close #1
            Debug.Print CStr(d)
            """);

        File.ReadAllText(PathOf("date.txt"), Encoding.Latin1).Should().Be(Printed().Single() + " ");
    }

    [Fact]
    public async Task Output_truncates_and_Append_adds()
    {
        File.WriteAllText(PathOf("log.txt"), "old text");
        await Run($$"""
            Open "{{PathOf("log.txt")}}" For Output As #1
            Print #1, "one"
            Close #1
            Open "{{PathOf("log.txt")}}" For Append As #1
            Print #1, "two"
            Close #1
            """);

        File.ReadAllText(PathOf("log.txt")).Should().Be("one\r\ntwo\r\n");
    }

    [Fact]
    public async Task FreeFile_EOF_and_Close_answer_as_vb6_exe_did()
    {
        File.WriteAllText(PathOf("ab.txt"), "a\r\nb");
        await Run($$"""
            Dim f As Integer, g As Integer, s As String
            Debug.Print CStr(FreeFile) & " " & FreeFile(0) & " " & FreeFile(1) & " " & TypeName(FreeFile)
            f = FreeFile
            g = FreeFile
            Debug.Print CStr(f) & " " & g
            Open "{{PathOf("ab.txt")}}" For Input As #f
            Debug.Print CStr(FreeFile)
            Line Input #f, s
            Line Input #f, s
            Debug.Print CStr(EOF(f))
            Open "{{PathOf("out.txt")}}" For Output As #5
            Debug.Print CStr(EOF(5))
            Close
            Debug.Print CStr(FreeFile)
            Close #9
            Close #f
            Debug.Print "closed"
            """);

        Printed().Should().Equal("1 1 256 Integer", "1 1", "2", "True", "True", "1", "closed");
    }

    [Fact]
    public async Task The_refusals_are_vb6_exe_s()
    {
        File.WriteAllText(PathOf("ab.txt"), "a\r\nb");
        await Run($$"""
            Dim s As String
            On Error Resume Next
            Open "{{PathOf("nosuch.txt")}}" For Input As #1
            Debug.Print Err.Number & " " & Err.Description: Err.Clear
            Open "{{PathOf("ab.txt")}}" For Input As #1
            Open "{{PathOf("ab.txt")}}" For Input As #1
            Debug.Print Err.Number & " " & Err.Description: Err.Clear
            Print #1, "x"
            Debug.Print Err.Number & " " & Err.Description: Err.Clear
            Line Input #9, s
            Debug.Print Err.Number & " " & Err.Description: Err.Clear
            Line Input #1, s
            Line Input #1, s
            Line Input #1, s
            Debug.Print Err.Number & " " & Err.Description & " [" & s & "]": Err.Clear
            Open "{{PathOf("ab.txt")}}" For Input As #2
            Debug.Print "twice-input " & Err.Number: Err.Clear
            Open "{{PathOf("o.txt")}}" For Output As #3
            Line Input #3, s
            Debug.Print Err.Number & " " & Err.Description: Err.Clear
            Open "{{PathOf("o.txt")}}" For Output As #4
            Debug.Print "twice-output " & Err.Number: Err.Clear
            Open "" For Output As #6
            Debug.Print Err.Number & " " & Err.Description: Err.Clear
            Open "{{PathOf("z.txt")}}" For Output As #0
            Debug.Print Err.Number & " " & Err.Description: Err.Clear
            Open "{{PathOf("lock.txt")}}" For Output Lock Write As #7
            Open "{{PathOf("lock.txt")}}" For Input As #8
            Debug.Print "locked " & Err.Number: Err.Clear
            Close
            Open "{{PathOf("ab.txt")}}" For Input As #1
            Open "{{PathOf("ab.txt")}}" For Output As #2
            Debug.Print "output-while-input " & Err.Number: Err.Clear
            Open "{{PathOf("ab.txt")}}" For Append As #2
            Debug.Print "append-while-input " & Err.Number: Err.Clear
            Close
            Open "{{PathOf("w.txt")}}" For Output As #1
            Open "{{PathOf("w.txt")}}" For Input As #2
            Debug.Print "input-while-output " & Err.Number: Err.Clear
            Close
            Open "{{PathOf("nodir")}}\x.txt" For Output As #1
            Debug.Print Err.Number & " " & Err.Description: Err.Clear
            Open "{{dir}}" For Input As #1
            Debug.Print "directory " & Err.Number: Err.Clear
            Close #0
            Debug.Print "close-0 " & Err.Number: Err.Clear
            Dim g As Integer
            g = -1
            Close #g
            Debug.Print "close-negative " & Err.Number: Err.Clear
            Debug.Print "freefile-2 " & FreeFile(2) & " " & Err.Number
            """);

        Printed().Should().Equal(
            "53 File not found",
            "55 File already open",
            "54 Bad file mode",
            "52 Bad file name or number",
            "62 Input past end of file [b]",
            "twice-input 0",
            "54 Bad file mode",
            "twice-output 55",
            "75 Path/File access error",
            "52 Bad file name or number",
            "locked 55",
            "output-while-input 55",
            "append-while-input 55",
            "input-while-output 55",
            "76 Path not found",
            "directory 75",
            "close-0 0",
            "close-negative 52",
            "freefile-2 256 0");
    }

    // The clauses the inventory rated Dies after #1145 had made them run (#1633): Access Read and Shared are taken on an
    // Input file, a second Input of a file opened Lock Read is 55 as Lock Write's is, and Append makes a missing file.
    [Fact]
    public async Task Access_Shared_Lock_Read_and_Append_answer_as_vb6_exe_did()
    {
        File.WriteAllText(PathOf("ab.txt"), "a\r\nb");
        await Run($$"""
            Dim s As String
            On Error Resume Next
            Open "{{PathOf("ab.txt")}}" For Input Access Read As #1
            Line Input #1, s
            Debug.Print "access-read " & Err.Number & " [" & s & "]": Err.Clear
            Open "{{PathOf("ab.txt")}}" For Input Shared As #2
            Line Input #2, s
            Debug.Print "shared " & Err.Number & " [" & s & "]": Err.Clear
            Close
            Open "{{PathOf("ab.txt")}}" For Input Lock Read As #1
            Open "{{PathOf("ab.txt")}}" For Input As #2
            Debug.Print "lock-read " & Err.Number: Err.Clear
            Close
            Open "{{PathOf("new.txt")}}" For Append As #1
            Print #1, "made"
            Close #1
            Debug.Print "append-missing " & Err.Number
            """);

        Printed().Should().Equal("access-read 0 [a]", "shared 0 [a]", "lock-read 55", "append-missing 0");
        File.ReadAllText(PathOf("new.txt")).Should().Be("made\r\n");
    }

    [Fact]
    public async Task A_file_number_can_be_a_variable_an_expression_or_written_without_its_hash()
    {
        await Run($$"""
            Dim n As Integer, s As String
            n = 3
            Open "{{PathOf("n.txt")}}" For Output As n
            Print #n, "by number"
            Close n
            Open "{{PathOf("n.txt")}}" For Input As #3
            Line Input #3, t
            Close #3
            Debug.Print t
            """);

        // `t` was never declared, and with no Option Explicit Line Input # makes it, as a Let would.
        Printed().Should().Equal("by number");
    }

    [Fact]
    public async Task A_relative_path_lands_in_the_project_s_folder()
    {
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, """
            Open "notes.txt" For Output As #1
            Print #1, "beside the project";
            Close #1
            """);
        vb.SetAppInfo(new AppInfo(Title: "Notes", ExeName: "Notes", Path: dir));
        await vb.Execute();

        File.ReadAllText(PathOf("notes.txt")).Should().Be("beside the project");
    }

    [Fact]
    public async Task A_relative_path_in_an_unsaved_project_is_refused_rather_than_written_where_HexIDE_was_started()
    {
        var run = () => Run("""
            Open "notes.txt" For Output As #1
            """);

        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*saved project*");
    }

    [Theory]
    [InlineData("Print 1, \"x\"")]
    [InlineData("Print \"a\", \"b\"")]
    public async Task Print_without_a_hash_in_a_standard_module_never_reaches_a_file(string statement)
    {
        var run = () => Run($$"""
            Open "{{PathOf("held.txt")}}" For Output As #1
            On Error Resume Next
            {{statement}}
            """);

        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*not implemented*#993*");
        // By its length: the run left the file open, and its writer would refuse a reader that shares no writes.
        new FileInfo(PathOf("held.txt")).Length.Should().Be(0);
    }

    [Fact]
    public async Task A_comma_moves_to_the_next_zone_from_where_the_line_has_got_to()
    {
        await Run($$"""
            Open "{{PathOf("zone.txt")}}" For Output As #1
            Print #1, "12345678901234567890", "x"
            Print #1, "12345678901234", "x"
            Print #1, "1234567890123", "x"
            Print #1, "ab" & vbCrLf & "c", "x"
            Print #1, "ab";
            Print #1, "cd", "x"
            Close #1
            """);

        // vb6.exe's bytes: past column 14 the next zone is 28, and a zone boundary already reached still moves on.
        File.ReadAllText(PathOf("zone.txt")).Should().Be(
            "12345678901234567890" + new string(' ', 8) + "x\r\n"
            + "12345678901234" + new string(' ', 14) + "x\r\n"
            + "1234567890123 x\r\n"
            + "ab\r\nc" + new string(' ', 13) + "x\r\n"
            + "abcd" + new string(' ', 10) + "x\r\n");
    }
}
