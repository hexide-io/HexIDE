using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6, native and p-code: https://github.com/hexide-io/HexIDE/issues/4680 (oracle PR #4686).
public class PropertyBagTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("5", "Integer", "5")]
    [InlineData("100000", "Long", "100000")]
    [InlineData("\"hi\"", "String", "hi")]
    [InlineData("1.5", "Double", "1.5")]
    [InlineData("CSng(2.5)", "Single", "2.5")]
    [InlineData("CCur(3.25)", "Currency", "3.25")]
    [InlineData("True", "Boolean", "True")]
    [InlineData("CByte(7)", "Byte", "7")]
    [InlineData("CDec(1.1)", "Decimal", "1.1")]
    [InlineData("Empty", "Empty", "")]
    public async Task Scalars_keep_their_type(string expression, string type, string value)
    {
        await Run($"""
            Dim p As PropertyBag
            Set p = New PropertyBag
            p.WriteProperty "A", {expression}
            Debug.Print TypeName(p.ReadProperty("a"))
            Debug.Print CStr(p.ReadProperty("A"))
            """);
        AssertDebugLog([new Vb6Value(type), new Vb6Value(value)]);
    }

    [Fact]
    public async Task Null_Nothing_and_Date_keep_their_type()
    {
        await Run("""
            Dim p As PropertyBag, v As Object
            Set p = New PropertyBag
            Debug.Print TypeName(p)
            p.WriteProperty "nul", Null
            p.WriteProperty "none", Nothing
            p.WriteProperty "dt", #1/2/2003 4:05:06 AM#
            Debug.Print IsNull(p.ReadProperty("nul"))
            Set v = p.ReadProperty("none")
            Debug.Print v Is Nothing
            Debug.Print TypeName(p.ReadProperty("dt"))
            Debug.Print p.ReadProperty("dt") = #1/2/2003 4:05:06 AM#
            """);
        AssertDebugLog([new Vb6Value("PropertyBag"), new Vb6Value(true), new Vb6Value(true), new Vb6Value("Date"), new Vb6Value(true)]);
    }

    // vb6.exe SP6, native and p-code, #4680 (oracle PR #4686): the descriptions, two spaces after "parameter." included.
    [Theory]
    [InlineData("v = p.ReadProperty(\"X\")", "Data value named 'X' not found")]
    [InlineData("p.WriteProperty \"arr\", Array(1, 2)", "Illegal parameter.  Can't write arrays")]
    [InlineData("p.WriteProperty \"coll\", c", "Illegal parameter.  Can't write object because it does not support persistence.")]
    public async Task Measured_error_descriptions(string statement, string description)
    {
        await Run($"""
            Dim p As PropertyBag, c As Collection, v As Variant
            Set p = New PropertyBag
            Set c = New Collection
            On Error Resume Next
            {statement}
            Debug.Print Err.Description
            """);
        AssertDebugLog([new Vb6Value(description)]);
    }

    [Theory]
    [InlineData("p.WriteProperty \"\", 1", 5)]
    [InlineData("v = p.ReadProperty(\"\")", 5)]
    [InlineData("v = p.ReadProperty(\"X\")", 327)]
    [InlineData("p.WriteProperty \"arr\", Array(1, 2)", 328)]
    [InlineData("p.WriteProperty \"coll\", c", 330)]
    [InlineData("p.Contents = \"bad\"", 13)]
    [InlineData("p.Contents = 12", 13)]
    [InlineData("p.Contents = b", 5)]
    public async Task Measured_errors(string statement, int number)
    {
        await Run($"""
            Dim p As PropertyBag, c As Collection, v As Variant, b(0 To 2) As Byte
            Set p = New PropertyBag
            Set c = New Collection
            p.WriteProperty "keep", 9
            On Error Resume Next
            {statement}
            Debug.Print Err.Number
            Err.Clear
            Debug.Print p.ReadProperty("keep")
            """);
        AssertDebugLog([new Vb6Value((long)number), new Vb6Value(9)]);
    }

    [Fact]
    public async Task Defaults_duplicates_and_case()
    {
        await Run("""
            Dim p As PropertyBag
            Set p = New PropertyBag
            p.WriteProperty "Def", 7, 7
            p.WriteProperty "Def2", 8, 7
            p.WriteProperty "Dup", 1
            p.WriteProperty "DUP", 2
            Debug.Print p.ReadProperty("Def", 42)
            Debug.Print p.ReadProperty("def2", 42)
            Debug.Print p.ReadProperty("dup", 42)
            """);
        AssertDebugLog([new Vb6Value(42), new Vb6Value(8), new Vb6Value(2)]);
    }

    private const string HexHelper = """
        Function HexBytes(ByVal b As Variant) As String
            Dim i As Long, s As String
            For i = LBound(b) To UBound(b)
                s = s & Right$("0" & Hex$(b(i)), 2)
            Next
            HexBytes = s
        End Function
        """;

    [Theory]
    [InlineData("p.WriteProperty \"A\", 1", "93B200001800000002000100610000000000000061000100")]
    [InlineData("p.WriteProperty \"A\", 1: p.WriteProperty \"B\", \"x\"", "93B200002C000000020001006100000010000000610001000800010062000000F0FFFFFF6200010000007800")]
    public async Task Exact_Contents_and_round_trip(string writes, string hex)
    {
        await Run($"""
            {HexHelper}
                Dim p As PropertyBag, q As PropertyBag
                Set p = New PropertyBag: Set q = New PropertyBag
                {writes}
                Debug.Print HexBytes(p.Contents)
                q.Contents = p.Contents
                Debug.Print HexBytes(q.Contents)
                Debug.Print q.ReadProperty("a")
            """);
        AssertDebugLog([new Vb6Value(hex), new Vb6Value(hex), new Vb6Value(1)]);
    }

    [Fact]
    public async Task Empty_Contents_is_an_empty_Byte_array()
    {
        await Run("""
            Dim p As New PropertyBag
            Debug.Print TypeName(p.Contents)
            Debug.Print CStr(LBound(p.Contents))
            Debug.Print CStr(UBound(p.Contents))
            """);
        AssertDebugLog([new Vb6Value("Byte()"), new Vb6Value("0"), new Vb6Value("-1")]);
    }

    // Entry bytes copied from the 480-byte dump in #4680; only the link is rebased for a single-entry bag.
    public static TheoryData<string, string, string> FullDumpEntries => new()
    {
        { "i", "5", "02000100690000000000000069000500" },
        { "l", "100000", "030001006C000000000000006C00A08601000000" },
        { "s", "\"hi\"", "080001007300000000000000730002000000680069000000" },
        { "dbl", "1.5", "050003006D400C9000000000640062006C00000000000000F83F0000" },
        { "sng", "CSng(2.5)", "0400030066C00DCC0000000073006E006700000020400000" },
        { "cur", "CCur(3.25)", "0600030073A00E8C00000000630075007200F47E0000000000000000" },
        { "dt", "#1/2/2003 4:05:06 AM#", "0700020074800C00000000006400740026BF5872E55EE240" },
        { "bool", "True", "0B0004006DD10DBC0000000062006F006F006C00FFFF0000" },
        { "byte", "CByte(7)", "1100040064B10EE400000000620079007400650007000000" },
        { "dec", "CDec(1.1)", "0E00030062A00C90000000006400650063000E000100000000000B000000000000000000" },
        { "emp", "Empty", "0000030071A00D940000000065006D0070000000" },
        { "nul", "Null", "010003006DA00EB8000000006E0075006C000000" },
        { "bytes", "b", "11200500F29C2CD60000000062007900740065007300010003000000000000000102FF00" },
        { "none", "Nothing", "0900040064F70DBC000000006E006F006E00650000000000000000000000000000000000" },
        { "def2", "8", "0200040033F20C9400000000640065006600320008000000" },
        { "dup", "1", "0200030071A00E90000000006400750070000100" },
        { "dup", "2", "0200030071A00E90000000006400750070000200" },
    };

    [Theory]
    [MemberData(nameof(FullDumpEntries))]
    public async Task Every_supported_full_dump_entry_matches(string name, string expression, string entryHex)
    {
        var entry = Convert.FromHexString(entryHex);
        var expected = new byte[8 + entry.Length];
        Convert.FromHexString("93B20000").CopyTo(expected, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(4), expected.Length);
        entry.CopyTo(expected, 8);
        await Run($"""
            {HexHelper}
                Dim p As PropertyBag, q As PropertyBag, b(0 To 2) As Byte
                b(0) = 1: b(1) = 2: b(2) = 255
                Set p = New PropertyBag: Set q = New PropertyBag
                p.WriteProperty "{name.ToUpperInvariant()}", {expression}
                Debug.Print HexBytes(p.Contents)
                q.Contents = p.Contents
                Debug.Print HexBytes(q.Contents)
                Debug.Print TypeName(q.ReadProperty("{name}")) = TypeName(p.ReadProperty("{name}"))
            """);
        AssertDebugLog([new Vb6Value(Convert.ToHexString(expected)), new Vb6Value(Convert.ToHexString(expected)), new Vb6Value(true)]);
    }

    [Fact]
    public async Task Measured_error_descriptions_are_program_facing_English()
    {
        await Run("""
            Dim p As PropertyBag, v As Variant
            Set p = New PropertyBag
            On Error Resume Next
            v = p.ReadProperty("X")
            Debug.Print Err.Description
            Err.Clear
            p.WriteProperty "a", Array(1)
            Debug.Print Err.Description
            """);
        AssertDebugLog([new Vb6Value("Data value named 'X' not found"), new Vb6Value("Illegal parameter.  Can't write arrays")]);
    }

    [Fact]
    public async Task Byte_array_read_back_has_its_type_bounds_and_values()
    {
        await Run("""
            Dim p As PropertyBag, b(0 To 2) As Byte, v As Variant
            Set p = New PropertyBag
            b(0) = 1: b(1) = 2: b(2) = 255
            p.WriteProperty "Bytes", b
            v = p.ReadProperty("bytes")
            Debug.Print TypeName(v)
            Debug.Print CStr(LBound(v))
            Debug.Print CStr(UBound(v))
            Debug.Print v(0)
            Debug.Print v(1)
            Debug.Print v(2)
            """);
        AssertDebugLog([new Vb6Value("Byte()"), new Vb6Value("0"), new Vb6Value("2"), new Vb6Value((byte)1), new Vb6Value((byte)2), new Vb6Value((byte)255)]);
    }

    [Fact]
    public async Task Other_objects_are_explicitly_refused()
    {
        var act = async () => await RunClasses("""
            Dim p As PropertyBag, o As Class1
            Set p = New PropertyBag: Set o = New Class1
            p.WriteProperty "object", o
            """, ("Class1", "Public x As Integer"));
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*PropertyBag*objects*#4741*");
    }

    private static string InputBytes(byte[] bytes) =>
        $"Dim b(0 To {bytes.Length - 1}) As Byte\n" +
        string.Join("\n", bytes.Select((b, i) => $"b({i}) = {b}"));

    [Fact]
    public async Task Persistable_font_Contents_reads_the_untouched_font()
    {
        // The font entry at offset 332 in the full measured dump; rebased as a single-entry bag.
        var bytes = Convert.FromHexString("93B20000380000000900040075F30DBC0000000066006F006E0074000352E30B918FCE119DE300AA004BB851010100000000803801000000");
        await Run($"""
            Dim p As PropertyBag, g As Object
            Set p = New PropertyBag
            {InputBytes(bytes)}
            p.Contents = b
            Set g = p.ReadProperty("font")
            Debug.Print g.Name
            Debug.Print g.Size
            """);
        AssertDebugLog([new Vb6Value("MS Sans Serif"), Vb6Value.NewCurrency(8.25m)]);
    }

    // SP6 native/p-code: #4741's 2026-11-20 comment and
    // docs/vb6-fidelity-oracle/a-propertybag-at-run-time.md, "A StdFont in a bag".
    public static TheoryData<string, string, string, double, int, int, int> FontRows => new()
    {
        { "", "0101000000008038010000", "MS Sans Serif", 8.25, 400, 0, 0 },
        { "v = f.Name", "010000009001444201000D4D532053616E73205365726966", "MS Sans Serif", 8.25, 400, 0, 0 },
        { "v = f.Bold", "010000009001444201000D4D532053616E73205365726966", "MS Sans Serif", 8.25, 400, 0, 0 },
        { "v = f.Size", "010000009001444201000D4D532053616E73205365726966", "MS Sans Serif", 8.25, 400, 0, 0 },
        { "v = f.Charset", "010000009001444201000D4D532053616E73205365726966", "MS Sans Serif", 8.25, 400, 0, 0 },
        { "p.WriteProperty \"before\", f: v = f.Name: Set p = New PropertyBag", "010000009001444201000D4D532053616E73205365726966", "MS Sans Serif", 8.25, 400, 0, 0 },
        { "f.Name = \"Arial\"", "0101000000008038010005417269616C", "Arial", 8.25, 400, 0, 0 },
        { "f.Size = 10", "010100000000A086010000", "MS Sans Serif", 9.75, 400, 0, 0 },
        { "f.Bold = True", "01010000BC028038010000", "Arial", 8.25, 700, 0, 0 },
        { "f.Weight = 700", "01010000BC028038010000", "Arial", 8.25, 700, 0, 0 },
        { "f.Italic = True", "0101000200008038010000", "MS Sans Serif", 8.25, 400, 0, 2 },
        { "f.Charset = 2", "0102000000008038010000", "Wingdings", 8.25, 400, 2, 0 },
        { "f.Name = \"Arial\": v = f.Size", "0100000090014442010005417269616C", "Arial", 8.25, 400, 0, 0 },
        { "f.Name = \"Arial\": f.Size = 10: v = f.Name", "010000009001DC7C010005417269616C", "Arial", 9.75, 400, 0, 0 },
        { "f.Name = \"Arial\": f.Size = 10: f.Bold = True: v = f.Name", "01000000BC02DC7C010005417269616C", "Arial", 9.75, 700, 0, 0 },
        { "f.Name = \"Arial\": f.Size = 10: f.Italic = True: v = f.Name", "010000029001DC7C010005417269616C", "Arial", 9.75, 400, 0, 2 },
        { "f.Name = \"Arial\": f.Size = 10: f.Underline = True: v = f.Name", "010000049001DC7C010005417269616C", "Arial", 9.75, 400, 0, 4 },
        { "f.Name = \"Arial\": f.Size = 10: f.Strikethrough = True: v = f.Name", "010000089001DC7C010005417269616C", "Arial", 9.75, 400, 0, 8 },
        { "f.Name = \"Arial\": f.Size = 10: f.Weight = 600: v = f.Name", "01000000BC02DC7C010005417269616C", "Arial", 9.75, 700, 0, 0 },
        { "f.Name = \"Arial\": f.Size = 10: f.Charset = 2: v = f.Name", "010200009001DC7C01000957696E6764696E6773", "Wingdings", 9.75, 400, 2, 0 },
        { "f.Name = \"Courier New\": f.Size = 10: v = f.Name", "010000009001DC7C01000B436F7572696572204E6577", "Courier New", 9.75, 400, 0, 0 },
        { "f.Name = \"Tahoma\": f.Size = 10.5: f.Bold = True: f.Italic = True: v = f.Name", "01000002BC02289A0100065461686F6D61", "Tahoma", 10.5, 700, 0, 2 },
    };

    private static string FontBag(string stream)
    {
        var body = "09000100660000000000000066000352E30B918FCE119DE300AA004BB851" + stream;
        body = body.PadRight((body.Length + 7) / 8 * 8, '0');
        return "93B20000" + Convert.ToHexString(BitConverter.GetBytes(8 + body.Length / 2)) + body;
    }

    [Theory]
    [MemberData(nameof(FontRows))]
    public async Task Font_Contents_and_read_back_match_measured_rows(string setup, string stream,
        string name, double size, int weight, int charset, int flags)
    {
        await Run($"""
            {HexHelper}
            Dim p As New PropertyBag, q As New PropertyBag, f As Object, g As Object, v As Variant
            Set f = New StdFont
            {setup}
            p.WriteProperty "F", f
            Debug.Print HexBytes(p.Contents)
            Debug.Print HexBytes(p.Contents)
            q.Contents = p.Contents
            Debug.Print HexBytes(q.Contents)
            Set g = q.ReadProperty("F")
            Debug.Print g Is f
            Debug.Print TypeName(g)
            Debug.Print g.Name
            Debug.Print g.Size
            Debug.Print g.Weight
            Debug.Print g.Charset
            Debug.Print g.Bold
            Debug.Print g.Italic
            Debug.Print g.Underline
            Debug.Print g.Strikethrough
            v = q.ReadProperty("F")
            Debug.Print TypeName(v)
            Debug.Print v
            """);
        var hex = FontBag(stream);
        AssertDebugLog([new Vb6Value(hex), new Vb6Value(hex), new Vb6Value(hex), new Vb6Value(false),
            new Vb6Value("Font"), new Vb6Value(name), Vb6Value.NewCurrency((decimal)size),
            new Vb6Value(weight), new Vb6Value(charset), new Vb6Value(weight == 700),
            new Vb6Value((flags & 2) != 0), new Vb6Value((flags & 4) != 0), new Vb6Value((flags & 8) != 0),
            new Vb6Value("String"), new Vb6Value(name)]);
    }

    [Fact]
    public async Task Writing_an_untouched_font_twice_does_not_realize_it()
    {
        // The last line, the first bag unchanged after the font is read, was measured in #4741's review (R01).
        // #4741's 2026-11-20 comment: writing does not realize; reading Name afterwards does.
        await Run($"""
            {HexHelper}
            Dim p As New PropertyBag, q As New PropertyBag, r As New PropertyBag, f As Object, v As Variant
            Set f = New StdFont
            p.WriteProperty "F", f
            q.WriteProperty "F", f
            Debug.Print HexBytes(p.Contents)
            Debug.Print HexBytes(q.Contents)
            v = f.Name
            r.WriteProperty "F", f
            Debug.Print HexBytes(r.Contents)
            Debug.Print HexBytes(p.Contents)
            """);
        var untouched = new Vb6Value(FontBag("0101000000008038010000"));
        AssertDebugLog([untouched, untouched,
            new Vb6Value(FontBag("010000009001444201000D4D532053616E73205365726966")), untouched]);
    }

    [Theory]
    [InlineData("0101000100008038010000")] // unmeasured flag 01
    [InlineData("0101000000008038010001FF")] // non-ASCII font name
    [InlineData("010100000000803801000E4141414141414141414141414141")] // over 13 characters
    [InlineData("01010002BC028038010000")] // empty name, Bold and Italic together
    public async Task Unmeasured_font_streams_are_explicitly_refused(string stream)
    {
        // Synthetic policy guards, not additional VB6 measurements; #4741 explicitly excludes these.
        var act = async () => await Run($"""
            Dim p As New PropertyBag
            {InputBytes(Convert.FromHexString(FontBag(stream)))}
            p.Contents = b
            """);
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*#4741*");
    }

    // vb6.exe SP6, native and p-code agreeing, 2026-11-20 (#4741's review, R02-R06): a font read back from a bag
    // and written again unread writes the stream it was loaded from; read first, it writes its realized values.
    [Theory]
    [InlineData("", "", "0101000000008038010000")]
    [InlineData("f.Size = 10", "", "010100000000A086010000")]
    [InlineData("v = f.Name", "", "010000009001444201000D4D532053616E73205365726966")]
    [InlineData("f.Bold = True", "", "01010000BC028038010000")]
    [InlineData("f.Size = 10", "v = g.Name", "010000009001DC7C01000D4D532053616E73205365726966")]
    public async Task A_font_read_back_is_written_again_as_loaded_until_it_is_read(string setup, string readBack,
        string rewritten)
    {
        await Run($"""
            {HexHelper}
            Dim p As New PropertyBag, q As New PropertyBag, r As New PropertyBag, f As Object, g As Object, v As Variant
            Set f = New StdFont
            {setup}
            p.WriteProperty "F", f
            q.Contents = p.Contents
            Set g = q.ReadProperty("F")
            {readBack}
            r.WriteProperty "F", g
            Debug.Print HexBytes(r.Contents)
            """);
        AssertDebugLog([new Vb6Value(FontBag(rewritten))]);
    }

    [Fact]
    public async Task Unmeasured_font_request_is_explicitly_refused()
    {
        var act = async () => await Run("""
            Dim p As New PropertyBag, f As Object
            Set f = New StdFont
            f.Bold = True
            f.Italic = True
            p.WriteProperty "F", f
            """);
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*#4741*");
    }

    [Theory]
    [InlineData("000102")]
    [InlineData("93B2000018000000020001006100000000000000610001")]
    [InlineData("93B200001800000002000100610000000400000061000100")]
    public async Task Invalid_Contents_keeps_previous_values(string hex)
    {
        await Run($"""
            Dim p As PropertyBag
            Set p = New PropertyBag
            p.WriteProperty "A", 1
            {InputBytes(Convert.FromHexString(hex))}
            On Error Resume Next
            p.Contents = b
            Debug.Print Err.Number
            Debug.Print p.ReadProperty("a")
            """);
        AssertDebugLog([new Vb6Value(5L), new Vb6Value(1)]);
    }

    [Fact]
    public async Task Full_dump_without_font_retains_order_and_duplicate_entries()
    {
        var rows = FullDumpEntries.ToArray();
        var entries = rows.Select(row => Convert.FromHexString(row.Data.Item3)).ToArray();
        var expected = new byte[8 + entries.Sum(entry => entry.Length)];
        Convert.FromHexString("93B20000").CopyTo(expected, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(4), expected.Length);
        var offset = 8;
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(8), i == entries.Length - 1 ? 8 - offset : entry.Length);
            entry.CopyTo(expected, offset);
            offset += entry.Length;
        }
        var writes = string.Join("\n", rows.Select(row => $"p.WriteProperty \"{row.Data.Item1.ToUpperInvariant()}\", {row.Data.Item2}"));
        await Run($"""
            {HexHelper}
            Dim p As PropertyBag, q As PropertyBag, b(0 To 2) As Byte
            b(0) = 1: b(1) = 2: b(2) = 255
            Set p = New PropertyBag: Set q = New PropertyBag
            {writes}
            p.WriteProperty "Def", 7, 7
            Debug.Print HexBytes(p.Contents)
            q.Contents = p.Contents
            Debug.Print HexBytes(q.Contents)
            Debug.Print q.ReadProperty("DUP")
            """);
        AssertDebugLog([new Vb6Value(Convert.ToHexString(expected)), new Vb6Value(Convert.ToHexString(expected)), new Vb6Value(2)]);
    }

    [Theory]
    [InlineData("p.WriteProperty \"x\", Null, Null")]
    [InlineData("p.Contents = Empty")]
    [InlineData("p.Contents = Null")]
    [InlineData("p.Contents = True")]
    [InlineData("p.Contents = Array(1)")]
    [InlineData("p.WriteProperty 1, 2")]
    [InlineData("v = p.ReadProperty(1)")]
    [InlineData("p.WriteProperty \"x\"")]
    [InlineData("v = p.ReadProperty(\"x\", 1, 2)")]
    [InlineData("v = p.OtherMember")]
    public async Task Unmeasured_edges_are_explicitly_refused(string statement)
    {
        var act = async () => await Run($"""
            Dim p As PropertyBag, v As Variant
            Set p = New PropertyBag
            {statement}
            """);
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*#4741*");
    }

    [Theory]
    [InlineData("p.WriteProperty \"a\", ChrW$(&HD800)")]
    [InlineData("p.WriteProperty ChrW$(&HD800), 1")]
    [InlineData("p.Contents = b")]
    public async Task Unpaired_utf16_text_is_explicitly_refused(string statement)
    {
        // A synthetic malformed UTF-16 payload tests HexIDE's refusal policy, not a VB6 value.
        var bytes = Convert.FromHexString("93B200001C00000008000100610000000000000061000100000000D8");
        var act = async () => await Run($"""
            Dim p As PropertyBag
            Set p = New PropertyBag
            {InputBytes(bytes)}
            {statement}
            """);
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*#4741*");
    }
}
