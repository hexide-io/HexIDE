using System.Globalization;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured on vb6.exe SP6, native and p-code: https://github.com/hexide-io/HexIDE/issues/2518
// See also docs/vb6-fidelity-oracle/lset-between-two-user-defined-types.md and measuring PR #4241.
public class LsetUdtTests : BaseVBTestFixture
{
    public static IEnumerable<object[]> LayoutRows()
    {
        yield return ["a As Integer\nb As Long", "s.a = 1: s.b = 2", "01 00 00 00 02 00 00 00"];
        yield return ["a As Byte\nb As Double", "s.a = 7: s.b = 1.5", "07 00 00 00 00 00 00 00 00 00 F8 3F EE EE EE EE"];
        yield return ["a As Byte\nb As Integer", "s.a = 7: s.b = 258", "07 00 02 01 EE EE EE EE"];
        yield return ["a As Integer\nb As Long\nc As Integer", "s.a = 1: s.b = 2: s.c = 3", "01 00 00 00 02 00 00 00 03 00 00 00"];
        yield return ["x As Inner\ny As Integer", "s.x.a = 1: s.x.b = 2: s.y = 3", "01 00 00 00 02 00 00 00 03 00 00 00"];
        // A nested type is a record of its own: aligned, its size rounded up, and the next field after that size
        // (measured in review, native and p-code: LenB 12 and 6, where flattening its fields would give 8 and 4).
        yield return ["x As InLB\ny As Byte", "s.x.L = &H4030201: s.x.B = 5: s.y = 6",
            "01 02 03 04 05 00 00 00 06 00 00 00 EE EE EE EE"];
        yield return ["x As InIB\ny As Byte", "s.x.N = &H201: s.x.B = 3: s.y = 4",
            "01 02 03 00 04 00 EE EE EE EE EE EE EE EE EE EE"];
    }

    private const string Inner = "Type Inner\na As Integer\nb As Long\nEnd Type\n"
        + "Type InLB\nL As Long\nB As Byte\nEnd Type\nType InIB\nN As Integer\nB As Byte\nEnd Type\n";

    [Theory]
    [MemberData(nameof(LayoutRows))]
    public async Task Alignment_matches_measured_bytes(string fields, string setup, string hex)
    {
        var bytes = hex.Split(' ').Select(h => Convert.ToByte(h, 16)).ToArray();
        await Run(Inner + $"Type T\n{fields}\nEnd Type\nType Bytes\nb(0 To {bytes.Length - 1}) As Byte\nEnd Type\n" +
            $"Dim s As T, d As Bytes\nFor i = 0 To {bytes.Length - 1}\nd.b(i) = 238\nNext\n{setup}\nLSet d = s\n" +
            $"For i = 0 To {bytes.Length - 1}\nDebug.Print d.b(i)\nNext\n");
        AssertDebugLog(bytes.Select(b => new Vb6Value(b)).ToList());
    }

    public static IEnumerable<object[]> EncodingRows()
    {
        yield return ["Double", "1.5", "00 00 00 00 00 00 F8 3F"];
        yield return ["Currency", "1.5", "98 3A 00 00 00 00 00 00"];
        yield return ["Date", "#1/2/2000 12:00:00 PM#", "00 00 00 00 F0 D5 E1 40"];
        yield return ["Single", "1.5", "00 00 C0 3F"];
        yield return ["Boolean", "True", "FF FF"];
        yield return ["Boolean", "False", "00 00"];
        yield return ["String * 4", "\"ABCD\"", "41 00 42 00 43 00 44 00"];
    }

    [Theory]
    [MemberData(nameof(EncodingRows))]
    public async Task Encoding_matches_measured_bytes(string type, string initial, string hex)
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo("en-GB"));
        var bytes = hex.Split(' ').Select(h => Convert.ToByte(h, 16)).ToArray();
        await Run($"Type T\nx As {type}\nEnd Type\nType Bytes\nb(0 To {bytes.Length - 1}) As Byte\nEnd Type\n" +
            $"Dim s As T, d As Bytes\ns.x = {initial}\nLSet d = s\nFor i = 0 To {bytes.Length - 1}\nDebug.Print d.b(i)\nNext\n");
        AssertDebugLog(bytes.Select(b => new Vb6Value(b)).ToList());
    }

    [Theory]
    [MemberData(nameof(EncodingRows))]
    public async Task Decoding_matches_measured_value(string type, string initial, string hex)
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo("en-GB"));
        var bytes = hex.Split(' ').Select(h => Convert.ToByte(h, 16)).ToArray();
        await Run($"Type T\nx As {type}\nEnd Type\nType Bytes\nb(0 To {bytes.Length - 1}) As Byte\nEnd Type\n" +
            "Dim s As Bytes, d As T\n" + string.Join("\n", bytes.Select((b, i) => $"s.b({i}) = {b}")) +
            $"\nLSet d = s\nDebug.Print d.x = {initial}\n");
        AssertDebugLog([new Vb6Value(true)]);
    }

    public static IEnumerable<object[]> CopyRows()
    {
        yield return ["a As Integer\nb As Integer", "s.a = 1: s.b = 2", "x As Long", "", "d.x", new double[] { 131073d }];
        yield return ["x As Long", "s.x = &H30004", "a As Integer\nb As Integer", "", "d.a;d.b", new double[] { 4, 3 }];
        yield return ["x As Long", "s.x = 5", "a As Long\nb As Long\nc As Long", "d.a = 7: d.b = 8: d.c = 9", "d.a;d.b;d.c", new double[] { 5, 8, 9 }];
        yield return ["a As Long\nb As Long\nc As Long", "s.a = 7: s.b = 8: s.c = 9", "x As Long", "", "d.x", new double[] { 7d }];
        yield return ["a As Integer\nb As Long\nc As Integer", "s.a = 1: s.b = 2: s.c = 3", "a As Integer\nb As Long", "", "d.a;d.b", new double[] { 1, 2 }];
        yield return ["b(0 To 1) As Byte", "s.b(0) = 1: s.b(1) = 2", "x As Double", "d.x = 1.5", "d.x", new double[] { BitConverter.Int64BitsToDouble(0x3ff8000000000201) }];
        yield return ["b(0 To 7) As Byte", "For i = 0 To 7\ns.b(i) = i + 1\nNext", "a As Integer\nb As Long", "", "d.a;d.b", new double[] { 0x201, 0x8070605 }];
        yield return ["b(0 To 11) As Byte", "For i = 0 To 11\ns.b(i) = i + 1\nNext", "x As Inner\ny As Integer", "", "d.x.a;d.x.b;d.y", new double[] { 0x201, 0x8070605, 0xA09 }];
        yield return ["a As Integer\nb As Long\nc As Integer", "s.a = 1: s.b = 2: s.c = 3", "x As Inner\ny As Integer", "", "d.x.a;d.x.b;d.y", new double[] { 1, 2, 3 }];
        yield return ["x As Inner\ny As Integer", "s.x.a = 1: s.x.b = 2: s.y = 3", "a As Integer\nb As Long\nc As Integer", "", "d.a;d.b;d.c", new double[] { 1, 2, 3 }];
        yield return ["x As String * 4", "s.x = \"ABCD\"", "x As Long", "", "d.x", new double[] { 4325441d }];
    }

    [Theory]
    [MemberData(nameof(CopyRows))]
    public async Task Overlay_matches_measured_value(string source, string setup, string target, string before, string read, double[] expected)
    {
        await Run(Inner + $"Type S\n{source}\nEnd Type\nType T\n{target}\nEnd Type\n" +
            $"Dim s As S, d As T\n{setup}\n{before}\nLSet d = s\n" +
            string.Join("\n", read.Split(';').Select(field => $"Debug.Print CDbl({field})")) + "\n");
        // The two-byte Double overlay differs by less than the fixture's usual tolerance.
        debug.Select(value => value.Value).Should().Equal(expected.Cast<object>());
    }

    [Theory]
    [InlineData("x As Long", "s.x = &H44434241", "ChrW(&H4241) & ChrW(&H4443) & \"CD\"")]
    [InlineData("a As Integer\nb As String * 3", "s.a = 65: s.b = \"xyz\"", "\"Axyz\"")]
    public async Task Fixed_string_overlay_matches_measured_characters(string fields, string setup, string expected)
    {
        await Run($"Type S\n{fields}\nEnd Type\nType T\nx As String * 4\nEnd Type\n" +
            $"Dim s As S, d As T\nd.x = \"ABCD\"\n{setup}\nLSet d = s\nDebug.Print d.x = {expected}\n");
        AssertDebugLog([new Vb6Value(true)]);
    }

    [Fact]
    public async Task Noncanonical_boolean_is_refused()
    {
        Func<Task> run = () => Run("Type S\nx As Integer\nEnd Type\nType T\nx As Boolean\nEnd Type\nDim s As S, d As T\ns.x = 1\nLSet d = s\n");
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*noncanonical Boolean*#2518*");
    }

    [Fact]
    public async Task Copied_padding_remains_part_of_the_layout()
    {
        // #2518: the bytes 1..8 decode to Integer &H201 and Long &H8070605, skipping padding.
        // The measured byte-copy rule also copies that padding when this record is a source again.
        await Run("""
            Type Bytes
                b(0 To 7) As Byte
            End Type
            Type T
                a As Integer
                b As Long
            End Type
            Dim source As Bytes, target As T, back As Bytes
            For i = 0 To 7
                source.b(i) = i + 1
            Next
            LSet target = source
            LSet back = target
            For i = 0 To 7
                Debug.Print back.b(i)
            Next

            """);
        AssertDebugLog(Enumerable.Range(1, 8).Select(value => new Vb6Value((byte)value)).ToList());
    }

    [Fact]
    public async Task Fixed_array_is_inline_in_both_directions()
    {
        await Run("""
            Type T
                a(0 To 1) As Long
            End Type
            Type Bytes
                b(0 To 7) As Byte
            End Type
            Dim s As T, d As Bytes, back As T
            s.a(0) = 1: s.a(1) = &H1020304
            LSet d = s
            For i = 0 To 7
                Debug.Print d.b(i)
            Next
            d.b(4) = 2: d.b(5) = 0: d.b(6) = 0: d.b(7) = 0
            LSet back = d
            Debug.Print back.a(0)
            Debug.Print back.a(1)

            """);
        AssertDebugLog([new Vb6Value((byte)1), new Vb6Value((byte)0), new Vb6Value((byte)0), new Vb6Value((byte)0),
            new Vb6Value((byte)4), new Vb6Value((byte)3), new Vb6Value((byte)2), new Vb6Value((byte)1), new Vb6Value(1L), new Vb6Value(2L)]);
    }

    [Theory]
    [InlineData("s As String", false)]
    [InlineData("s As String", true)]
    [InlineData("v As Variant", false)]
    [InlineData("v As Variant", true)]
    [InlineData("a() As Long", false)]
    [InlineData("a() As Long", true)]
    [InlineData("o As Object", false)]
    [InlineData("o As Object", true)]
    public async Task Pointer_field_on_either_side_raises_13(string field, bool target)
    {
        await Run($"Type Pointer\n{field}\nEnd Type\nType Plain\nx As Long\nEnd Type\n" +
            $"Dim p As Pointer, n As Plain\nOn Error Resume Next\nLSet {(target ? "p = n" : "n = p")}\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(13L)]);
    }

}
