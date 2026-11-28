using HexIDE.Addins;
using HexIDE.Runtime;

namespace HexIDE.Runtime.Tests;

public class BasicProviderPackRegistrationTests
{
    [Fact]
    public void Provider_registers_its_pack_area_after_resources_and_disposes_it_first()
    {
        var host = Substitute.For<IHexIdeHost>();
        var order = new List<string>();
        host.RegisterResources(Arg.Any<Uri>()).Returns(_ =>
        {
            order.Add("resource");
            var handle = Substitute.For<IDisposable>();
            handle.When(h => h.Dispose()).Do(_ => order.Add("dispose resource"));
            return handle;
        });
        host.RegisterLanguagePacks(Arg.Any<Uri>()).Returns(call =>
        {
            call.Arg<Uri>().Should().Be(new Uri("avares://HexIDE.Runtime/Localization/Packs/"));
            order.Add("packs");
            var handle = Substitute.For<IDisposable>();
            handle.When(h => h.Dispose()).Do(_ => order.Add("dispose packs"));
            return handle;
        });
        var provider = new BasicProvider();

        provider.Initialize(host);
        provider.Dispose();
        provider.Dispose();

        order.Should().Equal("resource", "resource", "resource", "packs", "dispose packs",
            "dispose resource", "dispose resource", "dispose resource");
    }
}
