using HexIDE.Addins;
using HexIDE.Runtime;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.Integration.Tests.Addins;

/// <summary>
/// Who may register what draws the designer's controls, and what a registration may take out (#5591). Whatever draws is given
/// a place in the designer's tree, so this is deny by default. Each test reads what the service then holds, not only what
/// the call returned.
/// </summary>
public class RegisterDesignerPicturesTests
{
    private sealed class Surface : IDesignerDrawingSurface
    {
        public void Attach(object visual) { }
        public void Detach(object visual) { }
        public void UpdateLayout() { }
    }

    [AvaloniaFact]
    public void The_Basic_provider_registers_its_drawer_last_so_a_refusal_leaves_its_themes_text_and_controls()
    {
        var host = Substitute.For<IHexIdeHost>();
        host.RegisterDesignerPictures(Arg.Any<Func<IDesignerDrawingSurface, IDesignerPictures>>())
            .Returns(_ => throw new InvalidOperationException("refused"));
        using var provider = new BasicProvider();

        FluentActions.Invoking(() => provider.Initialize(host)).Should().Throw<InvalidOperationException>().WithMessage("refused");

        host.Received(BasicProvider.Resources.Count).RegisterResources(Arg.Any<Uri>());
        host.Received(1).RegisterLanguagePacks(Arg.Any<Uri>());
        host.Received(BasicProvider.Components.Count).RegisterComponent(Arg.Any<IComponentClass>());
        host.Received(BasicProvider.Toolbox.Count).RegisterToolboxItem(Arg.Any<IComponentClass>(), Arg.Any<string?>());
        // VB6's projects too (#5625): a refused drawer mustn't cost every .vbp.
        host.Received(1).RegisterProjectType(Arg.Is<AddinProjectType>(type => type.FileNames.SequenceEqual(new[] { "*.vbp" })));
        host.Received(1).RegisterDesignerPictures(Arg.Is<Func<IDesignerDrawingSurface, IDesignerPictures>>(
            pictures => pictures(new Surface()) is InProcessDesignerPictures));
    }
}
