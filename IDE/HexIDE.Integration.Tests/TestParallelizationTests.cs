using System.Reflection;

namespace HexIDE.Integration.Tests;

public class TestParallelizationTests
{
    private const string Why =
        "a plain test that builds an Avalonia object while another class's Avalonia test is set up takes the UI thread, " +
        "and the run hangs (#2530)";

    [Fact]
    public void Test_classes_run_one_at_a_time()
    {
        var behaviour = typeof(TestParallelizationTests).Assembly.GetCustomAttribute<CollectionBehaviorAttribute>();
        behaviour.Should().NotBeNull(Why);
        behaviour!.DisableTestParallelization.Should().BeTrue(Why);
    }
}
