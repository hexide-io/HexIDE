using System;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 2.4 — the consolidated As-clause mapper now recognises Long/Byte/Date (and Variant), so those
/// declarations parse and get the correct default value.
/// </summary>
public class DeclarationTests : BaseVBTestFixture
{
    [Fact]
    public async Task DimLong_DefaultsToLongZero()
    {
        await Run("Dim x As Long\nDebug.Print x\n");
        AssertDebugLog([new Vb6Value(0L)]);
    }

    [Fact]
    public async Task DimByte_DefaultsToByteZero()
    {
        await Run("Dim x As Byte\nDebug.Print x\n");
        AssertDebugLog([new Vb6Value((byte)0)]);
    }

    [Fact]
    public async Task DimDate_DefaultsToVb6Epoch()
    {
        await Run("Dim d As Date\nDebug.Print d\n");
        AssertDebugLog([new Vb6Value(new DateTime(1899, 12, 30))]);
    }

    [Fact]
    public async Task DimInteger_StillDefaultsToIntegerZero()
    {
        await Run("Dim i As Integer\nDebug.Print i\n");
        AssertDebugLog([new Vb6Value(0)]);
    }

    [Fact]
    public async Task DimCurrency_DefaultsToCurrencyZero()
    {
        // `As Currency` now parses (new grammar token) and maps to the Currency default.
        await Run("Dim c As Currency\nDebug.Print c\n");
        AssertDebugLog([Vb6Value.NewCurrency(0m)]);
    }
}
