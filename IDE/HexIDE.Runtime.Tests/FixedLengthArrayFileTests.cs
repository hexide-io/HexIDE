using System.IO;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// #179: vb6.exe SP6 native/p-code rows in docs/vb6-fidelity-oracle/fixed-length-strings.md,
// "Arrays of fixed-length Strings, the width and parameters" (Put/Get and code-page-1252 tables).
public class FixedLengthArrayFileTests : BaseVBTestFixture, IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hexide-fixed-array-records-").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    private string PathOf(string name) => Path.Combine(dir, name);

    private async Task Execute(string code)
    {
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, code);
        try { await vb.Execute(); }
        finally { vb.BuiltIns.Files.CloseAll(); }
    }

    [Theory]
    [InlineData("Binary", false, "616220777879000000")]
    [InlineData("Binary", true, "616220777879000000")]
    [InlineData("Random", false, "616220777879000000")]
    [InlineData("Random", true, "01000300000000000000616220777879000000")]
    public async Task Put_writes_width_bytes_per_element_and_only_Random_dynamic_descriptor(string mode, bool dynamic, string hex)
    {
        var path = PathOf("put.dat");
        var declaration = dynamic ? "Dim a() As String * 3\nReDim a(2)" : "Dim a(2) As String * 3";
        await Execute($"{declaration}\na(0) = \"ab\"\na(1) = \"wxyz\"\nOpen \"{path}\" For {mode} As #1 Len = 32\nPut #1, 1, a\nClose #1");
        File.ReadAllBytes(path).Should().Equal(Convert.FromHexString(hex));
    }

    [Fact]
    public async Task Random_Put_writes_reversed_dimensions_and_first_index_fastest()
    {
        var path = PathOf("dimensions.dat");
        await Execute($"Dim a() As String * 3\nReDim a(1 To 2, 0 To 1)\na(1, 0) = \"ab\"\na(2, 1) = \"cd\"\nOpen \"{path}\" For Random As #1 Len = 32\nPut #1, 1, a\nClose #1");
        File.ReadAllBytes(path).Should().Equal(Convert.FromHexString(
            "0200" + "0200000000000000" + "0200000001000000" + "616220000000000000636420"));
    }

    [Fact]
    public async Task Random_Put_with_record_too_short_raises_59_and_writes_nothing()
    {
        var path = PathOf("short.dat");
        await Execute($"Dim a(2) As String * 3\na(0) = \"ab\"\na(1) = \"wxyz\"\nOpen \"{path}\" For Random As #1 Len = 4\nOn Error Resume Next\nPut #1, 1, a\nDebug.Print Err.Number\nClose #1");
        AssertDebugLog([new Vb6Value(59L)]);
        File.ReadAllBytes(path).Should().BeEmpty();
    }

    [Theory]
    [InlineData("ChrW(&H3B1) & \"b\"", "616220")]
    [InlineData("ChrW(&H416) & \"b\"", "3F6220")]
    [InlineData("ChrW(&H101) & ChrW(&H20AC)", "618020")]
    public async Task Binary_Put_array_uses_ANSI_best_fit(string expression, string hex)
    {
        await PutAnsi("Dim a(0) As String * 3", "a(0)", "a", expression, hex);
    }

    [Theory]
    [InlineData("Dim s As String", "ChrW(&H3B1) & \"b\"", "6162")]
    [InlineData("Dim s As String", "ChrW(&H416) & \"b\"", "3F62")]
    [InlineData("Dim s As String * 3", "ChrW(&H3B1) & \"b\"", "616220")]
    public async Task Binary_Put_scalar_ANSI_regression(string declaration, string expression, string hex)
    {
        await PutAnsi(declaration, "s", "s", expression, hex);
    }

    private async Task PutAnsi(string declaration, string target, string operand, string expression, string hex)
    {
        var path = PathOf("ansi.dat");
#if DEBUG
        var codePage = HexIDE.IDE.Vb6TextEncoding.Ansi.CodePage;
        HexIDE.IDE.Vb6TextEncoding.SimulateHostCodePage(1252);
#endif
        try
        {
            await Execute($"{declaration}\n{target} = {expression}\nOpen \"{path}\" For Binary As #1\nPut #1, 1, {operand}\nClose #1");
            File.ReadAllBytes(path).Should().Equal(Convert.FromHexString(hex));
        }
        finally
        {
#if DEBUG
            HexIDE.IDE.Vb6TextEncoding.SimulateHostCodePage(codePage);
#endif
        }
    }

    [Theory]
    [InlineData("abcdefghi", false, "abc", "def", "ghi", false)]
    [InlineData("abcdefghi", true, "abc", "def", null, null)]
    [InlineData("abcd", false, "abc", "d\0\0", "\0\0\0", true)]
    public async Task Binary_Get_reads_existing_elements_and_zero_fills_past_EOF(
        string text, bool dynamic, string first, string second, string? third, bool? eof)
    {
        var path = PathOf("get.dat");
        File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes(text));
        var declaration = dynamic ? "Dim a() As String * 3\nReDim a(1)" : "Dim a(2) As String * 3";
        var old = text == "abcd" ? "a(0) = \"xxx\"\na(1) = \"yyy\"\na(2) = \"zzz\"" : "";
        var print = "Debug.Print a(0)\nDebug.Print a(1)" + (third is null ? "" : "\nDebug.Print a(2)")
            + (eof is null ? "" : "\nDebug.Print EOF(1)");
        await Execute($"{declaration}\n{old}\nOpen \"{path}\" For Binary As #1\nGet #1, 1, a\n{print}\nClose #1");
        var expected = new List<Vb6Value> { new(first), new(second) };
        if (third is not null) expected.Add(new Vb6Value(third));
        if (eof is { } atEnd) expected.Add(new Vb6Value(atEnd));
        AssertDebugLog(expected);
    }

    [Theory]
    [InlineData(3, false, "ab ", "cd ")]
    [InlineData(3, true, "ab ", "cd ")]
    [InlineData(2, false, "ab", " c")]
    public async Task Random_Get_takes_descriptor_bounds_and_targets_own_width(int width, bool allocated, string first, string second)
    {
        var path = PathOf("random.dat");
        var allocate = allocated ? "ReDim a(4)" : "";
        await Execute($"Dim source() As String * 3\nReDim source(1)\nsource(0) = \"ab\"\nsource(1) = \"cd\"\nDim a() As String * {width}\n{allocate}\nOpen \"{path}\" For Random As #1 Len = 32\nPut #1, 1, source\nGet #1, 1, a\nDebug.Print LBound(a)\nDebug.Print UBound(a)\nDebug.Print a(0)\nDebug.Print a(1)\nClose #1");
        File.ReadAllBytes(path).Should().Equal(Convert.FromHexString("01000200000000000000616220636420"));
        AssertDebugLog([new Vb6Value(0L), new Vb6Value(1L), new Vb6Value(first), new Vb6Value(second)]);
        var array = (VBArray)context.State[rootEnv.variableToLocation["a"]].Value!;
        array.FixedStringWidth.Should().Be(width);
        array.IsDynamic.Should().BeTrue();
    }

    [Fact]
    public async Task Binary_Get_scalar_zero_fill_regression()
    {
        var path = PathOf("scalar.dat");
        File.WriteAllBytes(path, [0x61, 0x62]);
        await Execute($"Dim s As String * 5\ns = \"xxxxx\"\nOpen \"{path}\" For Binary As #1\nGet #1, 1, s\nDebug.Print s\nClose #1");
        AssertDebugLog([new Vb6Value("ab\0\0\0")]);
    }

    [Fact]
    public async Task Type_with_fixed_String_array_field_writes_measured_bytes()
    {
        // #179's measured row, repeated in hexide-io/HexIDE#4925.
        var path = PathOf("type.dat");
        await Execute($"Type T\ni As Integer\na(1) As String * 3\nEnd Type\nDim u As T\nu.i = 1\nu.a(0) = \"ab\"\nOpen \"{path}\" For Binary As #1\nPut #1, 1, u\nClose #1");
        File.ReadAllBytes(path).Should().Equal(Convert.FromHexString("0100616220000000"));
    }

    [Fact]
    public async Task Random_Get_into_fixed_array_stays_explicitly_refused()
    {
        var path = PathOf("fixed-random.dat");
        var run = () => Execute($"Dim a(2) As String * 3\nOpen \"{path}\" For Random As #1 Len = 32\nGet #1, 1, a");
        (await run.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Contain("#179");
    }

#if DEBUG
    [Theory]
    [InlineData("Get")]
    [InlineData("Put")]
    public async Task Double_byte_array_records_stay_explicitly_refused(string statement)
    {
        var path = PathOf("double-byte.dat");
        var codePage = HexIDE.IDE.Vb6TextEncoding.Ansi.CodePage;
        HexIDE.IDE.Vb6TextEncoding.SimulateHostCodePage(932);
        try
        {
            var run = () => Execute($"Dim a(2) As String * 3\nOpen \"{path}\" For Binary As #1\n{statement} #1, 1, a");
            (await run.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Contain("#179");
        }
        finally { HexIDE.IDE.Vb6TextEncoding.SimulateHostCodePage(codePage); }
    }
#endif
}
