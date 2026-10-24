using System.Globalization;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A For loop whose start, limit or step is fractional, for every counter type that keeps a fraction (#983). Each case is a
/// program that ran on vb6.exe (SP6, en-GB), native and p-code, in four batches. The expected lines are what its p-code
/// build logged, the build the VB6 IDE runs and HexIDE follows: docs/vb6-fidelity-oracle.md, "A For loop with a fraction".
/// </summary>
public class FractionalForTests : BaseVBTestFixture
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    private const string Helpers = """
        Public Sub L(ByVal s As String)
            Debug.Print s
        End Sub

        Public Function Tv(v As Variant) As String
            Tv = TypeName(v) & " " & CStr(v)
        End Function

        Public Function Lim(ByVal fail As Boolean, ByVal v As Variant) As Variant
            If fail Then Lim = 1 \ 0 Else Lim = v
        End Function
        """;

    private List<string?> Printed() => debug.Select(v => v.Value as string).ToList();

    private async Task RunCase(string program)
    {
        using var locale = VbLocale.Use(EnGb);
        await Run(Helpers + Environment.NewLine + program + Environment.NewLine);
    }

    [Fact]
    public async Task A_Double_counter__For_x_0_To_1_Step_0_1()
    {
        await RunCase("""
            Sub dbl_tenth()
                On Error Resume Next
                Dim n As Long
                Dim x As Double
                L "=== dbl-tenth: For x = 0 To 1 Step 0.1"
                For x = 0 To 1 Step 0.1
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            dbl_tenth
            """);
        Printed().Should().Equal(
            "=== dbl-tenth: For x = 0 To 1 Step 0.1",
            "  pass 1: Double 0",
            "  pass 2: Double 0.1",
            "  pass 3: Double 0.2",
            "  pass 4: Double 0.3",
            "  pass 5: Double 0.4",
            "  pass 6: Double 0.5",
            "  pass 7: Double 0.6",
            "  pass 8: Double 0.7",
            "  pass 9: Double 0.8",
            "  pass 10: Double 0.9",
            "  pass 11: Double 1",
            "  after: Double 1.1 passes 11 err 0");
    }

    [Fact]
    public async Task A_Double_counter__For_x_1_To_2_Step_0_1()
    {
        await RunCase("""
            Sub dbl_tenth_from1()
                On Error Resume Next
                Dim n As Long
                Dim x As Double
                L "=== dbl-tenth-from1: For x = 1 To 2 Step 0.1"
                For x = 1 To 2 Step 0.1
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            dbl_tenth_from1
            """);
        Printed().Should().Equal(
            "=== dbl-tenth-from1: For x = 1 To 2 Step 0.1",
            "  pass 1: Double 1",
            "  pass 2: Double 1.1",
            "  pass 3: Double 1.2",
            "  pass 4: Double 1.3",
            "  pass 5: Double 1.4",
            "  pass 6: Double 1.5",
            "  pass 7: Double 1.6",
            "  pass 8: Double 1.7",
            "  pass 9: Double 1.8",
            "  pass 10: Double 1.9",
            "  after: Double 2 passes 10 err 0");
    }

    [Fact]
    public async Task A_Double_counter__For_x_0_To_1_Step_0_25()
    {
        await RunCase("""
            Sub dbl_quarter()
                On Error Resume Next
                Dim n As Long
                Dim x As Double
                L "=== dbl-quarter: For x = 0 To 1 Step 0.25"
                For x = 0 To 1 Step 0.25
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            dbl_quarter
            """);
        Printed().Should().Equal(
            "=== dbl-quarter: For x = 0 To 1 Step 0.25",
            "  pass 1: Double 0",
            "  pass 2: Double 0.25",
            "  pass 3: Double 0.5",
            "  pass 4: Double 0.75",
            "  pass 5: Double 1",
            "  after: Double 1.25 passes 5 err 0");
    }

    [Fact]
    public async Task A_Double_counter__For_x_1_To_0_Step_0_25()
    {
        await RunCase("""
            Sub dbl_down()
                On Error Resume Next
                Dim n As Long
                Dim x As Double
                L "=== dbl-down: For x = 1 To 0 Step -0.25"
                For x = 1 To 0 Step -0.25
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            dbl_down
            """);
        Printed().Should().Equal(
            "=== dbl-down: For x = 1 To 0 Step -0.25",
            "  pass 1: Double 1",
            "  pass 2: Double 0.75",
            "  pass 3: Double 0.5",
            "  pass 4: Double 0.25",
            "  pass 5: Double 0",
            "  after: Double -0.25 passes 5 err 0");
    }

    [Fact]
    public async Task A_Double_counter__For_x_0_5_To_3()
    {
        await RunCase("""
            Sub dbl_halfstart()
                On Error Resume Next
                Dim n As Long
                Dim x As Double
                L "=== dbl-halfstart: For x = 0.5 To 3"
                For x = 0.5 To 3
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            dbl_halfstart
            """);
        Printed().Should().Equal(
            "=== dbl-halfstart: For x = 0.5 To 3",
            "  pass 1: Double 0.5",
            "  pass 2: Double 1.5",
            "  pass 3: Double 2.5",
            "  after: Double 3.5 passes 3 err 0");
    }

    [Fact]
    public async Task A_Double_counter__For_x_1_To_2_5()
    {
        await RunCase("""
            Sub dbl_halflimit()
                On Error Resume Next
                Dim n As Long
                Dim x As Double
                L "=== dbl-halflimit: For x = 1 To 2.5"
                For x = 1 To 2.5
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            dbl_halflimit
            """);
        Printed().Should().Equal(
            "=== dbl-halflimit: For x = 1 To 2.5",
            "  pass 1: Double 1",
            "  pass 2: Double 2",
            "  after: Double 3 passes 2 err 0");
    }

    [Fact]
    public async Task A_Double_counter__For_x_1_5_To_1()
    {
        await RunCase("""
            Sub dbl_empty()
                On Error Resume Next
                Dim n As Long
                Dim x As Double
                L "=== dbl-empty: For x = 1.5 To 1"
                For x = 1.5 To 1
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            dbl_empty
            """);
        Printed().Should().Equal(
            "=== dbl-empty: For x = 1.5 To 1",
            "  after: Double 1.5 passes 0 err 0");
    }

    [Fact]
    public async Task A_Single_counter__For_x_0_To_1_Step_0_1()
    {
        await RunCase("""
            Sub sng_tenth()
                On Error Resume Next
                Dim n As Long
                Dim x As Single
                L "=== sng-tenth: For x = 0 To 1 Step 0.1"
                For x = 0 To 1 Step 0.1
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            sng_tenth
            """);
        Printed().Should().Equal(
            "=== sng-tenth: For x = 0 To 1 Step 0.1",
            "  pass 1: Single 0",
            "  pass 2: Single 0.1",
            "  pass 3: Single 0.2",
            "  pass 4: Single 0.3",
            "  pass 5: Single 0.4",
            "  pass 6: Single 0.5",
            "  pass 7: Single 0.6",
            "  pass 8: Single 0.7",
            "  pass 9: Single 0.8000001",
            "  pass 10: Single 0.9000001",
            "  after: Single 1 passes 10 err 0");
    }

    [Fact]
    public async Task A_Single_counter__For_x_1_To_2_Step_0_5()
    {
        await RunCase("""
            Sub sng_half()
                On Error Resume Next
                Dim n As Long
                Dim x As Single
                L "=== sng-half: For x = 1 To 2 Step 0.5"
                For x = 1 To 2 Step 0.5
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            sng_half
            """);
        Printed().Should().Equal(
            "=== sng-half: For x = 1 To 2 Step 0.5",
            "  pass 1: Single 1",
            "  pass 2: Single 1.5",
            "  pass 3: Single 2",
            "  after: Single 2.5 passes 3 err 0");
    }

    [Fact]
    public async Task A_Currency_counter__For_x_0_To_1_Step_0_1()
    {
        await RunCase("""
            Sub cur_tenth()
                On Error Resume Next
                Dim n As Long
                Dim x As Currency
                L "=== cur-tenth: For x = 0 To 1 Step 0.1"
                For x = 0 To 1 Step 0.1
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            cur_tenth
            """);
        Printed().Should().Equal(
            "=== cur-tenth: For x = 0 To 1 Step 0.1",
            "  pass 1: Currency 0",
            "  pass 2: Currency 0.1",
            "  pass 3: Currency 0.2",
            "  pass 4: Currency 0.3",
            "  pass 5: Currency 0.4",
            "  pass 6: Currency 0.5",
            "  pass 7: Currency 0.6",
            "  pass 8: Currency 0.7",
            "  pass 9: Currency 0.8",
            "  pass 10: Currency 0.9",
            "  pass 11: Currency 1",
            "  after: Currency 1.1 passes 11 err 0");
    }

    [Fact]
    public async Task A_Currency_counter__For_x_0_To_1_Step_1_3()
    {
        await RunCase("""
            Sub cur_third()
                On Error Resume Next
                Dim n As Long
                Dim x As Currency
                L "=== cur-third: For x = 0 To 1 Step 1 / 3"
                For x = 0 To 1 Step 1 / 3
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            cur_third
            """);
        Printed().Should().Equal(
            "=== cur-third: For x = 0 To 1 Step 1 / 3",
            "  pass 1: Currency 0",
            "  pass 2: Currency 0.3333",
            "  pass 3: Currency 0.6666",
            "  pass 4: Currency 0.9999",
            "  after: Currency 1.3332 passes 4 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_0_To_1_Step_0_1()
    {
        await RunCase("""
            Sub var_tenth()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-tenth: For x = 0 To 1 Step 0.1"
                For x = 0 To 1 Step 0.1
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_tenth
            """);
        Printed().Should().Equal(
            "=== var-tenth: For x = 0 To 1 Step 0.1",
            "  pass 1: Double 0",
            "  pass 2: Double 0.1",
            "  pass 3: Double 0.2",
            "  pass 4: Double 0.3",
            "  pass 5: Double 0.4",
            "  pass 6: Double 0.5",
            "  pass 7: Double 0.6",
            "  pass 8: Double 0.7",
            "  pass 9: Double 0.8",
            "  pass 10: Double 0.9",
            "  pass 11: Double 1",
            "  after: Double 1.1 passes 11 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_1_To_2_Step_0_5()
    {
        await RunCase("""
            Sub var_half()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-half: For x = 1 To 2 Step 0.5"
                For x = 1 To 2 Step 0.5
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_half
            """);
        Printed().Should().Equal(
            "=== var-half: For x = 1 To 2 Step 0.5",
            "  pass 1: Double 1",
            "  pass 2: Double 1.5",
            "  pass 3: Double 2",
            "  after: Double 2.5 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_0_5_To_3()
    {
        await RunCase("""
            Sub var_halfstart()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-halfstart: For x = 0.5 To 3"
                For x = 0.5 To 3
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_halfstart
            """);
        Printed().Should().Equal(
            "=== var-halfstart: For x = 0.5 To 3",
            "  pass 1: Double 0.5",
            "  pass 2: Double 1.5",
            "  pass 3: Double 2.5",
            "  after: Double 3.5 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_1_To_2_5()
    {
        await RunCase("""
            Sub var_halflimit()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-halflimit: For x = 1 To 2.5"
                For x = 1 To 2.5
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_halflimit
            """);
        Printed().Should().Equal(
            "=== var-halflimit: For x = 1 To 2.5",
            "  pass 1: Double 1",
            "  pass 2: Double 2",
            "  after: Double 3 passes 2 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_0_To_1_Step_0_25()
    {
        await RunCase("""
            Sub var_curstep()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-curstep: For x = 0 To 1 Step 0.25@"
                For x = 0 To 1 Step 0.25@
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_curstep
            """);
        Printed().Should().Equal(
            "=== var-curstep: For x = 0 To 1 Step 0.25@",
            "  pass 1: Currency 0",
            "  pass 2: Currency 0.25",
            "  pass 3: Currency 0.5",
            "  pass 4: Currency 0.75",
            "  pass 5: Currency 1",
            "  after: Currency 1.25 passes 5 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_0_To_1_Step_0_25__var_sngstep()
    {
        await RunCase("""
            Sub var_sngstep()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-sngstep: For x = 0 To 1 Step 0.25!"
                For x = 0 To 1 Step 0.25!
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_sngstep
            """);
        Printed().Should().Equal(
            "=== var-sngstep: For x = 0 To 1 Step 0.25!",
            "  pass 1: Single 0",
            "  pass 2: Single 0.25",
            "  pass 3: Single 0.5",
            "  pass 4: Single 0.75",
            "  pass 5: Single 1",
            "  after: Single 1.25 passes 5 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_0_To_1_Step_CDec_0_1()
    {
        await RunCase("""
            Sub var_decstep()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-decstep: For x = 0 To 1 Step CDec(0.1)"
                For x = 0 To 1 Step CDec(0.1)
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_decstep
            """);
        Printed().Should().Equal(
            "=== var-decstep: For x = 0 To 1 Step CDec(0.1)",
            "  pass 1: Decimal 0",
            "  pass 2: Decimal 0.1",
            "  pass 3: Decimal 0.2",
            "  pass 4: Decimal 0.3",
            "  pass 5: Decimal 0.4",
            "  pass 6: Decimal 0.5",
            "  pass 7: Decimal 0.6",
            "  pass 8: Decimal 0.7",
            "  pass 9: Decimal 0.8",
            "  pass 10: Decimal 0.9",
            "  pass 11: Decimal 1",
            "  after: Decimal 1.1 passes 11 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_1_To_0_Step_0_5()
    {
        await RunCase("""
            Sub var_down()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-down: For x = 1 To 0 Step -0.5"
                For x = 1 To 0 Step -0.5
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_down
            """);
        Printed().Should().Equal(
            "=== var-down: For x = 1 To 0 Step -0.5",
            "  pass 1: Double 1",
            "  pass 2: Double 0.5",
            "  pass 3: Double 0",
            "  after: Double -0.5 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_1_To_3()
    {
        await RunCase("""
            Sub var_integral()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-integral: For x = 1 To 3"
                For x = 1 To 3
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_integral
            """);
        Printed().Should().Equal(
            "=== var-integral: For x = 1 To 3",
            "  pass 1: Integer 1",
            "  pass 2: Integer 2",
            "  pass 3: Integer 3",
            "  after: Integer 4 passes 3 err 0");
    }

    [Fact]
    public async Task A_Undeclared_counter__For_y_0_To_1_Step_0_25()
    {
        await RunCase("""
            Sub undeclared_tenth()
                On Error Resume Next
                Dim n As Long
                L "=== undeclared-tenth: For y = 0 To 1 Step 0.25"
                For y = 0 To 1 Step 0.25
                    n = n + 1
                    L "  pass " & n & ": " & Tv(y)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(y) & " passes " & n & " err " & Err.Number
            End Sub

            undeclared_tenth
            """);
        Printed().Should().Equal(
            "=== undeclared-tenth: For y = 0 To 1 Step 0.25",
            "  pass 1: Double 0",
            "  pass 2: Double 0.25",
            "  pass 3: Double 0.5",
            "  pass 4: Double 0.75",
            "  pass 5: Double 1",
            "  after: Double 1.25 passes 5 err 0");
    }

    [Fact]
    public async Task A_Date_counter__For_x_1_1_2000_To_1_2_2000_Step_0_25()
    {
        await RunCase("""
            Sub date_quarter()
                On Error Resume Next
                Dim n As Long
                Dim x As Date
                L "=== date-quarter: For x = #1/1/2000# To #1/2/2000# Step 0.25"
                For x = #1/1/2000# To #1/2/2000# Step 0.25
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            date_quarter
            """);
        Printed().Should().Equal(
            "=== date-quarter: For x = #1/1/2000# To #1/2/2000# Step 0.25",
            "  pass 1: Date 01/01/2000",
            "  pass 2: Date 01/01/2000 06:00:00",
            "  pass 3: Date 01/01/2000 12:00:00",
            "  pass 4: Date 01/01/2000 18:00:00",
            "  pass 5: Date 02/01/2000",
            "  after: Date 02/01/2000 06:00:00 passes 5 err 0");
    }

    [Fact]
    public async Task A_Date_counter__For_x_1_1_2000_To_1_3_2000()
    {
        await RunCase("""
            Sub date_days()
                On Error Resume Next
                Dim n As Long
                Dim x As Date
                L "=== date-days: For x = #1/1/2000# To #1/3/2000#"
                For x = #1/1/2000# To #1/3/2000#
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            date_days
            """);
        Printed().Should().Equal(
            "=== date-days: For x = #1/1/2000# To #1/3/2000#",
            "  pass 1: Date 01/01/2000",
            "  pass 2: Date 02/01/2000",
            "  pass 3: Date 03/01/2000",
            "  after: Date 04/01/2000 passes 3 err 0");
    }

    [Fact]
    public async Task A_Date_counter__For_x_1_To_2_Step_0_5()
    {
        await RunCase("""
            Sub date_halfday_num()
                On Error Resume Next
                Dim n As Long
                Dim x As Date
                L "=== date-halfday-num: For x = 1 To 2 Step 0.5"
                For x = 1 To 2 Step 0.5
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            date_halfday_num
            """);
        Printed().Should().Equal(
            "=== date-halfday-num: For x = 1 To 2 Step 0.5",
            "  pass 1: Date 31/12/1899",
            "  pass 2: Date 31/12/1899 12:00:00",
            "  pass 3: Date 01/01/1900",
            "  after: Date 01/01/1900 12:00:00 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_1_1_2000_To_1_2_2000_Step_0_5()
    {
        await RunCase("""
            Sub var_date()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-date: For x = #1/1/2000# To #1/2/2000# Step 0.5"
                For x = #1/1/2000# To #1/2/2000# Step 0.5
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_date
            """);
        Printed().Should().Equal(
            "=== var-date: For x = #1/1/2000# To #1/2/2000# Step 0.5",
            "  pass 1: Date 01/01/2000",
            "  pass 2: Date 01/01/2000 12:00:00",
            "  pass 3: Date 02/01/2000",
            "  after: Date 02/01/2000 12:00:00 passes 3 err 0");
    }

    [Fact]
    public async Task A_Integer_counter__For_x_1_To_3_Step_1_5()
    {
        await RunCase("""
            Sub int_halfstep()
                On Error Resume Next
                Dim n As Long
                Dim x As Integer
                L "=== int-halfstep: For x = 1 To 3 Step 1.5"
                For x = 1 To 3 Step 1.5
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            int_halfstep
            """);
        Printed().Should().Equal(
            "=== int-halfstep: For x = 1 To 3 Step 1.5",
            "  pass 1: Integer 1",
            "  pass 2: Integer 3",
            "  after: Integer 5 passes 2 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_0_To_1_Step_0_25__var_long_sng()
    {
        await RunCase("""
            Sub var_long_sng()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-long-sng: For x = 0& To 1 Step 0.25!"
                For x = 0& To 1 Step 0.25!
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_long_sng
            """);
        Printed().Should().Equal(
            "=== var-long-sng: For x = 0& To 1 Step 0.25!",
            "  pass 1: Double 0",
            "  pass 2: Double 0.25",
            "  pass 3: Double 0.5",
            "  pass 4: Double 0.75",
            "  pass 5: Double 1",
            "  after: Double 1.25 passes 5 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_0_To_40000_Step_10000_5()
    {
        await RunCase("""
            Sub var_int_sng_long()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-int-sng-long: For x = 0 To 40000 Step 10000.5!"
                For x = 0 To 40000 Step 10000.5!
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_int_sng_long
            """);
        Printed().Should().Equal(
            "=== var-int-sng-long: For x = 0 To 40000 Step 10000.5!",
            "  pass 1: Double 0",
            "  pass 2: Double 10000.5",
            "  pass 3: Double 20001",
            "  pass 4: Double 30001.5",
            "  after: Double 40002 passes 4 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_0_To_1_Step_0_25__var_cur_dbl()
    {
        await RunCase("""
            Sub var_cur_dbl()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-cur-dbl: For x = 0@ To 1 Step 0.25"
                For x = 0@ To 1 Step 0.25
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_cur_dbl
            """);
        Printed().Should().Equal(
            "=== var-cur-dbl: For x = 0@ To 1 Step 0.25",
            "  pass 1: Currency 0",
            "  pass 2: Currency 0.25",
            "  pass 3: Currency 0.5",
            "  pass 4: Currency 0.75",
            "  pass 5: Currency 1",
            "  after: Currency 1.25 passes 5 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_0_To_1_Step_0_25__var_limit_cur()
    {
        await RunCase("""
            Sub var_limit_cur()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-limit-cur: For x = 0 To 1@ Step 0.25"
                For x = 0 To 1@ Step 0.25
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_limit_cur
            """);
        Printed().Should().Equal(
            "=== var-limit-cur: For x = 0 To 1@ Step 0.25",
            "  pass 1: Currency 0",
            "  pass 2: Currency 0.25",
            "  pass 3: Currency 0.5",
            "  pass 4: Currency 0.75",
            "  pass 5: Currency 1",
            "  after: Currency 1.25 passes 5 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_1_To_3__var_dbl_start_int()
    {
        await RunCase("""
            Sub var_dbl_start_int()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-dbl-start-int: For x = 1# To 3"
                For x = 1# To 3
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_dbl_start_int
            """);
        Printed().Should().Equal(
            "=== var-dbl-start-int: For x = 1# To 3",
            "  pass 1: Double 1",
            "  pass 2: Double 2",
            "  pass 3: Double 3",
            "  after: Double 4 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_49998_To_50000()
    {
        await RunCase("""
            Sub var_to_50000()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-to-50000: For x = 49998 To 50000"
                For x = 49998 To 50000
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_to_50000
            """);
        Printed().Should().Equal(
            "=== var-to-50000: For x = 49998 To 50000",
            "  pass 1: Long 49998",
            "  pass 2: Long 49999",
            "  pass 3: Long 50000",
            "  after: Long 50001 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_1_To_50000_Step_24999()
    {
        await RunCase("""
            Sub var_1_to_50000()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-1-to-50000: For x = 1 To 50000 Step 24999"
                For x = 1 To 50000 Step 24999
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_1_to_50000
            """);
        Printed().Should().Equal(
            "=== var-1-to-50000: For x = 1 To 50000 Step 24999",
            "  pass 1: Long 1",
            "  pass 2: Long 25000",
            "  pass 3: Long 49999",
            "  after: Long 74998 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_32766_To_32767()
    {
        await RunCase("""
            Sub var_int_edge()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-int-edge: For x = 32766 To 32767"
                For x = 32766 To 32767
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_int_edge
            """);
        Printed().Should().Equal(
            "=== var-int-edge: For x = 32766 To 32767",
            "  pass 1: Integer 32766",
            "  pass 2: Integer 32767",
            "  after: Long 32768 passes 2 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_CByte_1_To_CByte_3()
    {
        // Native code differs here; HexIDE gives p-code's answer, as the VB6 IDE does.
        await RunCase("""
            Sub var_byte()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-byte: For x = CByte(1) To CByte(3)"
                For x = CByte(1) To CByte(3)
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_byte
            """);
        Printed().Should().Equal(
            "=== var-byte: For x = CByte(1) To CByte(3)",
            "  pass 1: Byte 1",
            "  pass 2: Byte 2",
            "  pass 3: Byte 3",
            "  after: Byte 4 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_1_To_2_Step_0_5__var_strings()
    {
        await RunCase(""""
            Sub var_strings()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-strings: For x = ""1"" To ""2"" Step ""0.5"""
                For x = "1" To "2" Step "0.5"
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_strings
            """");
        Printed().Should().Equal(
            "=== var-strings: For x = \"1\" To \"2\" Step \"0.5\"",
            "  pass 1: Double 1",
            "  pass 2: Double 1.5",
            "  pass 3: Double 2",
            "  after: Double 2.5 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_True_To_1()
    {
        await RunCase("""
            Sub var_bool()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-bool: For x = True To 1"
                For x = True To 1
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_bool
            """);
        Printed().Should().Equal(
            "=== var-bool: For x = True To 1",
            "  pass 1: Integer -1",
            "  pass 2: Integer 0",
            "  pass 3: Integer 1",
            "  after: Integer 2 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_1_1_2000_To_1_3_2000()
    {
        await RunCase("""
            Sub var_date_int()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-date-int: For x = #1/1/2000# To #1/3/2000#"
                For x = #1/1/2000# To #1/3/2000#
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_date_int
            """);
        Printed().Should().Equal(
            "=== var-date-int: For x = #1/1/2000# To #1/3/2000#",
            "  pass 1: Date 01/01/2000",
            "  pass 2: Date 02/01/2000",
            "  pass 3: Date 03/01/2000",
            "  after: Date 04/01/2000 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_36526_To_1_3_2000()
    {
        await RunCase("""
            Sub var_int_date_limit()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-int-date-limit: For x = 36526 To #1/3/2000#"
                For x = 36526 To #1/3/2000#
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_int_date_limit
            """);
        Printed().Should().Equal(
            "=== var-int-date-limit: For x = 36526 To #1/3/2000#",
            "  pass 1: Date 01/01/2000",
            "  pass 2: Date 02/01/2000",
            "  pass 3: Date 03/01/2000",
            "  after: Date 04/01/2000 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_1_1_2000_To_1_2_2000_Step_0_5__var_date_cur()
    {
        await RunCase("""
            Sub var_date_cur()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-date-cur: For x = #1/1/2000# To #1/2/2000# Step 0.5@"
                For x = #1/1/2000# To #1/2/2000# Step 0.5@
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_date_cur
            """);
        Printed().Should().Equal(
            "=== var-date-cur: For x = #1/1/2000# To #1/2/2000# Step 0.5@",
            "  pass 1: Date 01/01/2000",
            "  pass 2: Date 01/01/2000 12:00:00",
            "  pass 3: Date 02/01/2000",
            "  after: Date 02/01/2000 12:00:00 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_CDec_0_5_To_2()
    {
        // P-code counts this in Dates, a defect HexIDE does not copy: native code counts Decimals, and so does HexIDE.
        await RunCase("""
            Sub var_dec_start()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-dec-start: For x = CDec(0.5) To 2"
                For x = CDec(0.5) To 2
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_dec_start
            """);
        Printed().Should().Equal(
            "=== var-dec-start: For x = CDec(0.5) To 2",
            "  pass 1: Decimal 0.5",
            "  pass 2: Decimal 1.5",
            "  after: Decimal 2.5 passes 2 err 0");
    }

    [Fact]
    public async Task A_Single_counter__For_x_0_1_To_0_5_Step_0_1()
    {
        // Native code differs here; HexIDE gives p-code's answer, as the VB6 IDE does.
        await RunCase("""
            Sub sng_tenth_from()
                On Error Resume Next
                Dim n As Long
                Dim x As Single
                L "=== sng-tenth-from: For x = 0.1 To 0.5 Step 0.1"
                For x = 0.1 To 0.5 Step 0.1
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            sng_tenth_from
            """);
        Printed().Should().Equal(
            "=== sng-tenth-from: For x = 0.1 To 0.5 Step 0.1",
            "  pass 1: Single 0.1",
            "  pass 2: Single 0.2",
            "  pass 3: Single 0.3",
            "  pass 4: Single 0.4",
            "  after: Single 0.5 passes 4 err 0");
    }

    [Fact]
    public async Task A_Double_counter__For_x_0_5_To_1_Step_0()
    {
        await RunCase("""
            Sub dbl_step0()
                On Error Resume Next
                Dim n As Long
                Dim x As Double
                L "=== dbl-step0: For x = 0.5 To 1 Step 0"
                For x = 0.5 To 1 Step 0
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            dbl_step0
            """);
        Printed().Should().Equal(
            "=== dbl-step0: For x = 0.5 To 1 Step 0",
            "  pass 1: Double 0.5",
            "  pass 2: Double 0.5",
            "  pass 3: Double 0.5",
            "  pass 4: Double 0.5",
            "  pass 5: Double 0.5",
            "  pass 6: Double 0.5",
            "  pass 7: Double 0.5",
            "  pass 8: Double 0.5",
            "  pass 9: Double 0.5",
            "  pass 10: Double 0.5",
            "  pass 11: Double 0.5",
            "  pass 12: Double 0.5",
            "  pass 13: Double 0.5",
            "  pass 14: Double 0.5",
            "  pass 15: Double 0.5",
            "  after: Double 0.5 passes 15 err 0");
    }

    [Fact]
    public async Task A_Double_counter__For_x_1E_15_To_1E_15_2_Step_0_5()
    {
        await RunCase("""
            Sub dbl_big_step()
                On Error Resume Next
                Dim n As Long
                Dim x As Double
                L "=== dbl-big-step: For x = 1E+15 To 1E+15 + 2 Step 0.5"
                For x = 1E+15 To 1E+15 + 2 Step 0.5
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            dbl_big_step
            """);
        Printed().Should().Equal(
            "=== dbl-big-step: For x = 1E+15 To 1E+15 + 2 Step 0.5",
            "  pass 1: Double 1E+15",
            "  pass 2: Double 1E+15",
            "  pass 3: Double 1E+15",
            "  pass 4: Double 1E+15",
            "  pass 5: Double 1E+15",
            "  after: Double 1E+15 passes 5 err 0");
    }

    [Fact]
    public async Task A_Long_counter__For_x_1_To_4_Step_0_5()
    {
        await RunCase("""
            Sub lng_halfstep()
                On Error Resume Next
                Dim n As Long
                Dim x As Long
                L "=== lng-halfstep: For x = 1 To 4 Step 0.5"
                For x = 1 To 4 Step 0.5
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            lng_halfstep
            """);
        Printed().Should().Equal(
            "=== lng-halfstep: For x = 1 To 4 Step 0.5",
            "  pass 1: Long 1",
            "  pass 2: Long 1",
            "  pass 3: Long 1",
            "  pass 4: Long 1",
            "  pass 5: Long 1",
            "  pass 6: Long 1",
            "  pass 7: Long 1",
            "  pass 8: Long 1",
            "  pass 9: Long 1",
            "  pass 10: Long 1",
            "  pass 11: Long 1",
            "  pass 12: Long 1",
            "  pass 13: Long 1",
            "  pass 14: Long 1",
            "  pass 15: Long 1",
            "  after: Long 1 passes 15 err 0");
    }

    [Fact]
    public async Task A_Byte_counter__For_x_1_To_3_Step_1_5()
    {
        await RunCase("""
            Sub byte_frac()
                On Error Resume Next
                Dim n As Long
                Dim x As Byte
                L "=== byte-frac: For x = 1 To 3 Step 1.5"
                For x = 1 To 3 Step 1.5
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            byte_frac
            """);
        Printed().Should().Equal(
            "=== byte-frac: For x = 1 To 3 Step 1.5",
            "  pass 1: Byte 1",
            "  pass 2: Byte 3",
            "  after: Byte 5 passes 2 err 0");
    }

    [Fact]
    public async Task A_Double_counter__For_x_0_To_Sqr_2_Step_0_5()
    {
        await RunCase("""
            Sub dbl_limit_fn()
                On Error Resume Next
                Dim n As Long
                Dim x As Double
                L "=== dbl-limit-fn: For x = 0 To Sqr(2) Step 0.5"
                For x = 0 To Sqr(2) Step 0.5
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            dbl_limit_fn
            """);
        Printed().Should().Equal(
            "=== dbl-limit-fn: For x = 0 To Sqr(2) Step 0.5",
            "  pass 1: Double 0",
            "  pass 2: Double 0.5",
            "  pass 3: Double 1",
            "  after: Double 1.5 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_0_5_To_0_5_Step_0_5()
    {
        await RunCase("""
            Sub var_neg_start()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== var-neg-start: For x = -0.5 To 0.5 Step 0.5"
                For x = -0.5 To 0.5 Step 0.5
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            var_neg_start
            """);
        Printed().Should().Equal(
            "=== var-neg-start: For x = -0.5 To 0.5 Step 0.5",
            "  pass 1: Double -0.5",
            "  pass 2: Double 0",
            "  pass 3: Double 0.5",
            "  after: Double 1 passes 3 err 0");
    }

    [Fact]
    public async Task A_Single_counter__For_x_0_To_0_3_Step_0_1()
    {
        await RunCase("""
            Sub sng_a()
                On Error Resume Next
                Dim n As Long
                Dim x As Single
                L "=== sng-a: For x = 0 To 0.3 Step 0.1"
                For x = 0 To 0.3 Step 0.1
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            sng_a
            """);
        Printed().Should().Equal(
            "=== sng-a: For x = 0 To 0.3 Step 0.1",
            "  pass 1: Single 0",
            "  pass 2: Single 0.1",
            "  pass 3: Single 0.2",
            "  pass 4: Single 0.3",
            "  after: Single 0.4 passes 4 err 0");
    }

    [Fact]
    public async Task A_Single_counter__For_x_0_To_0_3_Step_0_3()
    {
        await RunCase("""
            Sub sng_b()
                On Error Resume Next
                Dim n As Long
                Dim x As Single
                L "=== sng-b: For x = 0 To 0.3 Step 0.3"
                For x = 0 To 0.3 Step 0.3
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            sng_b
            """);
        Printed().Should().Equal(
            "=== sng-b: For x = 0 To 0.3 Step 0.3",
            "  pass 1: Single 0",
            "  pass 2: Single 0.3",
            "  after: Single 0.6 passes 2 err 0");
    }

    [Fact]
    public async Task A_Single_counter__For_x_0_To_0_9_Step_0_15()
    {
        // Native code differs here; HexIDE gives p-code's answer, as the VB6 IDE does.
        await RunCase("""
            Sub sng_c()
                On Error Resume Next
                Dim n As Long
                Dim x As Single
                L "=== sng-c: For x = 0 To 0.9 Step 0.15"
                For x = 0 To 0.9 Step 0.15
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            sng_c
            """);
        Printed().Should().Equal(
            "=== sng-c: For x = 0 To 0.9 Step 0.15",
            "  pass 1: Single 0",
            "  pass 2: Single 0.15",
            "  pass 3: Single 0.3",
            "  pass 4: Single 0.45",
            "  pass 5: Single 0.6",
            "  pass 6: Single 0.75",
            "  after: Single 0.9 passes 6 err 0");
    }

    [Fact]
    public async Task A_Single_counter__For_x_0_To_0_7_Step_0_7()
    {
        await RunCase("""
            Sub sng_d()
                On Error Resume Next
                Dim n As Long
                Dim x As Single
                L "=== sng-d: For x = 0 To 0.7 Step 0.7"
                For x = 0 To 0.7 Step 0.7
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            sng_d
            """);
        Printed().Should().Equal(
            "=== sng-d: For x = 0 To 0.7 Step 0.7",
            "  pass 1: Single 0",
            "  pass 2: Single 0.7",
            "  after: Single 1.4 passes 2 err 0");
    }

    [Fact]
    public async Task A_Single_counter__For_x_0_1_To_0_7_Step_0_3()
    {
        await RunCase("""
            Sub sng_e()
                On Error Resume Next
                Dim n As Long
                Dim x As Single
                L "=== sng-e: For x = 0.1 To 0.7 Step 0.3"
                For x = 0.1 To 0.7 Step 0.3
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            sng_e
            """);
        Printed().Should().Equal(
            "=== sng-e: For x = 0.1 To 0.7 Step 0.3",
            "  pass 1: Single 0.1",
            "  pass 2: Single 0.4",
            "  after: Single 0.7 passes 2 err 0");
    }

    [Fact]
    public async Task A_Single_counter__For_x_0_1_To_0_9_Step_0_2()
    {
        // Native code differs here; HexIDE gives p-code's answer, as the VB6 IDE does.
        await RunCase("""
            Sub sng_f()
                On Error Resume Next
                Dim n As Long
                Dim x As Single
                L "=== sng-f: For x = 0.1 To 0.9 Step 0.2"
                For x = 0.1 To 0.9 Step 0.2
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            sng_f
            """);
        Printed().Should().Equal(
            "=== sng-f: For x = 0.1 To 0.9 Step 0.2",
            "  pass 1: Single 0.1",
            "  pass 2: Single 0.3",
            "  pass 3: Single 0.5",
            "  pass 4: Single 0.7",
            "  after: Single 0.9 passes 4 err 0");
    }

    [Fact]
    public async Task A_Single_counter__For_x_0_1_To_0_7_Step_0_15()
    {
        await RunCase("""
            Sub sng_g()
                On Error Resume Next
                Dim n As Long
                Dim x As Single
                L "=== sng-g: For x = 0.1 To 0.7 Step 0.15"
                For x = 0.1 To 0.7 Step 0.15
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            sng_g
            """);
        Printed().Should().Equal(
            "=== sng-g: For x = 0.1 To 0.7 Step 0.15",
            "  pass 1: Single 0.1",
            "  pass 2: Single 0.25",
            "  pass 3: Single 0.4",
            "  pass 4: Single 0.55",
            "  after: Single 0.7 passes 4 err 0");
    }

    [Fact]
    public async Task A_Single_counter__For_x_0_To_0_3_Step_0_05()
    {
        await RunCase("""
            Sub sng_h()
                On Error Resume Next
                Dim n As Long
                Dim x As Single
                L "=== sng-h: For x = 0 To 0.3 Step 0.05"
                For x = 0 To 0.3 Step 0.05
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            sng_h
            """);
        Printed().Should().Equal(
            "=== sng-h: For x = 0 To 0.3 Step 0.05",
            "  pass 1: Single 0",
            "  pass 2: Single 0.05",
            "  pass 3: Single 0.1",
            "  pass 4: Single 0.15",
            "  pass 5: Single 0.2",
            "  pass 6: Single 0.25",
            "  pass 7: Single 0.3",
            "  after: Single 0.35 passes 7 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_0_To_0_3_Step_0_1()
    {
        await RunCase("""
            Sub vsng_a()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== vsng-a: For x = 0! To 0.3! Step 0.1!"
                For x = 0! To 0.3! Step 0.1!
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            vsng_a
            """);
        Printed().Should().Equal(
            "=== vsng-a: For x = 0! To 0.3! Step 0.1!",
            "  pass 1: Single 0",
            "  pass 2: Single 0.1",
            "  pass 3: Single 0.2",
            "  pass 4: Single 0.3",
            "  after: Single 0.4 passes 4 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_0_1_To_0_5_Step_0_1()
    {
        // Native code differs here; HexIDE gives p-code's answer, as the VB6 IDE does.
        await RunCase("""
            Sub vsng_b()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== vsng-b: For x = 0.1! To 0.5! Step 0.1!"
                For x = 0.1! To 0.5! Step 0.1!
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            vsng_b
            """);
        Printed().Should().Equal(
            "=== vsng-b: For x = 0.1! To 0.5! Step 0.1!",
            "  pass 1: Single 0.1",
            "  pass 2: Single 0.2",
            "  pass 3: Single 0.3",
            "  pass 4: Single 0.4",
            "  after: Single 0.5 passes 4 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_0_To_0_9_Step_0_15()
    {
        // Native code differs here; HexIDE gives p-code's answer, as the VB6 IDE does.
        await RunCase("""
            Sub vsng_c()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== vsng-c: For x = 0! To 0.9! Step 0.15!"
                For x = 0! To 0.9! Step 0.15!
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            vsng_c
            """);
        Printed().Should().Equal(
            "=== vsng-c: For x = 0! To 0.9! Step 0.15!",
            "  pass 1: Single 0",
            "  pass 2: Single 0.15",
            "  pass 3: Single 0.3",
            "  pass 4: Single 0.45",
            "  pass 5: Single 0.6",
            "  pass 6: Single 0.75",
            "  after: Single 0.9 passes 6 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_CDec_0_5_To_2_Step_1()
    {
        await RunCase("""
            Sub vdec_step()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== vdec-step: For x = CDec(0.5) To 2 Step 1"
                For x = CDec(0.5) To 2 Step 1
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            vdec_step
            """);
        Printed().Should().Equal(
            "=== vdec-step: For x = CDec(0.5) To 2 Step 1",
            "  pass 1: Decimal 0.5",
            "  pass 2: Decimal 1.5",
            "  after: Decimal 2.5 passes 2 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_CDec_0_To_2()
    {
        // P-code counts this in Dates, a defect HexIDE does not copy: native code counts Decimals, and so does HexIDE.
        await RunCase("""
            Sub vdec_int()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== vdec-int: For x = CDec(0) To 2"
                For x = CDec(0) To 2
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            vdec_int
            """);
        Printed().Should().Equal(
            "=== vdec-int: For x = CDec(0) To 2",
            "  pass 1: Decimal 0",
            "  pass 2: Decimal 1",
            "  pass 3: Decimal 2",
            "  after: Decimal 3 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_0_To_CDec_2()
    {
        await RunCase("""
            Sub vdec_limit()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== vdec-limit: For x = 0 To CDec(2)"
                For x = 0 To CDec(2)
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            vdec_limit
            """);
        Printed().Should().Equal(
            "=== vdec-limit: For x = 0 To CDec(2)",
            "  pass 1: Decimal 0",
            "  pass 2: Decimal 1",
            "  pass 3: Decimal 2",
            "  after: Decimal 3 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_0_To_2_Step_CDec_1()
    {
        await RunCase("""
            Sub vdec_stepdec()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== vdec-stepdec: For x = 0 To 2 Step CDec(1)"
                For x = 0 To 2 Step CDec(1)
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            vdec_stepdec
            """);
        Printed().Should().Equal(
            "=== vdec-stepdec: For x = 0 To 2 Step CDec(1)",
            "  pass 1: Decimal 0",
            "  pass 2: Decimal 1",
            "  pass 3: Decimal 2",
            "  after: Decimal 3 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_CDec_0_5_To_CDec_2_Step_CDec_1()
    {
        await RunCase("""
            Sub vdec_half()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== vdec-half: For x = CDec(0.5) To CDec(2) Step CDec(1)"
                For x = CDec(0.5) To CDec(2) Step CDec(1)
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            vdec_half
            """);
        Printed().Should().Equal(
            "=== vdec-half: For x = CDec(0.5) To CDec(2) Step CDec(1)",
            "  pass 1: Decimal 0.5",
            "  pass 2: Decimal 1.5",
            "  after: Decimal 2.5 passes 2 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_CByte_1_To_3()
    {
        await RunCase("""
            Sub vbyte_intlimit()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== vbyte-intlimit: For x = CByte(1) To 3"
                For x = CByte(1) To 3
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            vbyte_intlimit
            """);
        Printed().Should().Equal(
            "=== vbyte-intlimit: For x = CByte(1) To 3",
            "  pass 1: Integer 1",
            "  pass 2: Integer 2",
            "  pass 3: Integer 3",
            "  after: Integer 4 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_CByte_254_To_CByte_255()
    {
        // Native code differs here; HexIDE gives p-code's answer, as the VB6 IDE does.
        await RunCase("""
            Sub vbyte_edge()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== vbyte-edge: For x = CByte(254) To CByte(255)"
                For x = CByte(254) To CByte(255)
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            vbyte_edge
            """);
        Printed().Should().Equal(
            "=== vbyte-edge: For x = CByte(254) To CByte(255)",
            "  pass 1: Byte 254",
            "  pass 2: Byte 255",
            "  after: Integer 256 passes 2 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_CByte_1_To_CByte_5_Step_CByte_2()
    {
        await RunCase("""
            Sub vbyte_step()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== vbyte-step: For x = CByte(1) To CByte(5) Step CByte(2)"
                For x = CByte(1) To CByte(5) Step CByte(2)
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            vbyte_step
            """);
        Printed().Should().Equal(
            "=== vbyte-step: For x = CByte(1) To CByte(5) Step CByte(2)",
            "  pass 1: Byte 1",
            "  pass 2: Byte 3",
            "  pass 3: Byte 5",
            "  after: Byte 7 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_CInt_1_To_CInt_3_Step_CInt_1()
    {
        await RunCase("""
            Sub vint_intstep()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== vint-intstep: For x = CInt(1) To CInt(3) Step CInt(1)"
                For x = CInt(1) To CInt(3) Step CInt(1)
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            vint_intstep
            """);
        Printed().Should().Equal(
            "=== vint-intstep: For x = CInt(1) To CInt(3) Step CInt(1)",
            "  pass 1: Integer 1",
            "  pass 2: Integer 2",
            "  pass 3: Integer 3",
            "  after: Integer 4 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_1_To_3__vlng()
    {
        await RunCase("""
            Sub vlng()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== vlng: For x = 1& To 3&"
                For x = 1& To 3&
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            vlng
            """);
        Printed().Should().Equal(
            "=== vlng: For x = 1& To 3&",
            "  pass 1: Long 1",
            "  pass 2: Long 2",
            "  pass 3: Long 3",
            "  after: Long 4 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_1_To_3__vsng_int()
    {
        await RunCase("""
            Sub vsng_int()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== vsng-int: For x = 1! To 3!"
                For x = 1! To 3!
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            vsng_int
            """);
        Printed().Should().Equal(
            "=== vsng-int: For x = 1! To 3!",
            "  pass 1: Single 1",
            "  pass 2: Single 2",
            "  pass 3: Single 3",
            "  after: Single 4 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_1_To_3__vcur_int()
    {
        await RunCase("""
            Sub vcur_int()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== vcur-int: For x = 1@ To 3@"
                For x = 1@ To 3@
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            vcur_int
            """);
        Printed().Should().Equal(
            "=== vcur-int: For x = 1@ To 3@",
            "  pass 1: Currency 1",
            "  pass 2: Currency 2",
            "  pass 3: Currency 3",
            "  after: Currency 4 passes 3 err 0");
    }

    [Fact]
    public async Task A_Variant_counter__For_x_1_1_2000_To_1_3_2000__vdate_implied()
    {
        await RunCase("""
            Sub vdate_implied()
                On Error Resume Next
                Dim n As Long
                Dim x As Variant
                L "=== vdate-implied: For x = #1/1/2000# To #1/3/2000#"
                For x = #1/1/2000# To #1/3/2000#
                    n = n + 1
                    L "  pass " & n & ": " & Tv(x)
                    If n >= 15 Then Exit For
                Next
                L "  after: " & Tv(x) & " passes " & n & " err " & Err.Number
            End Sub

            vdate_implied
            """);
        Printed().Should().Equal(
            "=== vdate-implied: For x = #1/1/2000# To #1/3/2000#",
            "  pass 1: Date 01/01/2000",
            "  pass 2: Date 02/01/2000",
            "  pass 3: Date 03/01/2000",
            "  after: Date 04/01/2000 passes 3 err 0");
    }

    [Fact]
    public async Task A_raised_limit_with_a_Double_counter__For_x_0_1_To_Lim_k_2_1_Step_0_25_twice()
    {
        await RunCase("""
            Sub c_dbl()
                On Error Resume Next
                Dim x As Double, k As Long, n As Long
                L "=== dbl: Dim x As Double, For x = 0.1 To Lim(k = 2, 1) Step 0.25, twice"
                For k = 1 To 2
                    If k = 2 Then x = 0.1
                    n = 0
                    Err.Clear
                    For x = 0.1 To Lim(k = 2, 1) Step 0.25
                        n = n + 1
                        L "  run " & k & " pass " & n & ": " & Tv(x) & " err " & Err.Number
                        If n >= 12 Then Exit For
                    Next
                    L "  run " & k & " after: " & Tv(x) & " passes " & n & " err " & Err.Number
                Next
            End Sub

            c_dbl
            """);
        Printed().Should().Equal(
            "=== dbl: Dim x As Double, For x = 0.1 To Lim(k = 2, 1) Step 0.25, twice",
            "  run 1 pass 1: Double 0.1 err 0",
            "  run 1 pass 2: Double 0.35 err 0",
            "  run 1 pass 3: Double 0.6 err 0",
            "  run 1 pass 4: Double 0.85 err 0",
            "  run 1 after: Double 1.1 passes 4 err 0",
            "  run 2 pass 1: Double 0.1 err 11",
            "  run 2 pass 2: Double 0.35 err 11",
            "  run 2 pass 3: Double 0.6 err 11",
            "  run 2 pass 4: Double 0.85 err 11",
            "  run 2 after: Double 1.1 passes 4 err 11");
    }

    [Fact]
    public async Task A_raised_limit_with_a_Single_counter__For_x_0_1_To_Lim_k_2_0_5_Step_0_1_twice()
    {
        // Native code differs here; HexIDE gives p-code's answer, as the VB6 IDE does.
        await RunCase("""
            Sub c_sng()
                On Error Resume Next
                Dim x As Single, k As Long, n As Long
                L "=== sng: Dim x As Single, For x = 0.1 To Lim(k = 2, 0.5) Step 0.1, twice"
                For k = 1 To 2
                    If k = 2 Then x = 0.1
                    n = 0
                    Err.Clear
                    For x = 0.1 To Lim(k = 2, 0.5) Step 0.1
                        n = n + 1
                        L "  run " & k & " pass " & n & ": " & Tv(x) & " err " & Err.Number
                        If n >= 12 Then Exit For
                    Next
                    L "  run " & k & " after: " & Tv(x) & " passes " & n & " err " & Err.Number
                Next
            End Sub

            c_sng
            """);
        Printed().Should().Equal(
            "=== sng: Dim x As Single, For x = 0.1 To Lim(k = 2, 0.5) Step 0.1, twice",
            "  run 1 pass 1: Single 0.1 err 0",
            "  run 1 pass 2: Single 0.2 err 0",
            "  run 1 pass 3: Single 0.3 err 0",
            "  run 1 pass 4: Single 0.4 err 0",
            "  run 1 after: Single 0.5 passes 4 err 0",
            "  run 2 pass 1: Single 0.1 err 11",
            "  run 2 pass 2: Single 0.2 err 11",
            "  run 2 pass 3: Single 0.3 err 11",
            "  run 2 pass 4: Single 0.4 err 11",
            "  run 2 after: Single 0.5 passes 4 err 11");
    }

    [Fact]
    public async Task A_raised_limit_with_a_Single_counter__For_x_0_To_Lim_k_2_0_9_Step_0_15_twice()
    {
        // Native code differs here; HexIDE gives p-code's answer, as the VB6 IDE does.
        await RunCase("""
            Sub c_sng2()
                On Error Resume Next
                Dim x As Single, k As Long, n As Long
                L "=== sng2: Dim x As Single, For x = 0 To Lim(k = 2, 0.9) Step 0.15, twice"
                For k = 1 To 2
                    If k = 2 Then x = 0
                    n = 0
                    Err.Clear
                    For x = 0 To Lim(k = 2, 0.9) Step 0.15
                        n = n + 1
                        L "  run " & k & " pass " & n & ": " & Tv(x) & " err " & Err.Number
                        If n >= 12 Then Exit For
                    Next
                    L "  run " & k & " after: " & Tv(x) & " passes " & n & " err " & Err.Number
                Next
            End Sub

            c_sng2
            """);
        Printed().Should().Equal(
            "=== sng2: Dim x As Single, For x = 0 To Lim(k = 2, 0.9) Step 0.15, twice",
            "  run 1 pass 1: Single 0 err 0",
            "  run 1 pass 2: Single 0.15 err 0",
            "  run 1 pass 3: Single 0.3 err 0",
            "  run 1 pass 4: Single 0.45 err 0",
            "  run 1 pass 5: Single 0.6 err 0",
            "  run 1 pass 6: Single 0.75 err 0",
            "  run 1 after: Single 0.9 passes 6 err 0",
            "  run 2 pass 1: Single 0 err 11",
            "  run 2 pass 2: Single 0.15 err 11",
            "  run 2 pass 3: Single 0.3 err 11",
            "  run 2 pass 4: Single 0.45 err 11",
            "  run 2 pass 5: Single 0.6 err 11",
            "  run 2 pass 6: Single 0.75 err 11",
            "  run 2 after: Single 0.9 passes 6 err 11");
    }

    [Fact]
    public async Task A_raised_limit_with_a_Currency_counter__For_x_0_1_To_Lim_k_2_0_5_Step_0_1_twice()
    {
        await RunCase("""
            Sub c_cur()
                On Error Resume Next
                Dim x As Currency, k As Long, n As Long
                L "=== cur: Dim x As Currency, For x = 0.1 To Lim(k = 2, 0.5) Step 0.1, twice"
                For k = 1 To 2
                    If k = 2 Then x = 0.1
                    n = 0
                    Err.Clear
                    For x = 0.1 To Lim(k = 2, 0.5) Step 0.1
                        n = n + 1
                        L "  run " & k & " pass " & n & ": " & Tv(x) & " err " & Err.Number
                        If n >= 12 Then Exit For
                    Next
                    L "  run " & k & " after: " & Tv(x) & " passes " & n & " err " & Err.Number
                Next
            End Sub

            c_cur
            """);
        Printed().Should().Equal(
            "=== cur: Dim x As Currency, For x = 0.1 To Lim(k = 2, 0.5) Step 0.1, twice",
            "  run 1 pass 1: Currency 0.1 err 0",
            "  run 1 pass 2: Currency 0.2 err 0",
            "  run 1 pass 3: Currency 0.3 err 0",
            "  run 1 pass 4: Currency 0.4 err 0",
            "  run 1 pass 5: Currency 0.5 err 0",
            "  run 1 after: Currency 0.6 passes 5 err 0",
            "  run 2 pass 1: Currency 0.1 err 11",
            "  run 2 pass 2: Currency 0.2 err 11",
            "  run 2 pass 3: Currency 0.3 err 11",
            "  run 2 pass 4: Currency 0.4 err 11",
            "  run 2 pass 5: Currency 0.5 err 11",
            "  run 2 after: Currency 0.6 passes 5 err 11");
    }

    [Fact]
    public async Task A_raised_limit_with_a_Date_counter__For_x_0_25_To_Lim_k_2_1_Step_0_25_twice()
    {
        await RunCase("""
            Sub c_date()
                On Error Resume Next
                Dim x As Date, k As Long, n As Long
                L "=== date: Dim x As Date, For x = 0.25 To Lim(k = 2, 1) Step 0.25, twice"
                For k = 1 To 2
                    If k = 2 Then x = 0.25
                    n = 0
                    Err.Clear
                    For x = 0.25 To Lim(k = 2, 1) Step 0.25
                        n = n + 1
                        L "  run " & k & " pass " & n & ": " & Tv(x) & " err " & Err.Number
                        If n >= 12 Then Exit For
                    Next
                    L "  run " & k & " after: " & Tv(x) & " passes " & n & " err " & Err.Number
                Next
            End Sub

            c_date
            """);
        Printed().Should().Equal(
            "=== date: Dim x As Date, For x = 0.25 To Lim(k = 2, 1) Step 0.25, twice",
            "  run 1 pass 1: Date 06:00:00 err 0",
            "  run 1 pass 2: Date 12:00:00 err 0",
            "  run 1 pass 3: Date 18:00:00 err 0",
            "  run 1 pass 4: Date 31/12/1899 err 0",
            "  run 1 after: Date 31/12/1899 06:00:00 passes 4 err 0",
            "  run 2 pass 1: Date 06:00:00 err 11",
            "  run 2 pass 2: Date 12:00:00 err 11",
            "  run 2 pass 3: Date 18:00:00 err 11",
            "  run 2 pass 4: Date 31/12/1899 err 11",
            "  run 2 after: Date 31/12/1899 06:00:00 passes 4 err 11");
    }

    [Fact]
    public async Task A_raised_limit_with_a_Double_counter__For_x_1_To_Lim_k_2_0_Step_0_25_twice()
    {
        await RunCase("""
            Sub c_dbl_down()
                On Error Resume Next
                Dim x As Double, k As Long, n As Long
                L "=== dbl-down: Dim x As Double, For x = 1 To Lim(k = 2, 0) Step -0.25, twice"
                For k = 1 To 2
                    If k = 2 Then x = 1
                    n = 0
                    Err.Clear
                    For x = 1 To Lim(k = 2, 0) Step -0.25
                        n = n + 1
                        L "  run " & k & " pass " & n & ": " & Tv(x) & " err " & Err.Number
                        If n >= 12 Then Exit For
                    Next
                    L "  run " & k & " after: " & Tv(x) & " passes " & n & " err " & Err.Number
                Next
            End Sub

            c_dbl_down
            """);
        Printed().Should().Equal(
            "=== dbl-down: Dim x As Double, For x = 1 To Lim(k = 2, 0) Step -0.25, twice",
            "  run 1 pass 1: Double 1 err 0",
            "  run 1 pass 2: Double 0.75 err 0",
            "  run 1 pass 3: Double 0.5 err 0",
            "  run 1 pass 4: Double 0.25 err 0",
            "  run 1 pass 5: Double 0 err 0",
            "  run 1 after: Double -0.25 passes 5 err 0",
            "  run 2 pass 1: Double 1 err 11",
            "  run 2 pass 2: Double 0.75 err 11",
            "  run 2 pass 3: Double 0.5 err 11",
            "  run 2 pass 4: Double 0.25 err 11",
            "  run 2 pass 5: Double 0 err 11",
            "  run 2 after: Double -0.25 passes 5 err 11");
    }

    [Fact]
    public async Task A_Variant_counter_mixing_a_Date_and_a_Decimal_is_refused_as_unmeasured()
    {
        // A Date beside any other number counts in Dates (measured above); beside a Decimal it was not measured.
        var run = () => RunCase("Sub P()\n    Dim x\n    For x = #1/1/2000# To #1/3/2000# Step CDec(1)\n    Next\nEnd Sub\nP\n");

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*Date and a Decimal*");
    }
}
