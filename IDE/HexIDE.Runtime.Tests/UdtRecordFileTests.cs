using System.IO;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6 /make, native and p-code: hexide-io/HexIDE#4925's 2026-11-19 measurements, cases 1–3.
public class UdtRecordFileTests : BaseVBTestFixture, IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hexide-udt-records-").FullName;
    public void Dispose() => Directory.Delete(dir, true);

    private const string Types = """
        Private Type T1
        i As Integer
        s As String * 2
        End Type
        Private Type T2
        i As Integer
        s As String
        End Type
        Private Type T3
        v As Variant
        End Type
        Private Type U1
        i As Integer
        a(1) As Integer
        End Type
        Private Type U2
        i As Integer
        a() As Integer
        End Type
        Private Type U3
        a(1) As String
        End Type
        Private Type U4
        a(1) As Variant
        End Type
        Private Type U5
        a(1) As T1
        End Type
        Private Type U6
        a(1 To 2, 0 To 1) As Byte
        End Type
        Private Type U7
        a() As String
        End Type
        Private Type U8
        a(1) As Long
        s As String
        End Type
        """;

    private async Task Execute(string body)
    {
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, Types + "\n" + body);
        try { await vb.Execute(); }
        finally { vb.BuiltIns.Files.CloseAll(); }
    }

    public static IEnumerable<object[]> Arrays()
    {
        foreach (var mode in new[] { "Binary", "Random" })
        foreach (var dynamic in new[] { false, true })
        foreach (var type in new[] { "T1", "T2", "T3" })
        {
            var setup = type == "T3" ? "a(0).v = CInt(5): a(1).v = \"x\""
                : "a(0).i = 1: a(0).s = \"ab\": a(1).i = 2" + (type == "T1" ? ": a(1).s = \"cd\"" : "");
            var hex = type == "T1" ? "0100616202006364" : type == "T2" ? "01000200616202000000" : "020005000800010078";
            var read = type == "T3" ? "TypeName(b(0).v) & b(0).v & TypeName(b(1).v) & b(1).v"
                : "b(0).i & b(0).s & b(1).i & b(1).s";
            yield return [mode, dynamic, type, setup, (mode == "Random" && dynamic ? "01000200000000000000" : "") + hex,
                read, type == "T1" ? "1ab2cd" : type == "T2" ? "1ab2" : "Integer5Stringx"];
        }
    }

    [Theory]
    [MemberData(nameof(Arrays))]
    public async Task Arrays_write_bytes_and_read_elements(string mode, bool dynamic, string type, string setup, string hex, string read, string expected)
    {
        var path = Path.Combine(dir, "array.dat");
        var declarations = dynamic ? $"Dim a() As {type}, b() As {type}\nReDim a(1)" : $"Dim a(1) As {type}, b(1) As {type}";
        await Execute($"{declarations}\n{setup}\nOpen \"{path}\" For {mode} As #1 Len = 32\nPut #1, 1, a\n"
            + (mode == "Binary" && dynamic ? "ReDim b(1)\n" : "")
            + $"Get #1, 1, b\nDebug.Print {read}\nClose #1");
        File.ReadAllBytes(path).Should().Equal(Convert.FromHexString(hex));
        AssertDebugLog([expected]);
    }

    public static IEnumerable<object[]> Fields()
    {
        foreach (var mode in new[] { "Binary", "Random" })
        {
            yield return [mode, "U1", "u.i = 1: u.a(0) = 2: u.a(1) = 3", "010002000300", "w.i & w.a(0) & w.a(1)", "123"];
            yield return [mode, "U2", "u.i = 1: ReDim u.a(1): u.a(0) = 2: u.a(1) = 3", "01000100020000000000000002000300", "w.i & LBound(w.a) & UBound(w.a) & w.a(0) & w.a(1)", "10123"];
            yield return [mode, "U2", "u.i = 1", "01000000", "w.i & \" \" & Err.Number", "1 0"];
            yield return [mode, "U3", "u.a(0) = \"ab\"", "020061620000", "w.a(0) & w.a(1)", "ab"];
            yield return [mode, "U4", "u.a(0) = CInt(5): u.a(1) = \"x\"", "020005000800010078", "TypeName(w.a(0)) & w.a(0) & TypeName(w.a(1)) & w.a(1)", "Integer5Stringx"];
            yield return [mode, "U5", "u.a(0).i = 1: u.a(0).s = \"ab\": u.a(1).i = 2: u.a(1).s = \"cd\"", "0100616202006364", "w.a(0).i & w.a(0).s & w.a(1).i & w.a(1).s", "1ab2cd"];
            yield return [mode, "U6", "u.a(1,0) = 1: u.a(2,0) = 2: u.a(1,1) = 3: u.a(2,1) = 4", "01020304", "w.a(1,0) & w.a(2,0) & w.a(1,1) & w.a(2,1)", "1234"];
            yield return [mode, "U7", "ReDim u.a(1): u.a(0) = \"ab\": u.a(1) = \"c\"", "0100020000000000000002006162010063", "LBound(w.a) & UBound(w.a) & w.a(0) & w.a(1)", "01abc"];
            yield return [mode, "U7", "", "0000", "CStr(Err.Number)", "0"];
            yield return [mode, "U8", "u.a(0) = 1: u.a(1) = 2: u.s = \"z\"", "010000000200000001007A", "w.a(0) & w.a(1) & w.s", "12z"];
        }
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public async Task Array_fields_write_in_place_and_round_trip(string mode, string type, string setup, string hex, string read, string expected)
    {
        var path = Path.Combine(dir, "field.dat");
        await Execute($"Dim u As {type}, w As {type}\n{setup}\nOpen \"{path}\" For {mode} As #1 Len = 32\nPut #1, 1, u\nGet #1, 1, w\nDebug.Print {read}\nClose #1");
        File.ReadAllBytes(path).Should().Equal(Convert.FromHexString(hex));
        AssertDebugLog([expected]);
    }

    [Theory]
    [InlineData("Dim a(1) As T1", "Random", 4)]
    [InlineData("Dim a() As T1\nReDim a(1)", "Random", 8)]
    [InlineData("Dim a() As T1\nReDim a(0 To 1, 1 To 2)", "Random", 32)]
    [InlineData("Dim a As U1", "Random", 4)]
    [InlineData("Dim a As U2\nReDim a.a(1)", "Random", 8)]
    public async Task Short_records_raise_59_without_writing(string declaration, string mode, int length)
    {
        var path = Path.Combine(dir, "short.dat");
        await Execute($"{declaration}\nOpen \"{path}\" For {mode} As #1 Len = {length}\nOn Error Resume Next\nPut #1, 1, a\nDebug.Print Err.Number\nClose #1");
        AssertDebugLog([new Vb6Value(59L)]);
        File.ReadAllBytes(path).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Array_held_in_a_Variant_field_keeps_the_unmeasured_record_refusal(bool read)
    {
        var path = Path.Combine(dir, "variant-array.dat");
        var run = () => Execute($"Dim u As T3\nu.v = Array(1, 2)\nOpen \"{path}\" For Binary As #1\n{(read ? "Get" : "Put")} #1, 1, u");
        (await run.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().EndWith("(#4925)");
    }

    // No locked-target measurement accompanies #4925: keep these edges refused rather than inventing a result.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Get_into_a_locked_dynamic_record_array_or_field_keeps_refusal(bool field)
    {
        var path = Path.Combine(dir, "locked.dat");
        var declaration = field ? "Dim u As U2\nReDim u.a(1)" : "Dim u() As T1\nReDim u(1)";
        var body = field ? "Dim n As Variant\nFor Each n In u.a\nGet #1, 1, u\nNext"
            : "ReadLocked u(0)\nSub ReadLocked(ByRef t As T1)\nGet #1, 1, u\nEnd Sub";
        var run = () => Execute($"{declaration}\nOpen \"{path}\" For Random As #1 Len = 32\nPut #1, 1, u\n{body}");
        (await run.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().EndWith("(#4925)");
    }

    [Theory]
    [InlineData("Binary", "")]
    [InlineData("Random", "0000")]
    public async Task Unallocated_record_array_writes_no_elements(string mode, string hex)
    {
        var path = Path.Combine(dir, "empty.dat");
        await Execute($"Dim a() As T1\nOpen \"{path}\" For {mode} As #1 Len = 32\nPut #1, 1, a\nClose #1");
        File.ReadAllBytes(path).Should().Equal(Convert.FromHexString(hex));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(32)]
    public async Task Static_record_array_fits_exactly_or_in_a_larger_record(int length)
    {
        var path = Path.Combine(dir, "fit.dat");
        await Execute($"Dim a(1) As T1\na(0).i = 1: a(0).s = \"ab\": a(1).i = 2: a(1).s = \"cd\"\nOpen \"{path}\" For Random As #1 Len = {length}\nPut #1, 1, a\nClose #1");
        File.ReadAllBytes(path).Should().Equal(Convert.FromHexString("0100616202006364"));
    }

    [Fact]
    public async Task Multidimensional_record_array_is_first_index_fastest()
    {
        var path = Path.Combine(dir, "matrix.dat");
        await Execute($"Dim a(0 To 1, 1 To 2) As T1\na(0,1).i = 1: a(1,1).i = 2: a(0,2).i = 3: a(1,2).i = 4\nOpen \"{path}\" For Binary As #1\nPut #1, 1, a\nClose #1");
        File.ReadAllBytes(path).Should().Equal(Convert.FromHexString("01000000020000000300000004000000"));
    }

    [Theory]
    [InlineData("U1", "a(0).i = 1: a(0).a(1) = 2: a(1).i = 3: a(1).a(0) = 4", "010000000200030004000000")]
    [InlineData("U2", "a(0).i = 1: ReDim a(0).a(0): a(0).a(0) = 2: a(1).i = 3", "010001000100000000000000020003000000")]
    public async Task Record_arrays_can_contain_array_fields(string type, string setup, string hex)
    {
        var path = Path.Combine(dir, "nested.dat");
        await Execute($"Dim a(1) As {type}\n{setup}\nOpen \"{path}\" For Binary As #1\nPut #1, 1, a\nClose #1");
        File.ReadAllBytes(path).Should().Equal(Convert.FromHexString(hex));
    }

    [Theory]
    [InlineData("Dim a(1) As T1", "010061", "1a\0" + "0\0\0")]
    [InlineData("Dim a() As T1\nReDim a(2)", "0100616202006364", "1ab2cd")]
    public async Task Binary_Get_keeps_bounds_and_zero_fills_past_end(string declaration, string hex, string expected)
    {
        var path = Path.Combine(dir, "truncated.dat");
        File.WriteAllBytes(path, Convert.FromHexString(hex));
        await Execute($"{declaration}\na(0).i = 9: a(0).s = \"zz\": a(1).i = 9: a(1).s = \"zz\"\nOpen \"{path}\" For Binary As #1\nGet #1, 1, a\nDebug.Print a(0).i & a(0).s & a(1).i & a(1).s\nDebug.Print EOF(1)\nClose #1");
        AssertDebugLog([expected, new Vb6Value(true)]);
        context.TryGetVariable(rootEnv, "a", out var held).Should().BeTrue();
        var array = (VBArray)held.Value!;
        array.UpperBound(1).Should().Be(declaration.Contains("ReDim") ? 2 : 1);
        if (array.UpperBound(1) == 2)
        {
            var third = (VbUdt)array.GetValue([2]).Value!;
            third.TryGet("i", out var i).Should().BeTrue();
            i.Value.Should().Be(0);
            third.TryGet("s", out var s).Should().BeTrue();
            s.Value.Should().Be("\0\0");
        }
    }

    [Fact]
    public async Task Binary_Get_into_unallocated_array_reads_nothing()
    {
        var path = Path.Combine(dir, "unallocated.dat");
        File.WriteAllBytes(path, Convert.FromHexString("0100616202006364"));
        await Execute($"Dim a() As T1\nOpen \"{path}\" For Binary As #1\nGet #1, 1, a\nDebug.Print EOF(1)\nOn Error Resume Next\nDebug.Print LBound(a)\nDebug.Print Err.Number\nClose #1");
        AssertDebugLog([new Vb6Value(false), new Vb6Value(9L)]);
    }

    [Fact]
    public async Task Static_target_reads_a_dynamic_descriptor_as_data()
    {
        var path = Path.Combine(dir, "descriptor.dat");
        File.WriteAllBytes(path, Convert.FromHexString("010002000000000000000100616202006364"));
        await Execute($"Dim a(1) As T1\nOpen \"{path}\" For Random As #1 Len = 32\nGet #1, 1, a\nDebug.Print a(0).i & a(0).s & a(1).i & a(1).s\nClose #1");
        AssertDebugLog(["1\u0002\00\0\0"]);
    }

    [Fact]
    public async Task Dynamic_target_takes_descriptor_bounds_even_when_record_is_too_short()
    {
        var path = Path.Combine(dir, "bad-descriptor.dat");
        File.WriteAllBytes(path, Convert.FromHexString("0100616202006364"));
        await Execute($"Dim a() As T1\nOpen \"{path}\" For Random As #1 Len = 32\nOn Error Resume Next\nGet #1, 1, a\nDebug.Print Err.Number\nDebug.Print LBound(a) & \" \" & UBound(a)\nClose #1");
        AssertDebugLog([new Vb6Value(59L), "25699 181955"]);
    }

    [Theory]
    [InlineData("Binary")]
    [InlineData("Random")]
    public async Task Dynamic_field_descriptor_replaces_existing_bounds_and_can_free_the_field(string mode)
    {
        var path = Path.Combine(dir, "replace.dat");
        await Execute($"Dim u As U2, w As U2\nu.i = 1: ReDim u.a(1): u.a(0) = 2: u.a(1) = 3\nReDim w.a(3): w.a(0) = 7\nOpen \"{path}\" For {mode} As #1 Len = 32\nPut #1, 1, u\nGet #1, 1, w\nDebug.Print LBound(w.a) & UBound(w.a) & w.a(0) & w.a(1)\nErase u.a\nPut #1, 1, u\nReDim w.a(2)\nGet #1, 1, w\nOn Error Resume Next\nDebug.Print LBound(w.a)\nDebug.Print Err.Number\nClose #1");
        AssertDebugLog(["0123", new Vb6Value(9L)]);
    }

    [Fact]
    public async Task Binary_Get_array_field_zero_fills_past_end()
    {
        var path = Path.Combine(dir, "field-short.dat");
        File.WriteAllBytes(path, Convert.FromHexString("010002"));
        await Execute($"Dim u As U1\nu.i = 9: u.a(0) = 9: u.a(1) = 9\nOpen \"{path}\" For Binary As #1\nGet #1, 1, u\nDebug.Print u.i & u.a(0) & u.a(1)\nClose #1");
        AssertDebugLog(["120"]);
    }
}
