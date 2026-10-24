using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Oracle: "Numeric type pairs — five arithmetic tables" (2026-09-03), #230.
// B/I/L/S/F/C/D = Byte/Integer/Long/Single/Double/Currency/Decimal.
// Rows and columns follow the oracle's order; no production lookup is used here.
public class ArithmeticTypeTableTests : BaseVBTestFixture
{
    private static readonly string[] Types = ["Byte", "Integer", "Long", "Single", "Double", "Currency", "Decimal"];
    private const string Declarations = "Dim b As Byte, i As Integer, l As Long, s As Single, f As Double, c As Currency, d As Variant\nb = 6: i = 7: l = 8: s = 9: f = 10: c = 11: d = CDec(12)\n";
    private const string Variables = "bilsfcd";
    private static readonly string[][] Tables =
    [
        ["BILSFCD", "IILSFCD", "LLLFFCD", "SSFSFCD", "FFFFFCD", "CCCCCCD", "DDDDDDD"],
        ["BILSFCD", "IILSFCD", "LLLFFCD", "SSFSFFD", "FFFFFFD", "CCCFFCD", "DDDDDDD"],
        ["FFFSFFD", "FFFSFFD", "FFFFFFD", "SSFSFFD", "FFFFFFD", "FFFFFFD", "DDDDDDD"],
        ["BILLLLL", "IILLLLL", "LLLLLLL", "LLLLLLL", "LLLLLLL", "LLLLLLL", "LLLLLLL"],
        ["FFFFFFF", "FFFFFFF", "FFFFFFF", "FFFFFFF", "FFFFFFF", "FFFFFFF", "FFFFFFF"],
    ];

    public static IEnumerable<object[]> AllCells()
    {
        string[] ops = ["+", "-", "*", "/", @"\", "Mod", "^"];
        int[] table = [0, 0, 1, 2, 3, 3, 4];
        for (int op = 0; op < ops.Length; op++)
        for (int l = 0; l < 7; l++)
        for (int r = 0; r < 7; r++)
            yield return [ops[op], l, r, Types["BILSFCD".IndexOf(Tables[table[op]][l][r])]];
    }

    public static IEnumerable<object[]> ChangedCells()
    {
        foreach (var row in AllCells())
        {
            var op = (string)row[0];
            var l = (int)row[1];
            var r = (int)row[2];
            bool longSingle = l == 2 && r == 3 || l == 3 && r == 2;
            bool currencyFloat = l == 5 && (r == 3 || r == 4) || r == 5 && (l == 3 || l == 4);
            if ((op is "+" or "-" or "*" or "/") && longSingle ||
                op == "*" && currencyFloat || op == "/" && (l == 6 || r == 6) ||
                (op is @"\" or "Mod") && l == 0 && r == 0)
                yield return row;
        }
    }

    [Theory]
    [MemberData(nameof(ChangedCells))]
    public async Task Issue230_ChangedCell(string op, int l, int r, string expected)
    {
        await CheckCell(op, l, r, expected);
    }

    [Theory]
    [MemberData(nameof(AllCells))]
    public async Task All49PairsAcross7Operators(string op, int l, int r, string expected)
    {
        await CheckCell(op, l, r, expected);
    }

    private async Task CheckCell(string op, int l, int r, string expected)
    {
        await Run(Declarations + $"Debug.Print TypeName({Variables[l]} {op} {Variables[r]})\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    // Value rows from the issue and the oracle's "Why a wrong result type", rounding, and power rows.
    // Assert boxed values exactly: the fixture's 0.001 tolerance would hide Currency rounding errors.
    [Theory]
    [InlineData("l = 16777217: s = 0", "l + s", "Double", "16777217")]
    [InlineData("c = 1: f = 0.00005", "c + f", "Currency", "1.0001")]
    [InlineData("f = 0.12345", "f", "Double", "0.12345")]
    [InlineData("c = 0: f = 0.12345", "c + f", "Currency", "0.1235")]
    [InlineData("c = 0: f = 0.12345", "(c + f) * 2", "Currency", "0.247")]
    [InlineData("c = 1: s = 0.00005", "c + s", "Currency", "1")]
    [InlineData("c = 1: f = 1E30", "c * f", "Double", "1E30")]
    [InlineData("f = 2.5: i = 1", @"f \ i", "Long", "2")]
    [InlineData("f = 3.5: i = 1", @"f \ i", "Long", "4")]
    [InlineData("f = -2.5: i = 1", @"f \ i", "Long", "-2")]
    [InlineData("f = 10.6: i = 3", "f Mod i", "Long", "2")]
    public async Task MeasuredValues(string setup, string expression, string type, string value)
    {
        await Run(Declarations + setup + "\nDebug.Print TypeName(" + expression + ")\nDebug.Print " + expression + "\n");
        debug[0].Value.Should().Be(type);
        object expected = type switch
        {
            "Currency" => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
            "Long" => (object)long.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
            _ => (object)double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
        };
        debug[1].Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("c = 1: f = 1E30", "c + f", 6)]
    [InlineData("f = 0: i = -1", "f ^ i", 5)]
    [InlineData("f = 1E308: i = 2", "f ^ i", 6)]
    public async Task MeasuredErrors(string setup, string expression, int error)
    {
        Func<Task> act = () => Run(Declarations + setup + "\nDebug.Print " + expression + "\n");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(error);
    }
}
