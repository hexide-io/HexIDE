using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;

namespace HexIDE.Integration.Tests;

public class CompositionRootTests
{
    [AvaloniaFact]
    public async Task Root_returns_the_same_view_model_on_each_read()
    {
        await using var owned = new RealComposition();
        var first = owned.Composition.Root;

        owned.Composition.Root.Should().BeSameAs(first);
    }

    [AvaloniaFact]
    public async Task Reading_Root_again_keeps_the_saved_Immediate_pane_in_its_dock()
    {
        var path = Path.Combine(UserDataPath.Directory, "dock-layout.json");
        var previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var layout = LayoutManifest.Default with
        {
            Tools = LayoutManifest.Default.Tools.Select(t => t.Key == "immediate" ? t with { Open = true } : t).ToArray(),
        };
        try
        {
            File.WriteAllText(path, LayoutManifestJson.Serialize(layout));
            await using var owned = new RealComposition();
            var first = owned.Composition.Root;
            var owner = first.Immediate.Owner;
            owner.Should().NotBeNull("the saved layout opens the Immediate pane (#4587)");

            var second = owned.Composition.Root;

            second.Immediate.Should().BeSameAs(first.Immediate);
            first.Immediate.Owner.Should().BeSameAs(owner);
        }
        finally
        {
            if (previous is null) File.Delete(path);
            else File.WriteAllBytes(path, previous);
        }
    }
}
