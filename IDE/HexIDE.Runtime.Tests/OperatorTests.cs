using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class OperatorTests : BaseVBTestFixture
{
    // Relational comparison of two strings uses VB6's default Option Compare Binary (ORDINAL) — pinned against
    // vb6.exe (gap-audit fix: the operators previously threw / mis-parsed numeric-looking strings numerically).
    [Theory]
    [InlineData("\"a\" < \"b\"", true)]          // ordinal a<b
    [InlineData("\"B\" < \"a\"", true)]          // ORDINAL: 'B'(66) < 'a'(97) — not case-insensitive
    [InlineData("\"10\" < \"9\"", true)]         // STRING compare: '1'<'9' — not numeric (would be False)
    [InlineData("\"abc\" < \"abd\"", true)]
    [InlineData("\"apple\" >= \"apple\"", true)]
    [InlineData("\"b\" > \"a\"", true)]
    [InlineData("\"a\" > \"b\"", false)]
    [InlineData("\"\" < \"a\"", true)]           // empty string is least
    public async Task RelationalComparison_Strings_UseOrdinalBinaryCompare(string expr, bool expected)
    {
        await Run($"Dim r\nr = ({expr})\nDebug.Print r\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, null, null)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    [InlineData(false, null, true)]
    [InlineData(null, true, true)]
    [InlineData(null, false, null)]
    [InlineData(null, null, null)]
    public async Task ImpOperator_ShouldReturnExpectedResult(bool? expression1, bool? expression2, bool? expectedResult)
    {
        string code = $@"
            Dim result
            result = {ConvertToVb6Value(expression1)} IMP {ConvertToVb6Value(expression2)}
            Debug.Print result
        ";

        await Run(code);

        AssertDebugLog([new Vb6Value(expectedResult)]);
    }

    [Fact]
    public async Task ImpOperator_Int_ShouldReturnExpectedResult()
    {
        await Run("Debug.Print 3 Imp 5");
        AssertDebugLog([-3]);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, null, null)]
    [InlineData(false, null, null)]
    [InlineData(null, true, null)]
    [InlineData(null, false, null)]
    [InlineData(null, null, null)]
    public async Task EqvOperator_ShouldReturnExpectedResult(bool? expression1, bool? expression2, bool? expectedResult)
    {
        string code = $@"
            Dim result
            result = {ConvertToVb6Value(expression1)} EQV {ConvertToVb6Value(expression2)}
            Debug.Print result
        ";

        await Run(code);

        AssertDebugLog([new Vb6Value(expectedResult)]);
    }

    [Fact]
    public async Task EqvOperator_Int_ShouldReturnExpectedResult()
    {
        await Run("Debug.Print 3 Eqv 5");
        AssertDebugLog([-7]);
    }

    [Theory]
    [InlineData(true, false)]           // Not True
    [InlineData(false, true)]           // Not False
    [InlineData(null, null)]            // Not Null
    [InlineData(0, -1)]                 // Not 0 (bitwise NOT of 0)
    [InlineData(1, -2)]                 // Not 1 (bitwise NOT of 1)
    [InlineData(255, -256)]             // Not 255 (bitwise NOT of 255)
    public async Task NotOperator_ShouldReturnExpectedResult(object? operand, object? expectedResult)
    {
        string vbOperand = ConvertToVb6Value(operand);
        string code = $@"
            Dim result
            result = Not {vbOperand}
            Debug.Print result
        ";

        await Run(code);

        Vb6Value expectedValue = expectedResult is int i ? new Vb6Value(i) : expectedResult is bool b ? new Vb6Value(b) : expectedResult is null ? Vb6Value.Null : throw new Exception();

        AssertDebugLog([expectedValue]);
    }

    [Theory]
    [InlineData("<", true, false, false, true, false, false)]
    [InlineData(">", false, false, true, false, false, true)]
    [InlineData("<=", true, true, false, true, true, false)]
    [InlineData(">=", false, true, true, false, true, true)]
    [InlineData("=", false, true, false, false, true, false)]
    [InlineData("<>", true, false, true, true, false, true)]
    public async Task ComparisonOperator_ShouldReturnExpectedResults(
        string op,
        bool intLowerResult, bool intEqualResult, bool intGreaterResult,
        bool doubleLowerResult, bool doubleEqualResult, bool doubleGreaterResult)
    {
        string code = $@"
            Dim result1, result2, result3, result4, result5, result6
            result1 = 1 {op} 2            ' int lower
            result2 = 2 {op} 2            ' int equal
            result3 = 3 {op} 2            ' int greater
            result4 = 1.5 {op} 2.5        ' double lower
            result5 = 2.5 {op} 2.5        ' double equal
            result6 = 3.5 {op} 2.5        ' double greater
            Debug.Print result1
            Debug.Print result2
            Debug.Print result3
            Debug.Print result4
            Debug.Print result5
            Debug.Print result6
        ";

        await Run(code);

        AssertDebugLog([
            new Vb6Value(intLowerResult),
            new Vb6Value(intEqualResult),
            new Vb6Value(intGreaterResult),
            new Vb6Value(doubleLowerResult),
            new Vb6Value(doubleEqualResult),
            new Vb6Value(doubleGreaterResult)
        ]);
    }

    [Theory]
    [InlineData("=", true, false, true, false, null, null)]
    [InlineData("<>", false, true, false, true, null, null)]
    public async Task EqualityOperator_ShouldReturnExpectedResults(
        string op,
        bool boolEqualResult, bool boolNotEqualResult,
        bool stringEqualResult, bool stringNotEqualResult,
        bool? nullEqualResult, bool? nullNotEqualResult)
    {
        string code = $@"
            Dim result1, result2, result3, result4, result5, result6
            result1 = True {op} True           ' bool equal
            result2 = True {op} False          ' bool not equal
            result3 = ""hello"" {op} ""hello""     ' string equal
            result4 = ""hello"" {op} ""world""     ' string not equal
            result5 = Null {op} Null           ' null equal
            result6 = Null {op} ""text""         ' null not equal
            Debug.Print result1
            Debug.Print result2
            Debug.Print result3
            Debug.Print result4
            Debug.Print result5
            Debug.Print result6
        ";

        await Run(code);

        AssertDebugLog([
            boolEqualResult,
            boolNotEqualResult,
            stringEqualResult,
            stringNotEqualResult,
            nullEqualResult,
            nullNotEqualResult
        ]);
    }

    [Theory]
    [InlineData(1, 2, 3)]             // int + int
    [InlineData(1, 2.5F, 3.5F)]       // int + float
    [InlineData(1, 2.5D, 3.5D)]       // int + double
    [InlineData(2.5F, 1, 3.5F)]       // float + int
    [InlineData(2.5F, 2.5F, 5.0F)]    // float + float
    [InlineData(2.5F, 2.5D, 5.0D)]    // float + double
    [InlineData(2.5D, 1, 3.5D)]       // double + int
    [InlineData(2.5D, 2.5F, 5.0D)]    // double + float
    [InlineData(2.5D, 2.5D, 5.0D)]    // double + double
    public async Task AdditionOperator_ShouldReturnExpectedResult(object operand1, object operand2, object expectedResult)
    {
        string vbOperand1 = ConvertToVb6Value(operand1);
        string vbOperand2 = ConvertToVb6Value(operand2);
        string code = $@"
            Dim result
            result = {vbOperand1} + {vbOperand2}
            Debug.Print result
        ";

        await Run(code);

        Vb6Value expectedValue = expectedResult is int i ? new Vb6Value(i) : expectedResult is float f ? new Vb6Value(f) : expectedResult is double d ? new Vb6Value(d) : throw new Exception();

        AssertDebugLog([expectedValue]);
    }

    [Theory]
    [InlineData(5, 2, 3)]               // int - int
    [InlineData(5, 2.5F, 2.5F)]         // int - float
    [InlineData(5, 2.5D, 2.5D)]         // int - double
    [InlineData(5.5F, 2, 3.5F)]         // float - int
    [InlineData(5.5F, 2.5F, 3.0F)]      // float - float
    [InlineData(5.5F, 2.5D, 3.0D)]      // float - double
    [InlineData(5.5D, 2, 3.5D)]         // double - int
    [InlineData(5.5D, 2.5F, 3.0D)]      // double - float
    [InlineData(5.5D, 2.5D, 3.0D)]      // double - double
    public async Task SubtractionOperator_ShouldReturnExpectedResult(object operand1, object operand2, object expectedResult)
    {
        string vbOperand1 = ConvertToVb6Value(operand1);
        string vbOperand2 = ConvertToVb6Value(operand2);
        string code = $@"
            Dim result
            result = {vbOperand1} - {vbOperand2}
            Debug.Print result
        ";

        await Run(code);

        Vb6Value expectedValue = expectedResult is int i ? new Vb6Value(i) : expectedResult is float f ? new Vb6Value(f) : expectedResult is double d ? new Vb6Value(d) : throw new Exception();

        AssertDebugLog([expectedValue]);
    }

    [Theory]
    [InlineData(5, 2, 2.5)]             // int / int
    [InlineData(5, 2.5F, 2.0F)]          // int / float
    [InlineData(5, 2.5D, 2.0)]          // int / double
    [InlineData(5.5F, 2, 2.75F)]        // float / int
    [InlineData(5.5F, 2.5F, 2.2F)]      // float / float
    [InlineData(5.5F, 2.5D, 2.2D)]      // float / double
    [InlineData(5.5D, 2, 2.75D)]        // double / int
    [InlineData(5.5D, 2.5F, 2.2D)]      // double / float
    [InlineData(5.5D, 2.5D, 2.2D)]      // double / double
    public async Task DivisionOperator_ShouldReturnExpectedResult(object operand1, object operand2, object expectedResult)
    {
        string vbOperand1 = ConvertToVb6Value(operand1);
        string vbOperand2 = ConvertToVb6Value(operand2);
        string code = $@"
            Dim result
            result = {vbOperand1} / {vbOperand2}
            Debug.Print result
        ";

        await Run(code);

        Vb6Value expectedValue = expectedResult is int i ? new Vb6Value(i) : expectedResult is float f ? new Vb6Value(f) : expectedResult is double d ? new Vb6Value(d) : throw new Exception();

        AssertDebugLog([expectedValue]);
    }

    [Theory]
    [InlineData(3, 2, 6)]               // int * int
    [InlineData(3, 2.5F, 7.5F)]         // int * float
    [InlineData(3, 2.5D, 7.5D)]         // int * double
    [InlineData(3.5F, 2, 7.0F)]         // float * int
    [InlineData(3.5F, 2.5F, 8.75F)]      // float * float
    [InlineData(3.5F, 2.5D, 8.75D)]      // float * double
    [InlineData(3.5D, 2, 7.0D)]         // double * int
    [InlineData(3.5D, 2.5F, 8.75D)]      // double * float
    [InlineData(3.5D, 2.5D, 8.75D)]      // double * double
    public async Task MultiplicationOperator_ShouldReturnExpectedResult(object operand1, object operand2, object expectedResult)
    {
        string vbOperand1 = ConvertToVb6Value(operand1);
        string vbOperand2 = ConvertToVb6Value(operand2);
        string code = $@"
            Dim result
            result = {vbOperand1} * {vbOperand2}
            Debug.Print result
        ";

        await Run(code);

        Vb6Value expectedValue = expectedResult is int i ? new Vb6Value(i) : expectedResult is float f ? new Vb6Value(f) : expectedResult is double d ? new Vb6Value(d) : throw new Exception();

        AssertDebugLog([expectedValue]);
    }

    // VB6 Mod (verified against vb6.exe): operands are banker's-rounded to an integer first, then integer
    // remainder (sign follows the dividend). Result is Integer only when both operands are Byte/Integer/Boolean;
    // if either operand is Single/Double/Long it is Long. So 5.5 Mod 2 = 6 Mod 2 = 0, typed Long.
    [Theory]
    [InlineData(5, 2, 1)]               // Integer Mod Integer -> 1 (Integer)
    [InlineData(5, 3, 2)]               // -> 2 (Integer)
    [InlineData(8, 3, 2)]               // -> 2 (Integer)
    [InlineData(5.5D, 2, 0L)]           // 5.5 rounds to 6; 6 Mod 2 = 0; a Double operand makes it Long
    [InlineData(5.5F, 2, 0L)]           // a Single operand makes it Long
    [InlineData(7.6D, 3, 2L)]           // 7.6 rounds to 8; 8 Mod 3 = 2 (Long)
    [InlineData(2.5D, 2, 0L)]           // 2.5 rounds half-to-even to 2; 2 Mod 2 = 0 (Long)
    public async Task ModulusOperator_ShouldReturnExpectedResult(object operand1, object operand2, object expectedResult)
    {
        string vbOperand1 = ConvertToVb6Value(operand1);
        string vbOperand2 = ConvertToVb6Value(operand2);
        string code = $@"
            Dim result
            result = {vbOperand1} Mod {vbOperand2}
            Debug.Print result
        ";

        await Run(code);

        Vb6Value expectedValue = expectedResult is int i ? new Vb6Value(i) : expectedResult is long l ? new Vb6Value(l) : expectedResult is float f ? new Vb6Value(f) : expectedResult is double d ? new Vb6Value(d) : throw new Exception();

        AssertDebugLog([expectedValue]);
    }

    [Theory]
    [InlineData(2, 3, 8d)]               // int ^ int
    [InlineData(2, 3.5F, 11.3137D)]     // int ^ float
    [InlineData(2, 3.5D, 11.3137D)]     // int ^ double
    [InlineData(3.5F, 2, 12.25D)]       // float ^ int
    [InlineData(3.5F, 2.5F, 22.9169D)]   // float ^ float
    [InlineData(3.5F, 2.5D, 22.9169D)]   // float ^ double
    [InlineData(3.5D, 2, 12.25D)]       // double ^ int
    [InlineData(3.5D, 2.5F, 22.9169D)]   // double ^ float
    [InlineData(3.5D, 2.5D, 22.9169D)]   // double ^ double
    public async Task PowerOperator_ShouldReturnExpectedResult(object operand1, object operand2, object expectedResult)
    {
        string vbOperand1 = ConvertToVb6Value(operand1);
        string vbOperand2 = ConvertToVb6Value(operand2);
        string code = $@"
            Dim result
            result = {vbOperand1} ^ {vbOperand2}
            Debug.Print result
        ";

        await Run(code);

        Vb6Value expectedValue = expectedResult is int i ? new Vb6Value(i) : expectedResult is float f ? new Vb6Value(f) : expectedResult is double d ? new Vb6Value(d) : throw new Exception();

        AssertDebugLog([expectedValue]);
    }

    [Theory]
    [InlineData("Hello", " World", "Hello World")] // String + String
    [InlineData("Value: ", 42, "Value: 42")]        // String + Int
    [InlineData("Value: ", 3.14F, "Value: 3.14")]   // String + Float
    [InlineData("Value: ", 3.14D, "Value: 3.14")]   // String + Double
    [InlineData(42, " is the answer", "42 is the answer")] // Int + String
    [InlineData(3.14F, " is pi", "3.14 is pi")]     // Float + String
    [InlineData(3.14D, " is pi", "3.14 is pi")]     // Double + String
    [InlineData(null, "Hello", "Hello")]             // Null + String
    [InlineData("Hello", null, "Hello")]             // String + Null
    [InlineData(null, null, null)]                    // Null + Null
    [InlineData("", "Hello", "Hello")]                // Empty + String
    [InlineData("Hello", "", "Hello")]                // String + Empty
    [InlineData("", "", "")]                          // Empty + Empty
    public async Task AmpersandOperator_ShouldReturnExpectedResult(object? operand1, object? operand2, object? expectedResult)
    {
        string vbOperand1 = ConvertToVb6Value(operand1);
        string vbOperand2 = ConvertToVb6Value(operand2);
        string code = $@"
            Dim result
            result = {vbOperand1} & {vbOperand2}
            Debug.Print result
        ";

        await Run(code);

        Vb6Value expectedValue = expectedResult is string str ? new Vb6Value(str) : expectedResult is null ? Vb6Value.Null : throw new Exception();

        AssertDebugLog([expectedValue]);
    }
}
