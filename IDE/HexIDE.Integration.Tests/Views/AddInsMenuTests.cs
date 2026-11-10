using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Addins;

namespace HexIDE.Integration.Tests.Views;

public class AddInsMenuTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rebuild_KeepsManagerBeforeContributions(bool contributed)
    {
        await using var owned = new RealComposition();
        var vm = owned.Composition.Root;
        var view = new MainView { DataContext = vm };
        var menu = view.FindControl<MenuItem>("AddInsMenuItem")!;
        var items = vm.AddinMenuService.AddInsItems;
        if (contributed) items.Add(new AddinMenuItemViewModel("Contribution", () => { }, null));
        menu.Items.Count.Should().Be(contributed ? 3 : 1);
        var manager = menu.Items[0].Should().BeOfType<MenuItem>().Subject;
        manager.Should().BeSameAs(view.FindControl<MenuItem>("AddInManagerMenuItem"));
        if (contributed)
        {
            menu.Items[1].Should().BeOfType<Separator>();
            menu.Items[2].Should().BeOfType<MenuItem>().Which.Header.Should().Be("Contribution");
        }
        items.Clear();
        menu.Items.Should().ContainSingle().Which.Should().BeSameAs(manager);
        view.DataContext = null;
    }
}
