using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 3 — Inspection / type-test intrinsics (TypeName, VarType, IsNumeric, IsDate, IsEmpty, IsNull,
/// IsArray, IsObject). Every expected value was pinned against vb6.exe.
/// </summary>
public class InspectionFunctionsTests : BaseVBTestFixture
{
    [Fact]
    public async Task TypeName_AcrossSubtypes()
    {
        await Run(
            "Debug.Print TypeName(123)\n" +
            "Debug.Print TypeName(100000)\n" +
            "Debug.Print TypeName(1.5)\n" +
            "Debug.Print TypeName(1.5!)\n" +
            "Debug.Print TypeName(1.5@)\n" +
            "Debug.Print TypeName(\"hi\")\n" +
            "Debug.Print TypeName(True)\n" +
            "Debug.Print TypeName(#1/2/2020#)\n" +
            "Debug.Print TypeName(CByte(5))\n" +
            "Debug.Print TypeName(Null)\n");
        AssertDebugLog(["Integer", "Long", "Double", "Single", "Currency", "String", "Boolean", "Date", "Byte", "Null"]);
    }

    [Fact]
    public async Task VarType_AcrossSubtypes()
    {
        await Run(
            "Debug.Print VarType(123)\n" +      // vbInteger
            "Debug.Print VarType(100000)\n" +   // vbLong
            "Debug.Print VarType(1.5)\n" +      // vbDouble
            "Debug.Print VarType(1.5!)\n" +     // vbSingle
            "Debug.Print VarType(1.5@)\n" +     // vbCurrency
            "Debug.Print VarType(\"hi\")\n" +   // vbString
            "Debug.Print VarType(True)\n" +     // vbBoolean
            "Debug.Print VarType(#1/2/2020#)\n" + // vbDate
            "Debug.Print VarType(CByte(5))\n" + // vbByte
            "Debug.Print VarType(Null)\n");     // vbNull
        // VarType is LONG, despite every vbXxx code fitting an Integer — measured (#193).
        AssertDebugLog([new Vb6Value(2L), new Vb6Value(3L), new Vb6Value(5L), new Vb6Value(4L), new Vb6Value(6L), new Vb6Value(8L), new Vb6Value(11L), new Vb6Value(7L), new Vb6Value(17L), new Vb6Value(1L)]);
    }

    [Fact]
    public async Task TypeName_And_VarType_OfEmptyAndArray()
    {
        await Run(
            "Dim v\n" +
            "Dim a(1 To 3) As Integer\n" +
            "Debug.Print TypeName(v)\n" +
            "Debug.Print VarType(v)\n" +
            "Debug.Print TypeName(a)\n" +
            "Debug.Print VarType(a)\n");
        AssertDebugLog(["Empty", new Vb6Value(0L), "Integer()", new Vb6Value(8194L)]);   // vbArray(8192) + vbInteger(2); VarType is Long (#193)
    }

    [Fact]
    public async Task IsNumeric_AcrossValuesAndStrings()
    {
        await Run(
            "Debug.Print IsNumeric(123)\n" +          // True
            "Debug.Print IsNumeric(True)\n" +         // True  (Boolean is numeric)
            "Debug.Print IsNumeric(#1/2/2020#)\n" +   // False (Date is not)
            "Debug.Print IsNumeric(\"123\")\n" +      // True
            "Debug.Print IsNumeric(\"1E3\")\n" +      // True
            "Debug.Print IsNumeric(\"  12  \")\n" +   // True (whitespace)
            "Debug.Print IsNumeric(\"&HFF\")\n" +     // True (hex string)
            "Debug.Print IsNumeric(\"1,234\")\n" +    // True (thousands)
            "Debug.Print IsNumeric(\"(12)\")\n" +     // True (accounting negative)
            "Debug.Print IsNumeric(\"12-\")\n" +      // True (trailing sign)
            "Debug.Print IsNumeric(\"$12\")\n" +      // False (currency symbol)
            "Debug.Print IsNumeric(\"\")\n" +         // False
            "Debug.Print IsNumeric(\"abc\")\n");      // False
        AssertDebugLog([true, true, false, true, true, true, true, true, true, true, false, false, false]);
    }

    [Fact]
    public async Task IsNumeric_OfEmpty_IsTrue()
    {
        await Run("Dim v\nDebug.Print IsNumeric(v)\n");
        AssertDebugLog([true]);
    }

    [Fact]
    public async Task IsDate_DateAndStringsButNotNumbers()
    {
        await Run(
            "Debug.Print IsDate(#1/2/2020#)\n" +   // True
            "Debug.Print IsDate(\"1/2/2020\")\n" + // True
            "Debug.Print IsDate(\"abc\")\n" +      // False
            "Debug.Print IsDate(40000)\n");        // False (a number is not a date)
        AssertDebugLog([true, true, false, false]);
    }

    [Fact]
    public async Task IsEmpty_And_IsNull()
    {
        await Run(
            "Dim v\n" +
            "Debug.Print IsEmpty(v)\n" +    // True
            "Debug.Print IsEmpty(0)\n" +    // False
            "Debug.Print IsNull(Null)\n" +  // True
            "Debug.Print IsNull(v)\n" +     // False (Empty is not Null)
            "Debug.Print IsNull(0)\n");     // False
        AssertDebugLog([true, false, true, false, false]);
    }

    [Fact]
    public async Task IsArray_And_IsObject()
    {
        await Run(
            "Dim a(1 To 3) As Integer\n" +
            "Debug.Print IsArray(a)\n" +   // True
            "Debug.Print IsArray(123)\n" + // False
            "Debug.Print IsObject(123)\n");// False
        AssertDebugLog([true, false, false]);
    }
}
