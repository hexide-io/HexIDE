using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// <c>Write #</c> and <c>Input #</c> (#1521). Every expected value is what vb6.exe wrote or read for the same statements, native
/// and p-code alike, under en-GB unless a test says otherwise: the probes and their logs are in
/// docs/vb6-fidelity-oracle.md, "Write # and Input #".
/// <para>The probes read a written file back byte by byte with Binary <c>Get</c>, which HexIDE does not have, so the Write #
/// tests read the file from C# instead. The Input # probes wrote each file with <c>Print #f, text;</c>, and these tests
/// write the same bytes from C#; the <c>Input #</c> statement and what it prints are the probe's own.</para>
/// </summary>
public class WriteInputTests : BaseVBTestFixture, IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hexide-write-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private string PathOf(string name) => Path.Combine(dir, name);

    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    [Theory]
    [InlineData("Write #f, 5, -5, 0, 32767, 70000", "5,-5,0,32767,70000\r\n")]
    [InlineData("Write #f, 2.5, -2.5, 0.5, -0.5, 1E+20, 1.5E-07", "2.5,-2.5,.5,-.5,1E+20,.00000015\r\n")]
    [InlineData("Write #f, CSng(0.1), CDbl(0.1), CCur(2.5), CCur(-0.25), CDec(1.5), CByte(7)", ".1,.1,2.5,-.25,1.5,7\r\n")]
    [InlineData("Write #f, \"a\", \"\", \"b c\", \"x,y\", \" lead\"", "\"a\",\"\",\"b c\",\"x,y\",\" lead\"\r\n")]
    [InlineData("Write #f, \"say \"\"hi\"\"\"", "\"say \"\"hi\"\"\"\r\n")]
    [InlineData("Write #f, \"a\"\"b\", \"c\"", "\"a\"\"b\",\"c\"\r\n")]
    [InlineData("Write #f, True, False", "#TRUE#,#FALSE#\r\n")]
    [InlineData("Write #f, #1/2/2003#, #1/2/2003 1:45:00 PM#, #1:45:00 PM#, #12/30/1899#",
        "#2003-01-02#,#2003-01-02 13:45:00#,#1899-12-30 13:45:00#,#1899-12-30#\r\n")]
    [InlineData("Write #f, #1/1/1800#, #1/1/100#, #12/31/9999 11:59:59 PM#, CDate(-1.5), CDate(0.5), #1/2/2003 12:00:00 AM#",
        "#1800-01-01#,#0100-01-01#,#9999-12-31 23:59:59#,#1899-12-29 12:00:00#,#1899-12-30 12:00:00#,#2003-01-02#\r\n")]
    [InlineData("v = Null: w = Empty: Write #f, v, w, \"end\"", "#NULL#,,\"end\"\r\n")]
    [InlineData("Write #f, \"a\"; \"b\"; 5", "\"a\",\"b\",5\r\n")]
    [InlineData("Write #f, \"a\",", "\"a\",")]
    [InlineData("Write #f, \"a\";", "\"a\",")]
    [InlineData("Write #f, \"a\";: Write #f, \"b\"", "\"a\",\"b\"\r\n")]
    [InlineData("Write #f,", "\r\n")]
    [InlineData("Write #f, \"l1\" & vbCrLf & \"l2\", 1", "\"l1\r\nl2\",1\r\n")]
    [InlineData("Write #f, \"a\", Spc(2), \"b\"", "\"a\",  ,\"b\"\r\n")]
    [InlineData("Write #f, \"a\", Tab(6), \"b\"", "\"a\", ,\"b\"\r\n")]
    [InlineData("Write #f, 1E-10, 1.5E-20, -1E-05, 0.0001, 0.00001", ".0000000001,1.5E-20,-.00001,.0001,.00001\r\n")]
    [InlineData("Write #f, 1E+15, 1E+16, 1.23456789012345E+15, 123456789012345#, 12345.6789",
        "1E+15,1E+16,1.23456789012345E+15,123456789012345,12345.6789\r\n")]
    [InlineData("Write #f, CSng(1 / 3), CDbl(1 / 3), 0.1 + 0.2, CSng(1E+20), CSng(1E-10)", ".3333333,.333333333333333,.3,1E+20,1E-10\r\n")]
    [InlineData("Write #f, 1E-15, 1E-16, 1.5E-14, 1.5E-15, 1.25E-14, 0.000123456789012345",
        ".000000000000001,1E-16,.000000000000015,1.5E-15,1.25E-14,1.23456789012345E-04\r\n")]
    [InlineData("Write #f, 999999999999999#, 1E+15 - 1, 1234567890123456#, -1E-15, -1E+15",
        "999999999999999,999999999999999,1.23456789012346E+15,-.000000000000001,-1E+15\r\n")]
    [InlineData("Write #f, CSng(1.5E-06), CSng(1.5E-07), CSng(1234567), CSng(12345678), CSng(0.0000001), CSng(0.000001)",
        ".0000015,1.5E-07,1234567,1.234568E+07,.0000001,.000001\r\n")]
    // The probe wrote CCur("922337203685477.5807") and CDec("79228162514264337593543950335"), which HexIDE's CCur and CDec
    // cannot read from a String yet (#1531). The same Currency is made here by arithmetic, beside a Decimal of 21 digits.
    [InlineData("Write #f, CCur(922337203685477@) + CCur(0.5807), CCur(\"-0.0001\"), CCur(100)", "922337203685477.5807,-.0001,100\r\n")]
    [InlineData("Write #f, CDec(1E+20), CDec(\"0.0000000000000000000000000001\"), CDec(-0.5)",
        "100000000000000000000,.0000000000000000000000000001,-.5\r\n")]
    [InlineData("Write #f, CByte(255), CInt(-32768), CLng(-2147483648#)", "255,-32768,-2147483648\r\n")]
    public async Task Write_writes_what_vb6_exe_wrote(string statement, string expected)
    {
        using (VbLocale.Use(EnGb))
            await Run($$"""
                Dim f As Integer, v As Variant, w As Variant
                f = FreeFile
                Open "{{PathOf("w.txt")}}" For Output As #f
                {{statement}}
                Close #f
                """);

        Encoding.Latin1.GetString(File.ReadAllBytes(PathOf("w.txt"))).Should().Be(expected);
    }

    // A number's point is a point under de-DE too, where Print # writes a comma (measured: " 2,5 ").
    [Fact]
    public async Task Write_ignores_the_locale()
    {
        using (VbLocale.Use(CultureInfo.GetCultureInfo("de-DE")))
            await Run($$"""
                Dim f As Integer
                f = FreeFile
                Open "{{PathOf("w.txt")}}" For Output As #f
                Write #f, 2.5, -0.25, 1E+20, CCur(2.5), CDec(1.5), CSng(0.1), #1/2/2003#, True
                Close #f
                """);

        Encoding.Latin1.GetString(File.ReadAllBytes(PathOf("w.txt"))).Should().Be("2.5,-.25,1E+20,2.5,1.5,.1,#2003-01-02#,#TRUE#\r\n");
    }

    // Reads `content` with `Input #f, <targets>` into Variants r1 to r10, and returns what `print` gave, with Err.Number.
    private async Task<string> ReadVariants(string content, string targets, string print)
    {
        File.WriteAllBytes(PathOf("in.txt"), Encoding.Latin1.GetBytes(content));
        using (VbLocale.Use(EnGb))
            await Run($$"""
                Private Function D(v) As String
                    D = TypeName(v) & " " & CStr(v)
                End Function
                Private Sub Rows()
                    On Error Resume Next
                    Dim f As Integer, r1, r2, r3, r4, r5, r6, r7, r8, r9, r10
                    r1 = "kept"
                    f = FreeFile
                    Open "{{PathOf("in.txt")}}" For Input As #f
                    Input #f, {{targets}}
                    Debug.Print {{print}} & " err " & Err.Number
                    Close #f
                End Sub
                Rows
                """);
        return (string)debug.Single().Value!;
    }

    private static string AllOf(int count) => string.Join(" & \"|\" & ", Enumerable.Range(1, count).Select(k => $"D(r{k})"));

    private static string Targets(int count) => string.Join(", ", Enumerable.Range(1, count).Select(k => $"r{k}"));

    // Into a Variant, a field is typed by what it holds: the type and value TypeName and CStr gave in vb6.exe.
    [Theory]
    [InlineData("5,-5,0,32767,70000\r\n", 5, "Integer 5|Integer -5|Integer 0|Integer 32767|Long 70000")]
    [InlineData("2.12345,123456789012,1.50,-0,1e3,&H10,abc,TRUE,  7  ,\r\n", 10,
        "Decimal 2.12345|Double 123456789012|Currency 1.5|Integer 0|Double 1000|Integer 16|String abc|String TRUE|Integer 7|Empty ")]
    [InlineData("1.2345,1.23456,99999.9999,922337203685477,922337203685478,2147483647,2147483648,32768,1E+300\r\n", 9,
        "Currency 1.2345|Decimal 1.23456|Currency 99999.9999|Double 922337203685477|Double 922337203685478|Long 2147483647|Double 2147483648|Long 32768|Double 1E+300")]
    [InlineData("  hello world  ,\"q\" , x\r\nnext line\r\n", 4, "String hello world|String q|String x|String next line")]
    [InlineData("\"a\"\"b\",\"c\"\r\n", 3, "String a|String b|String c")]
    [InlineData("#TRUE#,#FALSE#\r\n", 2, "Boolean True|Boolean False")]
    [InlineData("#2003-01-02#,#2003-01-02 13:45:00#,#1899-12-30 13:45:00#,#1899-12-30#\r\n", 4,
        "Date 02/01/2003|Date 02/01/2003 13:45:00|Date 13:45:00|Date 00:00:00")]
    [InlineData("#1/2/2003#,#2003-01-02 13:45:00#,#13:45:00#,#FALSE#\r\n", 4,
        "Date 02/01/2003|Date 02/01/2003 13:45:00|Date 13:45:00|Boolean False")]
    [InlineData("123456789012.5,1234567890123456.5,12345678901234567890,1.123456789012345678901234567890\r\n", 4,
        "Currency 123456789012.5|Decimal 1234567890123456.5|Decimal 12345678901234567890|Decimal 1.1234567890123456789012345679")]
    [InlineData("-1.5,+5,5.,.5,1d3,5%,&O17,1E+400\r\n", 8,
        "Currency -1.5|Integer 5|Integer 5|Currency 0.5|Double 1000|Integer 5|Integer 15|String 1E+400")]
    [InlineData("(5),1 2,-.00001,.000000000000000000000000000012,922337203685477.5807,922337203685477.58071\r\n", 6,
        "String (5)|Integer 1|Integer 2|Decimal -0.00001|Double 1.2E-29|Currency 922337203685477.5807")]
    [InlineData("999999999999999,1234567890123456,9999999999999999,100000000000000000000,123456789012345678901234567890\r\n", 5,
        "Double 999999999999999|Decimal 1234567890123456|Decimal 9999999999999999|Decimal 100000000000000000000|Double 1.23456789012346E+29")]
    [InlineData("2.10000,1.00000,0.12340,5.0,1.0000,-0.00000\r\n", 6,
        "Currency 2.1|Integer 1|Currency 0.1234|Integer 5|Integer 1|Decimal 0")]
    [InlineData("0.0,0.0000,0.00000,00.5,-0.5,1.23450\r\n", 6,
        "Currency 0|Currency 0|Decimal 0|Currency 0.5|Currency -0.5|Currency 1.2345")]
    [InlineData("&HFFFF,&H10000,&HFFFFFFFF,&H7FFFFFFF,&O177777,&H8000\r\n", 6,
        "Integer -1|Long 65536|Integer -1|Long 2147483647|Integer -1|Integer -32768")]
    [InlineData("&H1FFFFFFFF,5\r\n", 2, "Integer -1|Integer 5")]
    [InlineData("1E-400,-&H10\r\n", 2, "Double 0|String -&H10")]
    [InlineData("5 x,6\r\n", 3, "Integer 5|String x|Integer 6")]
    [InlineData("5 ,6\r\n", 2, "Integer 5|Integer 6")]
    [InlineData("\"a\" junk,b\r\n", 3, "String a|String junk|String b")]
    [InlineData("\t5,\t\"t\"\r\n", 2, "Integer 5|String t")]
    [InlineData(",,5\r\n", 3, "Empty |Empty |Integer 5")]
    [InlineData("  ,5\r\n", 2, "Empty |Integer 5")]
    [InlineData("#abc#,5\r\n", 2, "String #abc#|Integer 5")]
    [InlineData("#,5\r\n", 2, "String #|Integer 5")]
    [InlineData("-abc,5\r\n", 2, "String -abc|Integer 5")]
    [InlineData(".,5\r\n", 2, "String .|Integer 5")]
    public async Task Into_a_Variant_a_field_is_typed_by_what_it_holds(string content, int count, string expected)
    {
        (await ReadVariants(content, Targets(count), AllOf(count))).Should().Be(expected + " err 0");
    }

    [Theory]
    [InlineData("\"a\",\"\",\"b c\",\"x,y\",\" lead\"\r\n", "r1, r2, r3, r4, r5",
        "\"[\" & r1 & \"][\" & r2 & \"][\" & r3 & \"][\" & r4 & \"][\" & r5 & \"] \" & TypeName(r2)", "[a][][b c][x,y][ lead] String")]
    [InlineData("\"say \"\"hi\"\"\"\r\n", "r1", "\"[\" & r1 & \"]\"", "[say ]")]
    [InlineData("#NULL#,,\"end\"\r\n", "r1, r2, r3", "TypeName(r1) & \"|\" & TypeName(r2) & \"|\" & r3", "Null|Empty|end")]
    [InlineData("\"l1\r\nl2\",1\r\n", "r1, r2", "\"[\" & Replace(Replace(r1, vbCr, \"<CR>\"), vbLf, \"<LF>\") & \"] \" & r2", "[l1<CR><LF>l2] 1")]
    public async Task What_Write_wrote_reads_back_as_vb6_exe_read_it(string content, string targets, string print, string expected)
    {
        (await ReadVariants(content, targets, print)).Should().Be(expected + " err 0");
    }

    // Past the end of the file it is 62, and a Variant is emptied first, even one that held "kept".
    [Theory]
    [InlineData("12abc,5\r\n", 3, "Integer 12|Integer 5|Empty ")]
    [InlineData("1\n2\r3\r\n", 3, "Integer 1|Integer 3|Empty ")]
    [InlineData("1,2\r\n", 3, "Integer 1|Integer 2|Empty ")]
    [InlineData("", 1, "Empty ")]
    public async Task Past_the_end_a_Variant_is_emptied_and_it_is_62(string content, int count, string expected)
    {
        (await ReadVariants(content, Targets(count), AllOf(count))).Should().Be(expected + " err 62");
    }

    // Reads `content` into one variable of `type`, which held `initial`, and returns its value and Err.Number.
    private async Task<string> ReadTyped(string type, string initial, string content)
    {
        File.WriteAllBytes(PathOf("in.txt"), Encoding.Latin1.GetBytes(content));
        using (VbLocale.Use(EnGb))
            await Run($$"""
                Private Sub Rows()
                    On Error Resume Next
                    Dim f As Integer, x As {{type}}
                    f = FreeFile
                    Open "{{PathOf("in.txt")}}" For Input As #f
                    Err.Clear
                    x = {{initial}}
                    Input #f, x
                    Debug.Print "[" & x & "] err " & Err.Number
                    Close #f
                End Sub
                Rows
                """);
        return (string)debug.Single().Value!;
    }

    // A typed variable reads the field for its type. 6 and 13 leave it as it was.
    [Theory]
    [InlineData("Boolean", "False", "1", "[False] err 6")]
    [InlineData("Boolean", "False", "0", "[False] err 6")]
    [InlineData("Boolean", "False", "-1", "[False] err 6")]
    [InlineData("Boolean", "False", "2", "[False] err 6")]
    [InlineData("Boolean", "False", "True", "[False] err 0")]
    [InlineData("Boolean", "False", "abc", "[False] err 0")]
    [InlineData("Boolean", "False", "#TRUE#", "[True] err 0")]
    [InlineData("Boolean", "False", "\"\"", "[False] err 0")]
    [InlineData("Boolean", "True", "#FALSE#", "[False] err 0")]
    [InlineData("Boolean", "True", "\"True\"", "[False] err 0")]
    [InlineData("Boolean", "True", ",", "[False] err 0")]
    [InlineData("Boolean", "True", "#NULL#", "[True] err 13")]
    [InlineData("Boolean", "True", "#2003-01-02#", "[True] err 13")]
    [InlineData("Date", "#1/1/1999#", "2003-01-02", "[01/01/1999] err 6")]
    [InlineData("Date", "#1/1/1999#", "02/01/2003", "[01/01/1999] err 6")]
    [InlineData("Date", "#1/1/1999#", "37000", "[01/01/1999] err 6")]
    [InlineData("Date", "#1/1/1999#", "#2003-01-02#", "[02/01/2003] err 0")]
    [InlineData("Date", "#1/1/1999#", "#13:45:00#", "[13:45:00] err 0")]
    [InlineData("Date", "#1/1/1999#", "abc", "[00:00:00] err 0")]
    [InlineData("Date", "#1/1/1999#", "\"2003-01-02\"", "[00:00:00] err 0")]
    [InlineData("Date", "#1/1/1999#", "\"\"", "[00:00:00] err 0")]
    [InlineData("Date", "#1/1/1999#", ",", "[00:00:00] err 0")]
    [InlineData("Date", "#1/1/1999#", "#NULL#", "[01/01/1999] err 13")]
    [InlineData("Date", "#1/1/1999#", "#TRUE#", "[01/01/1999] err 13")]
    [InlineData("Double", "9", "abc", "[0] err 0")]
    [InlineData("Double", "9", "2.5", "[2.5] err 0")]
    [InlineData("Double", "9", "1E3", "[1000] err 0")]
    [InlineData("Double", "9", "#TRUE#", "[0] err 0")]
    [InlineData("Double", "9", "#2003-01-02#", "[0] err 0")]
    [InlineData("Double", "9", "\"\"", "[0] err 0")]
    [InlineData("Double", "9", "1,5", "[1] err 0")]
    [InlineData("Double", "9", ".5", "[0.5] err 0")]
    [InlineData("Double", "9", "\"5\"", "[0] err 0")]
    [InlineData("Double", "9", "\"2.5\"", "[0] err 0")]
    [InlineData("Double", "9", "2.5\"", "[2.5] err 0")]
    [InlineData("Double", "9", "#2.5#", "[0] err 0")]
    [InlineData("Double", "9", "x2.5", "[0] err 0")]
    [InlineData("Double", "9", "-", "[0] err 0")]
    [InlineData("Double", "9", "1E+309", "[9] err 6")]
    [InlineData("Double", "9", "1E+400", "[9] err 6")]
    [InlineData("Integer", "9", "2.5", "[2] err 0")]
    [InlineData("Integer", "9", "3.5", "[4] err 0")]
    [InlineData("Integer", "9", "#TRUE#", "[0] err 0")]
    [InlineData("Integer", "9", "#NULL#", "[0] err 0")]
    [InlineData("Integer", "9", "\"\"", "[0] err 0")]
    [InlineData("Integer", "9", "\"5\"", "[0] err 0")]
    [InlineData("Integer", "9", "abc", "[0] err 0")]
    [InlineData("Integer", "9", "1E3", "[1000] err 0")]
    [InlineData("Integer", "9", "&HFFFF", "[-1] err 0")]
    [InlineData("Integer", "9", "2.5\"", "[2] err 0")]
    [InlineData("Integer", "9", "-32769", "[9] err 6")]
    [InlineData("Integer", "9", "70000", "[9] err 6")]
    [InlineData("Integer", "9", "1E+309", "[9] err 6")]
    [InlineData("Byte", "9", "255", "[255] err 0")]
    [InlineData("Byte", "9", "300", "[9] err 6")]
    [InlineData("Byte", "9", "-1", "[9] err 6")]
    [InlineData("Byte", "9", "\"5\"", "[0] err 0")]
    [InlineData("Long", "9", "1E3", "[1000] err 0")]
    [InlineData("Long", "9", "2147483648", "[9] err 6")]
    [InlineData("Long", "9", "\"5\"", "[0] err 0")]
    [InlineData("Single", "9", "0.1", "[0.1] err 0")]
    [InlineData("Single", "9", "1.5E-07", "[1.5E-07] err 0")]
    [InlineData("Single", "9", "1E+39", "[9] err 6")]
    [InlineData("Single", "9", "\"2.5\"", "[0] err 0")]
    [InlineData("Currency", "9", "2.12345", "[2.1234] err 0")]
    [InlineData("Currency", "9", "abc", "[0] err 0")]
    [InlineData("Currency", "9", "\"5\"", "[0] err 0")]
    [InlineData("Currency", "9", "1E+309", "[9] err 6")]
    [InlineData("String", "\"kept\"", "\"\"", "[] err 0")]
    [InlineData("String", "\"kept\"", " a ", "[a] err 0")]
    [InlineData("String", "\"kept\"", "#2003-01-02#", "[#2003-01-02#] err 0")]
    [InlineData("String", "\"kept\"", "#NULL#", "[#NULL#] err 0")]
    [InlineData("String", "\"kept\"", "5", "[5] err 0")]
    [InlineData("String", "\"kept\"", "1 2,3", "[1 2] err 0")]
    [InlineData("String", "\"kept\"", "12abc,5", "[12abc] err 0")]
    [InlineData("String", "\"kept\"", "\"a\"b,c", "[a] err 0")]
    [InlineData("String", "\"kept\"", "\"open\r\n", "[open\r\n] err 0")]
    public async Task A_typed_variable_reads_the_field_for_its_type(string type, string initial, string content, string expected)
    {
        (await ReadTyped(type, initial, content + (content.EndsWith('\n') ? "" : "\r\n"))).Should().Be(expected);
    }

    // A number ends at a blank, so `a b` is two fields to a numeric variable and `#ERROR 5#` is too.
    [Fact]
    public async Task A_numeric_variable_reads_a_blank_as_the_end_of_its_field()
    {
        File.WriteAllBytes(PathOf("in.txt"), Encoding.Latin1.GetBytes("a b,5\r\n#ERROR 5#,#abc#\r\n1 2,3\r\nabc,5\r\n"));
        await Run($$"""
            Private Sub Rows()
                Dim f As Integer, i As Integer, j As Integer
                f = FreeFile
                Open "{{PathOf("in.txt")}}" For Input As #f
                Input #f, i, j
                Debug.Print i & " " & j
                Line Input #f, s
                Input #f, i, j
                Debug.Print i & " " & j
                Line Input #f, s
                Input #f, i, j
                Debug.Print i & " " & j
                Line Input #f, s
                Input #f, i, j
                Debug.Print i & " " & j
                Close #f
            End Sub
            Rows
            """);

        AssertDebugLog(["0 0", "0 5", "1 2", "0 5"]);
    }

    // Past the end a typed variable keeps what it had.
    [Fact]
    public async Task Past_the_end_a_typed_variable_keeps_its_value_and_it_is_62()
    {
        File.WriteAllBytes(PathOf("in.txt"), Encoding.Latin1.GetBytes("1\r\n"));
        using (VbLocale.Use(EnGb))
            await Run($$"""
                Private Sub Rows()
                    On Error Resume Next
                    Dim f As Integer, i As Integer, j As Integer, s As String, dt As Date, b As Boolean
                    f = FreeFile
                    Open "{{PathOf("in.txt")}}" For Input As #f
                    Input #f, j
                    i = 9: s = "kept": dt = #1/1/1999#: b = True
                    Err.Clear: Input #f, i: Debug.Print "eof into Integer: " & i & " err " & Err.Number
                    Err.Clear: Input #f, s: Debug.Print "eof into String: [" & s & "] err " & Err.Number
                    Err.Clear: Input #f, dt: Debug.Print "eof into Date: " & dt & " err " & Err.Number
                    Err.Clear: Input #f, b: Debug.Print "eof into Boolean: " & b & " err " & Err.Number
                    Close #f
                End Sub
                Rows
                """);

        AssertDebugLog([
            "eof into Integer: 9 err 62",
            "eof into String: [kept] err 62",
            "eof into Date: 01/01/1999 err 62",
            "eof into Boolean: True err 62",
        ]);
    }

    // After a line's last field the line end is taken too, so Line Input # reads the next line; after a comma it is not.
    [Theory]
    [InlineData("3\r\nline1\r\n", "n", "n [3] then Line Input [line1]")]
    [InlineData("5,6\r\nline\r\n", "n", "n [5] then Line Input [6]")]
    [InlineData("\"q\"\r\nline\r\n", "s", "s [q] then Line Input [line]")]
    [InlineData("7   \r\nline\r\n", "n", "n [7] then Line Input [line]")]
    [InlineData("\"q\"   \r\nline\r\n", "s", "s [q] then Line Input [line]")]
    [InlineData("abc  \r\nline\r\n", "s", "s [abc] then Line Input [line]")]
    [InlineData("8,\r\nline\r\n", "n", "n [8] then Line Input []")]
    public async Task Line_Input_after_Input_reads_from_where_the_fields_ended(string content, string target, string expected)
    {
        File.WriteAllBytes(PathOf("in.txt"), Encoding.Latin1.GetBytes(content));
        await Run($$"""
            Private Sub Rows()
                On Error Resume Next
                Dim f As Integer, n As Integer, s As String, ln As String
                f = FreeFile
                Open "{{PathOf("in.txt")}}" For Input As #f
                Err.Clear
                Input #f, {{target}}
                Line Input #f, ln
                Debug.Print "{{target}} [" & {{target}} & "] then Line Input [" & ln & "] err " & Err.Number
                Close #f
            End Sub
            Rows
            """);

        AssertDebugLog([expected + " err 0"]);
    }

    // The point is a point under de-DE too: "2.5" reads as 2.5, and "2,5" as 2 then 5.
    [Fact]
    public async Task Input_ignores_the_locale()
    {
        File.WriteAllBytes(PathOf("in.txt"), Encoding.Latin1.GetBytes("2.5,\"1,5\",2.12345\r\n2.5\r\n"));
        using (VbLocale.Use(CultureInfo.GetCultureInfo("de-DE")))
            await Run($$"""
                Private Function D(v) As String
                    D = TypeName(v) & " " & CStr(v)
                End Function
                Private Sub Rows()
                    Dim f As Integer, r1, r2, r3, dbl As Double
                    f = FreeFile
                    Open "{{PathOf("in.txt")}}" For Input As #f
                    Input #f, r1, r2, r3
                    Debug.Print "in c: " & D(r1) & "|" & D(r2) & "|" & D(r3)
                    Input #f, dbl
                    Debug.Print "in d into Double: " & dbl
                    Close #f
                End Sub
                Rows
                """);

        AssertDebugLog(["in c: Currency 2,5|String 1,5|Decimal 2,12345", "in d into Double: 2,5"]);
    }

    [Fact]
    public async Task Input_into_an_array_element_is_refused_by_name()
    {
        File.WriteAllBytes(PathOf("in.txt"), Encoding.Latin1.GetBytes("1\r\n"));
        var run = () => Run($$"""
            Dim a(1) As Integer, f As Integer
            f = FreeFile
            Open "{{PathOf("in.txt")}}" For Input As #f
            Input #f, a(0)
            """);

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*Input # into a(0)*#1521*");
    }
}
