using System.Threading.Tasks;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The overflow edges of #234, each cell as vb6.exe gave it under <c>On Error Resume Next</c>: the result's TypeName, its
/// value, and Err.Number. Native code, p-code and the IDE (<c>/runexit</c>) agree on every cell here
/// (docs/vb6-fidelity-oracle.md, "Overflow edges").
/// </summary>
public class OverflowEdgeTests : BaseVBTestFixture
{
    private const string Declarations =
        "    Dim v As Variant, vi As Variant, vz As Variant, vs As Variant, vt As Variant, vd As Variant, vE As Variant\r\n" +
        "    Dim a As Integer, l As Long, y As Byte, z As Double, c As Currency, s As Single, f As Boolean, st As String\r\n";

    // One cell: the statement leaves its result in v, or leaves v Empty when it raises.
    private async Task<string> Cell(string statement)
    {
        await Run("Sub P()\r\n    On Error Resume Next\r\n" + Declarations + "    v = Empty\r\n    " + statement + "\r\n" +
                  "    Debug.Print TypeName(v) & \": \" & v & \" Err \" & Err.Number\r\nEnd Sub\r\nP\r\n");
        return (string)debug[^1].Value!;
    }

    // ── 0 / 0 is Overflow, unless Decimal's division does it ─────────────────

    [Theory]
    [InlineData("z = 0: v = z / z")]
    [InlineData("v = CDbl(0) / CDbl(0)")]
    [InlineData("c = 0: v = c / c")]
    [InlineData("vz = 0#: v = vz / vz")]
    [InlineData("y = 0: v = y / y")]
    [InlineData("a = 0: v = a / a")]
    [InlineData("l = 0: v = l / l")]
    [InlineData("s = 0: v = s / s")]
    [InlineData("f = False: v = f / f")]
    [InlineData("vE = Empty: v = vE / vE")]
    [InlineData("a = 0: z = 0: v = a / z")]
    [InlineData("a = 0: z = 0: v = z / a")]
    [InlineData("vi = CInt(0): v = vi / vi")]
    public async Task Zero_over_zero_is_Overflow(string statement)
    {
        // A numeric String's 0 / 0 is Err 6 too (measured), but a String operand is refused before it gets here (#1047).
        (await Cell(statement)).Should().Be("Empty:  Err 6");
    }

    [Theory]
    [InlineData("vd = CDec(0): v = vd / vd")]
    [InlineData("vd = CDec(0): a = 0: v = vd / a")]
    [InlineData("vd = CDec(0): a = 0: v = a / vd")]
    [InlineData("vd = CDec(0): z = 0: v = vd / z")]
    [InlineData("vd = CDec(0): z = 0: v = z / vd")]
    [InlineData("vd = CDec(0): c = 0: v = vd / c")]
    public async Task Zero_over_zero_with_a_Decimal_operand_is_Division_by_zero(string statement)
    {
        // Measured: a Decimal on either side makes it Decimal's division, which reports 11 for 0 / 0 as for 1 / 0.
        (await Cell(statement)).Should().Be("Empty:  Err 11");
    }

    [Theory]
    [InlineData("a = 1: v = a / 0", "Empty:  Err 11")]
    [InlineData("z = 1: v = z / 0", "Empty:  Err 11")]
    [InlineData("vd = CDec(0): vt = CDec(1): v = vt / vd", "Empty:  Err 11")]
    [InlineData("a = 0: v = a \\ a", "Empty:  Err 11")]
    [InlineData("a = 0: v = a Mod a", "Empty:  Err 11")]
    [InlineData("z = 0: v = z ^ -1", "Empty:  Err 5")]
    public async Task Everything_else_over_zero_keeps_its_own_error(string statement, string expected)
    {
        (await Cell(statement)).Should().Be(expected);
    }

    // ── a Variant Single widens to Double ────────────────────────────────────

    [Theory]
    [InlineData("vs = CSng(3E+38): v = vs + vs", "Double: 6.00000001099551E+38 Err 0")]
    [InlineData("vs = CSng(3E+38): vt = CSng(10): v = vs * vt", "Double: 3.00000000549776E+39 Err 0")]
    [InlineData("vs = CSng(-3E+38): vt = CSng(3E+38): v = vs - vt", "Double: -6.00000001099551E+38 Err 0")]
    [InlineData("vs = CSng(3E+38): vt = CSng(0.1): v = vs / vt", "Double: 2.99999996079427E+39 Err 0")]
    [InlineData("vs = CSng(3E+38): s = 3E+38: v = vs + s", "Double: 6.00000001099551E+38 Err 0")]
    [InlineData("vs = CSng(3E+38): s = 3E+38: v = s + vs", "Double: 6.00000001099551E+38 Err 0")]
    [InlineData("vs = CSng(3E+38): vi = CInt(1): v = vs + vi", "Single: 3E+38 Err 0")]
    public async Task A_Variant_Single_that_overflows_widens_to_Double(string statement, string expected)
    {
        (await Cell(statement)).Should().Be(expected);
    }

    [Theory]
    [InlineData("v = CSng(3E+38) + CSng(3E+38)")]
    [InlineData("s = 3E+38: v = s + s")]
    public async Task Two_typed_Singles_still_overflow(string statement)
    {
        (await Cell(statement)).Should().Be("Empty:  Err 6");
    }

    // ── unary minus at a type's minimum raises, as p-code and the IDE do ─────

    [Theory]
    [InlineData("a = -32768: v = -a", "Empty:  Err 6")]
    [InlineData("l = &H80000000: v = -l", "Empty:  Err 6")]
    [InlineData("v = -CInt(-32768)", "Empty:  Err 6")]
    [InlineData("vi = CInt(-32768): v = -vi", "Long: 32768 Err 0")]
    public async Task Unary_minus_at_a_minimum_raises_on_the_typed_path_and_widens_a_Variant(string statement, string expected)
    {
        // Native code wraps the typed rows silently. P-code and the IDE raise, and HexIDE matches the IDE.
        (await Cell(statement)).Should().Be(expected);
    }
}
