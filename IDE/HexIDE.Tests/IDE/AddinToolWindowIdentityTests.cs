using HexIDE.Addins;

namespace HexIDE.Tests.IDE;

public class AddinToolWindowIdentityTests
{
    [Fact]
    public void OwnedHost_ForwardsNewContributionSurfacesToTheHost()
    {
        var host = Substitute.For<IHexIdeHost>();
        var tools = new AddinToolWindowService();
        var owned = new AddinOwnedHost(host, tools.ForOwner("owner"), new AddinOrigin("owner", "Owner"));
        var server = new AddinLanguageServer(null!);
        var launch = new AddinDebugLaunch("Fixture") { CreateConfigurationAsync = (_, _) => throw new NotSupportedException() };
        var project = new AddinProjectType("Fixture")
        {
            FileNames = ["*.fixture"],
            OpenAsync = (_, _, _) => throw new NotSupportedException(),
        };
        var connection = Substitute.For<IAddinServerConnection>();
        var launchHandle = Substitute.For<IDisposable>();
        var projectHandle = Substitute.For<IDisposable>();
        host.RegisterLanguageServer(server).Returns(connection);
        host.RegisterDebugLaunch(launch).Returns(launchHandle);
        host.RegisterProjectType(project).Returns(projectHandle);

        owned.RegisterLanguageServer(server).Should().BeSameAs(connection);
        owned.RegisterDebugLaunch(launch).Should().BeSameAs(launchHandle);
        owned.RegisterProjectType(project).Should().BeSameAs(projectHandle);
        host.Received(1).RegisterLanguageServer(server);
        host.Received(1).RegisterDebugLaunch(launch);
        host.Received(1).RegisterProjectType(project);
    }

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
