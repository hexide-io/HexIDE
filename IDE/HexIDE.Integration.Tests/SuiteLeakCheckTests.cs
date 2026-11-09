using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit.Sdk;
using Xunit.v3;

namespace HexIDE.Integration.Tests;

public class SuiteLeakCheckTests
{
    [Fact]
    public async Task Disposing_the_tracking_fixture_does_not_report_a_leak_again()
    {
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("ping", "-n 60 127.0.0.1")
            : new ProcessStartInfo("sleep", "60");
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.CreateNoWindow = true;
        using var process = Process.Start(start)!;
        var guard = new LanguageServerLeakGuard();
        try
        {
            LanguageServerLeakGuard.Note(process);
            Func<Task> dispose = async () => await guard.DisposeAsync();
            await dispose.Should().NotThrowAsync("leaks belong to the final test, not assembly fixture disposal");
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
    }

    [Fact]
    public void The_window_guard_is_not_an_assembly_fixture()
    {
        typeof(WindowLeakGuard).Assembly.GetCustomAttributes<AssemblyFixtureAttribute>()
            .Select(attribute => attribute.AssemblyFixtureType).Should().NotContain(typeof(WindowLeakGuard));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Both_checks_are_tests_in_the_collection_ordered_last(int position)
    {
        var assembly = typeof(WindowLeakGuard).Assembly;
        var checks = assembly.GetType("HexIDE.Integration.Tests.SuiteLeakChecks");
        checks.Should().NotBeNull("the leak checks must be ordinary tests");
        var methods = checks!.GetMethods().Where(method => method.GetCustomAttribute<FactAttribute>() != null).ToList();
        methods.Should().HaveCount(2);
        methods.Single(method => method.Name == "Every_window_was_closed")
            .GetCustomAttribute<Avalonia.Headless.XUnit.AvaloniaFactAttribute>().Should().NotBeNull();
        methods.Single(method => method.Name == "Every_language_server_was_stopped")
            .GetCustomAttribute<FactAttribute>().Should().NotBeNull();
        var collection = checks.GetCustomAttribute<CollectionAttribute>();
        collection.Should().NotBeNull();
        var attribute = assembly.GetCustomAttribute<TestCollectionOrdererAttribute>();
        attribute.Should().NotBeNull();
        var orderer = (ITestCollectionOrderer)Activator.CreateInstance(attribute!.OrdererType)!;
        var final = Substitute.For<ITestCollection>();
        final.TestCollectionClassName.Returns(collection!.Type!.FullName);
        var first = Substitute.For<ITestCollection>();
        var second = Substitute.For<ITestCollection>();
        var collections = new[] { first, second }.ToList();
        collections.Insert(position, final);
        orderer.OrderTestCollections(collections).Should().Equal(first, second, final);
    }
}
