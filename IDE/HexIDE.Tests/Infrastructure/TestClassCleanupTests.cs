using System.Reflection;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// xunit v3 disposes a test class through <see cref="IAsyncDisposable"/> when it has it, and then never calls
/// <see cref="IDisposable.Dispose"/>. A class that gets <c>DisposeAsync</c> from a base and does its own cleanup in
/// <c>Dispose</c> leaks that cleanup on every test, silently: one class's temporary folders built up in the thousands, and the
/// test fallback it reset in <c>Dispose</c> stayed set for every later class (#3411 slice 10b-3). So a test class has one
/// cleanup, and overrides <c>DisposeAsync</c> where its base has one. This file is linked into every test project, and each
/// runs it over its own assembly.
/// </summary>
public sealed class TestClassCleanupTests
{
    [Fact]
    public void No_test_class_has_a_Dispose_that_xunit_never_calls()
    {
        var testClasses = typeof(TestClassCleanupTests).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && IsTestClass(type))
            .ToList();
        testClasses.Should().Contain(typeof(TestClassCleanupTests), "the premise: the assembly's test classes are found");

        var both = testClasses
            .Where(type => typeof(IAsyncDisposable).IsAssignableFrom(type) && typeof(IDisposable).IsAssignableFrom(type))
            .Select(type => type.FullName)
            .ToList();

        string.Join(", ", both).Should().BeEmpty("xunit v3 calls only DisposeAsync on a class that has both, so its Dispose "
            + "never runs; put the cleanup in one, overriding the base's DisposeAsync");
    }

    private static bool IsTestClass(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Any(method => method.GetCustomAttributes(inherit: true).Any(attribute => IsFact(attribute.GetType())));

    // AvaloniaFact, ForeignServerFact, Theory and the rest all derive from xunit's FactAttribute.
    private static bool IsFact(Type? attribute)
    {
        for (; attribute is not null; attribute = attribute.BaseType)
            if (attribute.Name == "FactAttribute")
                return true;
        return false;
    }
}
