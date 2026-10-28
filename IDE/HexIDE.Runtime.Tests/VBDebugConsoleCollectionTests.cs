using System.Reflection;

namespace HexIDE.Runtime.Tests;

public class VBDebugConsoleCollectionTests
{
    [Theory]
    [InlineData(typeof(VbDisplayTests))]
    [InlineData(typeof(VBDebugConsoleTests))]
    public void Static_output_users_share_a_nonparallel_collection(Type testClass)
    {
        var collection = testClass.GetCustomAttribute<CollectionAttribute>();
        collection.Should().NotBeNull();
        collection.Type.Should().Be(typeof(VBDebugConsoleCollection));
        collection.Type!.GetCustomAttribute<CollectionDefinitionAttribute>()!
            .DisableParallelization.Should().BeTrue();
    }
}

// Both classes capture the same static Output event (#2326).
[CollectionDefinition(DisableParallelization = true)]
public sealed class VBDebugConsoleCollection;
