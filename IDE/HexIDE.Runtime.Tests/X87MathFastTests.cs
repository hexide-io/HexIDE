using System.Diagnostics;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class X87MathFastTests
{
    public static TheoryData<string> Functions => new("Sin", "Cos", "Tan", "Atn");

    // #5337 preserves #5259's model and the measurements in oracle PRs #5258 and #5294.
    [Theory]
    [MemberData(nameof(Functions))]
    public void Measured_cells_keep_the_same_bits(string function)
    {
        var (fast, exact) = GetFunctions(function);
        foreach (var row in X87MathOracle.Trig)
        {
            double x = BitConverter.UInt64BitsToDouble(row.Argument);
            ulong expected = function switch
            {
                "Sin" => row.Sin, "Cos" => row.Cos, "Tan" => row.Tan, _ => row.Atn
            };
            BitConverter.DoubleToUInt64Bits(fast(x)).Should().Be(expected);
            BitConverter.DoubleToUInt64Bits(exact(x)).Should().Be(expected);
        }
    }

    [Theory]
    [MemberData(nameof(Functions))]
    public void Seeded_arguments_and_cancellation_keep_the_models_bits(string function)
    {
        var (fast, exact) = GetFunctions(function);
        foreach (double x in X87MathArguments.Create(20000))
            BitConverter.DoubleToUInt64Bits(fast(x)).Should().Be(BitConverter.DoubleToUInt64Bits(exact(x)),
                "{0} at argument bits {1:X16}", function, BitConverter.DoubleToUInt64Bits(x));
    }

    [Theory]
    [InlineData(0x0000000000000000UL)]
    [InlineData(0x8000000000000000UL)]
    [InlineData(0x0000000000000001UL)]
    [InlineData(0x8000000000000001UL)]
    [InlineData(0x3DDFFFFFFFFFFFFFUL)]
    [InlineData(0x3DE0000000000000UL)]
    [InlineData(0x3DF0000000000000UL)]
    [InlineData(0x43DFFFFFFFFFFFFFUL)]
    [InlineData(0xC3DFFFFFFFFFFFFFUL)]
    public void Tiny_and_limit_edges_keep_the_models_bits(ulong bits)
    {
        double x = BitConverter.UInt64BitsToDouble(bits);
        foreach (string name in new[] { "Sin", "Cos", "Tan", "Atn" })
        {
            var (fast, exact) = GetFunctions(name);
            BitConverter.DoubleToUInt64Bits(fast(x)).Should().Be(BitConverter.DoubleToUInt64Bits(exact(x)));
        }
    }

    [Theory]
    [InlineData(1.0 / 64)]
    [InlineData(1.0 / 16)]
    [InlineData(1.0 / 4)]
    public void Atan_series_length_boundaries_keep_the_models_bits(double boundary)
    {
        foreach (double x in new[] { Math.BitDecrement(boundary), boundary, Math.BitIncrement(boundary) })
        {
            BitConverter.DoubleToUInt64Bits(X87Math.Atan(x)).Should().Be(BitConverter.DoubleToUInt64Bits(X87Math.AtanExact(x)));
            BitConverter.DoubleToUInt64Bits(X87Math.Atan(-x)).Should().Be(BitConverter.DoubleToUInt64Bits(X87Math.AtanExact(-x)));
        }
    }

    [Fact]
    public void A_possible_64_bit_boundary_uses_the_exact_model()
    {
        double midpoint = Math.ScaleB(1.0, -64);
        X87Math.TryStoreFast(1, midpoint, out _).Should().BeFalse();
        X87Math.TryStoreFast(1, Math.BitIncrement(midpoint), out _).Should().BeFalse();
        X87Math.TryStoreFast(1, Math.BitDecrement(midpoint), out _).Should().BeFalse();
        // Immediately below a power of two, the 64-bit spacing is half as large.
        X87Math.TryStoreFast(1, -midpoint / 2, out _).Should().BeFalse();
        X87Math.TryStoreFast(-1, midpoint / 2, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(-1, 0x3FF0000000000000UL)]
    [InlineData(0, 0x3FF0000000000000UL)]
    [InlineData(1, 0x3FF0000000000000UL)]
    [InlineData(3, 0x3FF0000000000001UL)]
    public void The_second_round_keeps_the_64_bit_store_at_a_double_tie(int quarters, ulong expected)
    {
        double low = Math.ScaleB(1.0, -53) + quarters * Math.ScaleB(1.0, -65);
        X87Math.TryStoreFast(1, low, out double result).Should().BeTrue();
        BitConverter.DoubleToUInt64Bits(result).Should().Be(expected);
    }

    [Fact(Timeout = 300_000)]
    public async Task Release_fast_paths_cost_at_most_a_tenth_of_the_full_model()
    {
        // Performance is a Release contract (#5337). Build the same source optimized,
        // even when this suite is Debug, rather than timing unoptimized operators.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Join(root.FullName, "CLAUDE.md")))
            root = root.Parent;
        root.Should().NotBeNull();
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root!.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[]
                 {
                     "run", "--project", "scripts/probes/issue-5337/Probe.csproj", "-c", "Release",
                     "--disable-build-servers", "--artifacts-path", "artifacts/5337-speed-check", "--", "--benchmark"
                 })
            start.ArgumentList.Add(argument);
        start.Environment["UseSharedCompilation"] = "false";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        using var process = Process.Start(start)!;
        var cancel = TestContext.Current.CancellationToken;
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancel);
        Task<string> error = process.StandardError.ReadToEndAsync(cancel);
        try
        {
            await process.WaitForExitAsync(cancel);
        }
        catch (OperationCanceledException)
        {
            // A timed-out build must not outlive the test and hold the run's output pipe.
            process.Kill(entireProcessTree: true);
            throw;
        }
        string report = await output + await error;
        TestContext.Current.TestOutputHelper!.WriteLine(report);
        process.ExitCode.Should().Be(0, "all four optimized paths must cost at most a tenth of the full model: {0}", report);
        report.Should().Contain("Sin: fast").And.Contain("Cos: fast").And.Contain("Tan: fast").And.Contain("Atn: fast");
    }

    private static (Func<double, double> Fast, Func<double, double> Exact) GetFunctions(string name) => name switch
    {
        "Sin" => (X87Math.Sin, X87Math.SinExact),
        "Cos" => (X87Math.Cos, X87Math.CosExact),
        "Tan" => (X87Math.Tan, X87Math.TanExact),
        _ => (X87Math.Atan, X87Math.AtanExact)
    };
}
