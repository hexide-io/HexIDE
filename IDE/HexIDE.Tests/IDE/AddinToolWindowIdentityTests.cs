using HexIDE.Addins;

namespace HexIDE.Tests.IDE;

public class AddinToolWindowIdentityTests
{
    [Fact]
    public void Register_DuplicateWithinOwner_IsRefusedBeforeCreatingContent()
    {
        var service = new AddinToolWindowService();
        using var first = service.Register("output", "First", null, () => new object());
        var created = false;
        var register = () => service.Register("output", "Second", null, () => { created = true; return new object(); });

        register.Should().Throw<InvalidOperationException>();
        created.Should().BeFalse();
        service.Tools.Should().HaveCount(1);
    }
    [Fact]
    public void Register_ReentrantDuplicate_IsRefused_AndFailedFactoryReleasesTheId()
    {
        var service = new AddinToolWindowService();
        var register = () => service.Register("output", "Outer", null, () =>
        {
            using var nested = service.Register("output", "Nested", null, () => new object());
            return new object();
        });

        register.Should().Throw<InvalidOperationException>();
        service.Tools.Should().BeEmpty();
        using var replacement = service.Register("output", "Replacement", null, () => new object());
        service.Tools.Should().ContainSingle();
    }

}
