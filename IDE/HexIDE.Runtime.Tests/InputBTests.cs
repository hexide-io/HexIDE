using System.IO;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// InputB's bytes and positions, measured in VB6 SP6 native and p-code on
/// https://github.com/hexide-io/HexIDE/issues/4895 (oracle PR #4896).
/// Append and externally changed Binary files: https://github.com/hexide-io/HexIDE/issues/4927
/// (oracle PR #4942, native and p-code identical).
/// Refusals below are HexIDE policy, not invented VB6 results.
/// </summary>
public class InputBTests : BaseVBTestFixture, IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hexide-inputb-").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    private string Make(string hex)
    {
        var path = Path.Combine(dir, "bytes.bin");
        File.WriteAllBytes(path, Convert.FromHexString(hex));
        return path;
    }

    private const string Position = """
        Debug.Print CStr(Loc(1)) & " " & Seek(1) & " " & EOF(1) & " " & LOF(1)
        """;

    private void Bytes(string name, string hex)
    {
        context.TryGetVariable(rootEnv, name, out var value).Should().BeTrue();
        value.StringBytes().Should().Equal(Convert.FromHexString(hex));
    }

    [Theory]
    [InlineData("Binary", "414243", "2", 1, "InputB", "4142", 0, "2 3 False 3", "2 1")]
    [InlineData("Binary", "414243", "LOF(1)", 1, "InputB", "414243", 0, "3 4 False 3", "3 1")]
    [InlineData("Binary", "414243", "0", 1, "InputB", "", 0, "0 1 False 3", "0 0")]
    [InlineData("Binary", "414243", "-1", 1, "InputB", "7800", 5, "0 1 False 3", "2 1")]
    [InlineData("Binary", "414243", "2", 2, "InputB", "4243", 0, "3 4 False 3", "2 1")]
    [InlineData("Binary", "610D0A62", "4", 1, "InputB", "610D0A62", 0, "4 5 False 4", "4 2")]
    [InlineData("Input", "78E979", "3", 1, "InputB", "78E979", 0, "1 4 True 3", "3 1")]
    [InlineData("Input", "78E979", "2", 1, "InputB", "78E9", 0, "1 3 False 3", "2 1")]
    [InlineData("Input", "78E979", "4", 1, "InputB", "", 62, "1 4 True 3", "0 0")]
    [InlineData("Input", "610D0A62", "4", 1, "InputB", "610D0A62", 0, "1 5 True 4", "4 2")]
    [InlineData("Input", "611A62", "3", 1, "InputB", "", 62, "1 6 True 3", "0 0")]
    [InlineData("Input", "611A62", "1", 1, "InputB", "61", 0, "1 2 True 3", "1 0")]
    [InlineData("Input", "78E979", "2", 1, "InputB$", "78E9", 0, "1 3 False 3", "2 1")]
    public async Task A_read_matches_the_measured_bytes_lengths_error_and_position(string mode, string hex,
        string count, int start, string function, string expected, int error, string position, string lengths)
    {
        var path = Make(hex);
        await Run($$"""
            On Error Resume Next
            Dim s As String
            Open "{{path}}" For {{mode}} As #1
            {{(start == 1 ? "" : $"Seek #1, {start}")}}
            {{(count == "-1" ? "s = \"x\"" : "")}}
            s = {{function}}({{count}}, #1)
            Debug.Print CStr(Err.Number)
            {{Position}}
            Debug.Print CStr(LenB(s)) & " " & Len(s)
            Close #1
            """);

        Bytes("s", expected);
        debug.Select(v => v.Value as string).Should().Equal(error.ToString(System.Globalization.CultureInfo.InvariantCulture), position, lengths);
    }

    [Fact]
    public async Task A_second_Binary_read_keeps_the_last_odd_byte()
    {
        var path = Make("414243");
        await Run($$"""
            Dim s As String, t As String
            Open "{{path}}" For Binary As #1
            s = InputB(2, #1)
            t = InputB(1, #1)
            Debug.Print CStr(Err.Number)
            {{Position}}
            Debug.Print CStr(LenB(t)) & " " & Len(t)
            Close #1
            """);

        Bytes("s", "4142");
        Bytes("t", "43");
        debug.Select(v => v.Value as string).Should().Equal("0", "3 4 False 3", "1 0");
    }

    [Fact]
    public async Task Input_reads_from_the_byte_position_InputB_left()
    {
        var path = Make("78E979");
        await Run($$"""
            Dim s As String, t As String
            Open "{{path}}" For Input As #1
            s = InputB(2, #1)
            t = Input(1, #1)
            Debug.Print CStr(Err.Number)
            Debug.Print t
            {{Position}}
            Close #1
            """);

        Bytes("s", "78E9");
        debug.Select(v => v.Value as string).Should().Equal("0", "y", "1 4 True 3");
    }

    [Fact]
    public async Task A_second_Input_read_at_CtrlZ_raises_62_and_keeps_the_target()
    {
        var path = Make("611A62");
        await Run($$"""
            On Error Resume Next
            Dim s As String, t As String
            Open "{{path}}" For Input As #1
            s = InputB(1, #1)
            t = InputB(1, #1)
            Debug.Print CStr(Err.Number)
            {{Position}}
            Close #1
            """);

        Bytes("s", "61");
        Bytes("t", "");
        debug.Select(v => v.Value as string).Should().Equal("62", "1 6 True 3");
    }

    [Theory]
    [InlineData("Output As #2", "1, #2", 54)]
    [InlineData("Random As #3 Len = 2", "2, #3", 54)]
    [InlineData(null, "1, #9", 52)]
    public async Task A_wrong_mode_or_closed_number_raises_the_measured_error(string? open, string arguments, int error)
    {
        var path = Make("414243");
        await Run($$"""
            On Error Resume Next
            Dim s As String
            {{(open is null ? "" : $"Open \"{path}\" For {open}")}}
            s = InputB({{arguments}})
            Debug.Print CStr(Err.Number)
            Close
            """);

        Bytes("s", "");
        AssertDebugLog([new Vb6Value(error.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
    }

    [Fact]
    public async Task A_file_number_without_a_hash_returns_a_String()
    {
        var path = Make("414243");
        await Run($$"""
            Open "{{path}}" For Binary As #1
            Debug.Print TypeName(InputB(1, 1))
            Close #1
            """);

        AssertDebugLog([new Vb6Value("String")]);
    }

    [Fact]
    public async Task The_programs_own_Put_is_not_mistaken_for_an_external_change()
    {
        var path = Make("414243");
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(-2));
        // The same bytes as #4895's Binary file, written by the existing Put path. This checks our refusal policy.
        await Run($$"""
            Dim b As Byte, s As String
            Open "{{path}}" For Binary As #1
            b = 65: Put #1, 1, b
            b = 66: Put #1, 2, b
            b = 67: Put #1, 3, b
            Seek #1, 1
            s = InputB(LOF(1), #1)
            {{Position}}
            Close #1
            """);

        Bytes("s", "414243");
        AssertDebugLog([new Vb6Value("3 4 False 3")]);
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(3, 2)]
    [InlineData(4, 1)]
    public async Task Binary_past_end_is_refused_before_the_position_or_target_changes(int start, int count)
    {
        var path = Make("414243");
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, $$"""
            On Error Resume Next
            Dim s As String
            s = "x"
            Open "{{path}}" For Binary As #1
            Seek #1, {{start}}
            s = InputB({{count}}, #1)
            """);
        try
        {
            var run = () => vb.Execute();
            (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("InputB past the end of a Binary file is refused: VB6 returns stale bytes there (#4927)");
            Bytes("s", "7800");
            vb.BuiltIns.Files.SeekPosition(1).Should().Be(start);
            vb.BuiltIns.Files.Loc(1).Should().Be(start - 1);
            vb.BuiltIns.Files.Eof(1).Should().BeFalse();
        }
        finally { vb.BuiltIns.Files.CloseAll(); }
    }

    [Theory]
    [InlineData("Append", "InputB", 1, false, false, 54)]
    [InlineData("Append", "InputB", 0, false, false, 54)]
    [InlineData("Append", "InputB", -1, false, false, 5)]
    [InlineData("Append", "InputB$", 1, false, false, 54)]
    [InlineData("Append", "InputB", 1, true, false, 54)]
    [InlineData("Append", "InputB", 1, false, true, 54)]
    [InlineData("Output", "InputB", -1, false, false, 5)]
    [InlineData("Output", "InputB", 0, false, false, 54)]
    [InlineData("Append", "Input", -1, false, false, 5)]
    [InlineData("Output", "Input", -1, false, false, 5)]
    public async Task Append_and_Output_check_the_count_before_the_mode(string mode, string function, int count,
        bool seek, bool empty, int error)
    {
        var path = Make(Convert.ToHexString(Letters(26)));
        if (empty) File.Delete(path);
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, $$"""
            On Error Resume Next
            Dim s As String
            s = "x"
            Open "{{path}}" For {{mode}} As #1
            {{(seek ? "Seek #1, 1" : "")}}
            {{Position}}
            Debug.Print CStr(Err.Number)
            s = {{function}}({{count}}, #1)
            Debug.Print CStr(Err.Number)
            {{Position}}
            """);
        try
        {
            await vb.Execute();
            Bytes("s", "7800");
            var position = $"0 1 True {(empty || mode == "Output" ? 0 : 26)}";
            debug.Select(v => v.Value as string).Should().Equal(position, "0",
                error.ToString(System.Globalization.CultureInfo.InvariantCulture), position);
        }
        finally { vb.BuiltIns.Files.CloseAll(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task An_external_change_is_read_in_Binary_and_refused_in_Input(bool binary, bool alreadyRead)
    {
        var path = Make("414243");
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv,
            "Dim s As String\ns = \"x\"\ns = InputB(1, #1)\n");
        var files = vb.BuiltIns.Files;
        try
        {
            files.Open(path, binary ? VbFileMode.Binary : VbFileMode.Input, false, 1);
            if (alreadyRead) files.Read(1, 1);
            var position = files.SeekPosition(1);
            var writtenAt = File.GetLastWriteTimeUtc(path);
            using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                writer.Write(Convert.FromHexString("444546"));
            File.SetLastWriteTimeUtc(path, writtenAt.AddSeconds(2));
            var run = () => vb.Execute();

            if (binary)
            {
                await run();
                Bytes("s", alreadyRead ? "45" : "44");
                files.SeekPosition(1).Should().Be(position + 1);
            }
            else
            {
                (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage(
                    "InputB on an Input file changed while it is open is not implemented (#4944).");
                Bytes("s", "7800");
                files.SeekPosition(1).Should().Be(position);
            }
        }
        finally { files.CloseAll(); }
    }

    private static byte[] Letters(int count, byte first = (byte)'A') =>
        Enumerable.Range(0, count).Select(i => (byte)(first + i % 26)).ToArray();

    [Theory]
    [InlineData(0, false, false, false)]
    [InlineData(10, false, false, false)]
    [InlineData(10, true, false, false)]
    [InlineData(600, false, false, false)]
    [InlineData(10, false, true, false)]
    [InlineData(10, false, false, true)]
    public async Task Binary_reads_every_external_mark_from_its_current_position(int firstRead, bool chunks,
        bool seek, bool writerStaysOpen)
    {
        var bytes = Letters(10000);
        var path = Make(Convert.ToHexString(bytes));
        var remaining = writerStaysOpen ? 600 : bytes.Length - firstRead;
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, $$"""
            Dim s As String
            {{(seek ? "Seek #1, 11" : "")}}
            {{(chunks ? "For i = 1 To 99: s = s & InputB(100, #1): Next i: s = s & InputB(90, #1)" : $"s = InputB({remaining}, #1)")}}
            Debug.Print CStr(Err.Number)
            {{Position}}
            """);
        var files = vb.BuiltIns.Files;
        FileStream? writer = null;
        try
        {
            files.Open(path, VbFileMode.Binary, false, 1);
            if (firstRead > 0) files.ReadBytes(1, firstRead).Should().Equal(bytes.Take(firstRead));
            var writtenAt = File.GetLastWriteTimeUtc(path);
            writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, bufferSize: 0);
            int[] marks = writerStaysOpen ? [11, 520] :
                [11, 256, 257, 512, 513, 514, 1024, 1025, 2048, 2049, 4096, 4097, 4098, 8192, 8193, 10000];
            foreach (var mark in marks)
            {
                writer.Position = mark - 1;
                writer.WriteByte((byte)'#');
                bytes[mark - 1] = (byte)'#';
            }
            if (!writerStaysOpen)
            {
                writer.Dispose();
                writer = null;
            }
            // The still-open writer row must work even when length and timestamp match the Open version.
            File.SetLastWriteTimeUtc(path, writerStaysOpen ? writtenAt : writtenAt.AddSeconds(2));
            await vb.Execute();

            context.TryGetVariable(rootEnv, "s", out var value).Should().BeTrue();
            value.StringBytes().Should().Equal(bytes.Skip(firstRead).Take(remaining));
            var end = firstRead + remaining;
            debug.Select(v => v.Value as string).Should().Equal("0", $"{end} {end + 1} False 10000");
        }
        finally
        {
            writer?.Dispose();
            files.CloseAll();
        }
    }

    [Theory]
    [InlineData("grow", false, 2990, 10, "3000 3001 False 3010", "3010 3011 False 3010")]
    [InlineData("cut", false, 90, 0, "100 101 False 100", null)]
    [InlineData("rewrite", false, 2990, 2000, "3000 3001 False 5000", "5000 5001 False 5000")]
    [InlineData("rewrite", true, 2990, 2000, "3000 3001 False 5000", "5000 5001 False 5000")]
    public async Task Binary_reads_the_current_length_and_contents(string change, bool seek, int count, int nextCount,
        string position, string? nextPosition)
    {
        var bytes = Letters(3000);
        var path = Make(Convert.ToHexString(bytes));
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, $$"""
            Dim s As String, t As String
            {{(seek ? "Seek #1, 11" : "")}}
            s = InputB({{count}}, #1)
            Debug.Print CStr(Err.Number)
            {{Position}}
            {{(nextCount == 0 ? "" : $"t = InputB({nextCount}, #1)\nDebug.Print CStr(Err.Number)\n{Position}")}}
            """);
        var files = vb.BuiltIns.Files;
        try
        {
            files.Open(path, VbFileMode.Binary, false, 1);
            files.ReadBytes(1, 10).Should().Equal(bytes.Take(10));
            bytes = change switch
            {
                "grow" => [.. bytes, .. Enumerable.Repeat((byte)'#', 10)],
                "cut" => bytes[..100],
                _ => Letters(5000, (byte)'a'),
            };
            using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            {
                writer.SetLength(bytes.Length);
                writer.Write(bytes);
            }
            await vb.Execute();

            context.TryGetVariable(rootEnv, "s", out var value).Should().BeTrue();
            value.StringBytes().Should().Equal(bytes.Skip(10).Take(count));
            if (nextCount > 0)
            {
                context.TryGetVariable(rootEnv, "t", out value).Should().BeTrue();
                value.StringBytes().Should().Equal(bytes.Skip(10 + count).Take(nextCount));
                debug.Select(v => v.Value as string).Should().Equal("0", position, "0", nextPosition);
            }
            else debug.Select(v => v.Value as string).Should().Equal("0", position);
        }
        finally { files.CloseAll(); }
    }

    [Fact]
    public async Task Binary_past_end_after_an_external_cut_is_refused_at_the_current_end()
    {
        var bytes = Letters(3000);
        var path = Make(Convert.ToHexString(bytes));
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, """
            Dim s As String
            s = InputB(90, #1)
            s = InputB(10, #1)
            """);
        var files = vb.BuiltIns.Files;
        try
        {
            files.Open(path, VbFileMode.Binary, false, 1);
            files.ReadBytes(1, 10);
            using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                writer.SetLength(100);
            var run = () => vb.Execute();
            (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage(
                "InputB past the end of a Binary file is refused: VB6 returns stale bytes there (#4927)");
            context.TryGetVariable(rootEnv, "s", out var value).Should().BeTrue();
            value.StringBytes().Should().Equal(bytes.Skip(10).Take(90));
            files.SeekPosition(1).Should().Be(101);
            files.Loc(1).Should().Be(100);
        }
        finally { files.CloseAll(); }
    }

    // Preserve SnapshotOf's access check (Random and Binary files oracle, #1646); this already passes on main.
    [Fact]
    public async Task Binary_without_read_access_keeps_error_75_and_the_target()
    {
        var path = Make("414243");
        await Run($$"""
            On Error Resume Next
            Dim s As String
            s = "x"
            Open "{{path}}" For Binary Access Write As #1
            s = InputB(1, #1)
            Debug.Print CStr(Err.Number)
            Close #1
            """);
        Bytes("s", "7800");
        AssertDebugLog([new Vb6Value("75")]);
    }

    // Argument rules shared with Input (lof-loc-seek-and-the-input-function.md); three pass on main already.
    [Theory]
    [InlineData("InputB(Number:=2, FileNumber:=1)")]
    [InlineData("InputB(2, FileNumber:=1)")]
    [InlineData("InputB(1)")]
    [InlineData("InputB(1, 1, 1)")]
    public async Task Named_or_wrong_argument_counts_still_raise_Syntax_error(string expression)
    {
        var run = () => Run($"Dim s As String\ns = {expression}\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Syntax error*");
    }
}
