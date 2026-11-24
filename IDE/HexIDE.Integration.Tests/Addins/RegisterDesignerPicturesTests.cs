using System;
using Avalonia;
using HexIDE.Addins;
using HexIDE.Runtime;
using HexIDE.Runtime.Components;

namespace HexIDE.Integration.Tests.Addins;

/// <summary>
/// Who may register what draws the designer's controls, and what a registration may take out (#5591). Whatever draws is given
/// a place in the designer's tree, so this is deny by default. Each test reads what the service then holds, not only what
/// the call returned.
/// </summary>
public class RegisterDesignerPicturesTests
{
    private static AddinOrigin Owner(string id) => new(id, "Owner " + id) { IsComposed = true };

    private static RegistrationGrant Grant(string id, bool pictures) => new(id, Composed: true) { DesignerPictures = pictures };

    private static AddinDesignerPictureService Service(object scope, params RegistrationGrant[] grants) =>
        new() { Scope = scope, Grants = grants };

    private sealed class Nothing : IDesignerPictures
    {
        public DesignerPicture Draw(ComponentInstance instance, DesignerDrawRequest request) => throw new NotSupportedException();
    }

    private static Func<IDesignerDrawingSurface, IDesignerPictures> Drawer() => _ => new Nothing();

    [AvaloniaFact]
    public void An_owner_whose_grant_doesnt_say_so_or_with_no_grant_is_refused()
    {
        var scope = new object();
        var service = Service(scope, Grant("themes-only", pictures: false));

        FluentActions.Invoking(() => service.Register(Drawer(), Owner("themes-only")))
            .Should().Throw<InvalidOperationException>().WithMessage("Owner themes-only isn't one HexIDE lets draw*");
        FluentActions.Invoking(() => service.Register(Drawer(), Owner("stranger")))
            .Should().Throw<InvalidOperationException>().WithMessage("Owner stranger isn't one HexIDE lets draw*");

        AddinDesignerPictureService.For(scope).Should().BeNull();
    }

    [AvaloniaFact]
    public void A_granted_owner_draws_until_its_registration_is_disposed()
    {
        var scope = new object();
        var drawer = Drawer();
        var registration = Service(scope, Grant("a", pictures: true)).Register(drawer, Owner("a"));

        AddinDesignerPictureService.For(scope).Should().BeSameAs(drawer);
        registration.Dispose();
        AddinDesignerPictureService.For(scope).Should().BeNull();
    }

    [AvaloniaFact]
    public void A_second_drawer_is_refused_naming_both_owners_and_the_first_stands()
    {
        var scope = new object();
        var service = Service(scope, Grant("a", pictures: true), Grant("b", pictures: true));
        var first = Drawer();
        using var held = service.Register(first, Owner("a"));

        FluentActions.Invoking(() => service.Register(Drawer(), Owner("b")))
            .Should().Throw<InvalidOperationException>().WithMessage("Owner b can't draw*Owner a (a) draws them already.");

        AddinDesignerPictureService.For(scope).Should().BeSameAs(first);
    }

    [AvaloniaFact]
    public void A_registration_disposed_again_never_takes_out_a_later_one()
    {
        var scope = new object();
        var service = Service(scope, Grant("a", pictures: true), Grant("b", pictures: true));
        var earlier = service.Register(Drawer(), Owner("a"));
        earlier.Dispose();
        var later = Drawer();
        using var current = service.Register(later, Owner("b"));

        earlier.Dispose();

        AddinDesignerPictureService.For(scope).Should().BeSameAs(later);
    }

    [AvaloniaFact]
    public void The_IDEs_own_host_refuses_it_with_no_owner_to_check()
    {
        var host = new HexIdeHost(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, new AddinResourceService(), new AddinLanguagePackService());

        FluentActions.Invoking(() => host.RegisterDesignerPictures(Drawer()))
            .Should().Throw<InvalidOperationException>().WithMessage("*only through the host an add-in is given.");
    }

    [AvaloniaFact]
    public void Each_application_holds_its_own_drawer_and_the_providers_is_the_test_apps()
    {
        // TestApp registers the provider as the IDE does, so its drawer is the application's, and a scope of a test's own
        // holds nothing.
        AddinDesignerPictureService.For(Application.Current)!.Invoke(new Surface()).Should().BeOfType<InProcessDesignerPictures>();
        AddinDesignerPictureService.For(new object()).Should().BeNull();
    }

    private sealed class Surface : IDesignerDrawingSurface
    {
        public void Attach(object visual) { }
        public void Detach(object visual) { }
        public void UpdateLayout() { }
    }

    [AvaloniaFact]
    public void The_Basic_provider_registers_its_drawer_last_so_a_refusal_leaves_its_themes_and_text()
    {
        var host = Substitute.For<IHexIdeHost>();
        host.RegisterDesignerPictures(Arg.Any<Func<IDesignerDrawingSurface, IDesignerPictures>>())
            .Returns(_ => throw new InvalidOperationException("refused"));
        using var provider = new BasicProvider();

        FluentActions.Invoking(() => provider.Initialize(host)).Should().Throw<InvalidOperationException>().WithMessage("refused");

        host.Received(BasicProvider.Resources.Count).RegisterResources(Arg.Any<Uri>());
        host.Received(1).RegisterLanguagePacks(Arg.Any<Uri>());
        host.Received(1).RegisterDesignerPictures(Arg.Is<Func<IDesignerDrawingSurface, IDesignerPictures>>(
            pictures => pictures(new Surface()) is InProcessDesignerPictures));
    }
}
