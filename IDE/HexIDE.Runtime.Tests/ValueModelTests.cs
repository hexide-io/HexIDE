using System;
using HexIDE.Runtime.Interpreter;
using VT = HexIDE.Runtime.Interpreter.Vb6Value.ValueType;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 2.1 — the real VB6 scalar value model. Adds Long/Byte/Currency/Decimal, a working Date default,
/// and the magnitude rule for the C# int → Vb6Value conversion (fits Int16 ⇒ Integer, else ⇒ Long). Pure
/// value-model facts — no interpreter run.
/// </summary>
public class ValueModelTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-32768)]   // Int16.MinValue
    [InlineData(32767)]    // Int16.MaxValue
    public void IntCtor_WithinInt16_IsInteger_IntBoxed(int v)
    {
        var val = new Vb6Value(v);
        val.Type.Should().Be(VT.Integer);
        val.Value.Should().BeOfType<int>();   // Integer stays int-boxed (16-bit range enforced later, by range-check)
    }

    [Theory]
    [InlineData(32768)]
    [InlineData(-32769)]
    [InlineData(40000)]
    [InlineData(1048576)]
    public void IntCtor_OutsideInt16_IsLong_LongBoxed(int v)
    {
        var val = new Vb6Value(v);
        val.Type.Should().Be(VT.Long);
        val.Value.Should().BeOfType<long>();
        ((long)val.Value!).Should().Be(v);
    }

    [Fact]
    public void TypedCtors_ProduceExpectedTypes()
    {
        new Vb6Value(5L).Type.Should().Be(VT.Long);
        new Vb6Value((byte)5).Type.Should().Be(VT.Byte);
        new Vb6Value(new DateTime(2000, 1, 1)).Type.Should().Be(VT.Date);
    }

    [Fact]
    public void CurrencyAndDecimal_ShareBox_ButAreDistinctTypes()
    {
        var cur = Vb6Value.NewCurrency(1.5m);
        var dec = Vb6Value.NewDecimal(1.5m);

        cur.Type.Should().Be(VT.Currency);
        dec.Type.Should().Be(VT.Decimal);
        cur.Value.Should().BeOfType<decimal>();
        dec.Value.Should().BeOfType<decimal>();

        // Same boxed value, different VB6 subtype ⇒ not equal (Type is part of Vb6Value equality).
        (cur == dec).Should().BeFalse();
    }

    [Fact]
    public void GetDefaultValueFor_NewNumericTypes_AreZeroOfTheRightBox()
    {
        new Vb6Value(VT.Long).Value.Should().Be(0L);
        new Vb6Value(VT.Byte).Value.Should().Be((byte)0);
        new Vb6Value(VT.Currency).Value.Should().Be(0m);
        new Vb6Value(VT.Decimal).Value.Should().Be(0m);
    }

    [Fact]
    public void DateDefault_IsVb6Epoch_1899_12_30()
    {
        // VB6 serial 0 (a Dim'd-but-unset Date) is 1899-12-30 00:00 — previously this threw.
        new Vb6Value(VT.Date).Value.Should().Be(new DateTime(1899, 12, 30));
    }

    [Fact]
    public void IntegerAndLong_OfSameMagnitude_AreNotEqual()
    {
        var integer = new Vb6Value(5);    // Integer (fits Int16)
        var @long = new Vb6Value(5L);     // Long (explicit)

        integer.Type.Should().Be(VT.Integer);
        @long.Type.Should().Be(VT.Long);
        (integer == @long).Should().BeFalse();
    }

    // --- 2.2: cross-type numeric ordering via TryCompareTo (used by Select Case / comparisons) ---

    [Fact]
    public void TryCompareTo_CrossTypeNumeric_ComparesByValue()
    {
        (new Vb6Value(40000).TryCompareTo(new Vb6Value(1)) > 0).Should().BeTrue();        // Long vs Integer
        new Vb6Value(5L).TryCompareTo(new Vb6Value(5)).Should().Be(0);                    // Long == Integer (by value)
        (Vb6Value.NewCurrency(2.5m).TryCompareTo(new Vb6Value(3)) < 0).Should().BeTrue(); // Currency vs Integer
        new Vb6Value((byte)10).TryCompareTo(new Vb6Value(10)).Should().Be(0);             // Byte == Integer
    }

    [Fact]
    public void TryCompareTo_SameNewType_IsNative()
    {
        (new Vb6Value(5L).TryCompareTo(new Vb6Value(3L)) > 0).Should().BeTrue();
        (Vb6Value.NewCurrency(1.2346m).TryCompareTo(Vb6Value.NewCurrency(1.2345m)) > 0).Should().BeTrue();
        (new Vb6Value(new DateTime(2001, 1, 1)).TryCompareTo(new Vb6Value(new DateTime(2000, 1, 1))) > 0).Should().BeTrue();
    }
}
