using System.IO;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// InputB's bytes and positions, measured in VB6 SP6 native and p-code on
/// https://github.com/hexide-io/HexIDE/issues/4895 (oracle PR #4896).
/// Refusals below are HexIDE policy from that issue, not invented VB6 results.
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
            (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*InputB*past*end*#4927*");
            Bytes("s", "7800");
            vb.BuiltIns.Files.SeekPosition(1).Should().Be(start);
            vb.BuiltIns.Files.Loc(1).Should().Be(start - 1);
            vb.BuiltIns.Files.Eof(1).Should().BeFalse();
        }
        finally { vb.BuiltIns.Files.CloseAll(); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task Append_is_explicitly_refused(int count)
    {
        var path = Make("414243");
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv,
            $"Open \"{path}\" For Append As #1\nDim s As String\ns = InputB({count}, #1)\n");
        try
        {
            var run = () => vb.Execute();

            (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*InputB*Append*#4927*");
        }
        finally { vb.BuiltIns.Files.CloseAll(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_file_changed_outside_the_program_is_refused(bool binary, bool alreadyRead)
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

            (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*InputB*changed*#4927*");
            Bytes("s", "7800");
            files.SeekPosition(1).Should().Be(position);
        }
        finally { files.CloseAll(); }
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
