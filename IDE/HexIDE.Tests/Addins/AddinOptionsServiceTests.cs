using System.Reflection;

namespace HexIDE.Tests.Addins;

public class AddinOptionsServiceTests
{
    // Registering from this test method captures THIS test assembly as the "add-in".
    private static string ThisAssemblyPath => Assembly.GetExecutingAssembly().Location;

    [Fact]
    public void RegisterAndLookup_RoundTrips()
    {
        var svc = new AddinOptionsService();
        object control = new();

        svc.RegisterOptionsPage(() => control);
        var factory = svc.GetFactoryFor(ThisAssemblyPath);

        factory.Should().NotBeNull();
        factory!().Should().BeSameAs(control);
    }

    [Fact]
    public void Dispose_RemovesRegistration()
    {
        var svc = new AddinOptionsService();
        var handle = svc.RegisterOptionsPage(() => new object());

        handle.Dispose();

        svc.GetFactoryFor(ThisAssemblyPath).Should().BeNull();
    }

    [Fact]
    public void GetFactoryFor_UnknownPath_ReturnsNull()
    {
        var svc = new AddinOptionsService();
        svc.GetFactoryFor("/no/such/addin.dll").Should().BeNull();
    }
}
