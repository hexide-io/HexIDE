using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Runtime.Components;

namespace HexIDE.Tests.Addins;

public sealed class AddinWithdrawalTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Host_handle_withdraws_only_its_registration(bool component, bool owned)
    {
        var components = new ComponentRegistry();
        var templates = new AddinProjectTemplateService();
        IHexIdeHost host = new HexIdeHost(null!, null!, null!, null!, null!, null!, null!, null!,
            templates, null!, components, null!, null!, null!, null!);
        if (owned) host = new AddinOwnedHost(host, null!, new AddinOrigin("probe", "Probe"));
        object contribution = component ? Substitute.For<IComponentClass>() : Substitute.For<IAddinProjectTemplate>();
        // Reflection lets the regression run against the original void SDK signatures too.
        var method = typeof(IHexIdeHost).GetMethod(component ? "RegisterComponent" : "RegisterProjectTemplate")!;
        var removed = 0;
        components.ComponentUnregistered += _ => removed++;
        IDisposable? first;
        IDisposable? second;
        using (var initialization = new AddinInitializationScope())
        {
            first = method.Invoke(host, [contribution]) as IDisposable;
            second = method.Invoke(host, [contribution]) as IDisposable;
            initialization.Complete();
        }
        first.Should().NotBeNull("each registration must return a withdrawal handle (#3425)");
        second.Should().NotBeNull();
        first!.Dispose();
        first.Dispose();
        if (component) components.Components.Should().ContainSingle(c => ReferenceEquals(c, contribution));
        else templates.Templates.Should().ContainSingle(t => ReferenceEquals(t, contribution));
        second!.Dispose();
        if (component)
        {
            components.Components.Should().NotContain((IComponentClass)contribution);
            removed.Should().Be(2);
        }
        else templates.Templates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Disposed_handle_is_safe_during_rollback(bool component)
    {
        var components = new ComponentRegistry();
        var templates = new AddinProjectTemplateService();
        object service = component ? components : templates;
        object contribution = component ? Substitute.For<IComponentClass>() : Substitute.For<IAddinProjectTemplate>();
        var removed = 0;
        components.ComponentUnregistered += _ => removed++;
        using (new AddinInitializationScope())
        {
            var handle = service.GetType().GetMethod("Register")!.Invoke(service, [contribution]) as IDisposable;
            handle.Should().NotBeNull();
            handle!.Dispose();
        }
        if (component)
        {
            components.Components.Should().NotContain((IComponentClass)contribution);
            removed.Should().Be(1);
        }
        else templates.Templates.Should().BeEmpty();
    }
}
