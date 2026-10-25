using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Addins;

namespace HexIDE.Integration.Tests.Views;

public class AddInsMenuTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rebuild_KeepsManagerBeforeContributions(bool contributed)
    {
        var view = new MainView();
        var menu = view.FindControl<MenuItem>("AddInsMenuItem")!;
        var items = new ObservableCollection<AddinMenuItemViewModel>();
        if (contributed) items.Add(new AddinMenuItemViewModel("Contribution", () => { }, null));
        var rebuild = typeof(MainView).GetMethod("RebuildAddInsMenu", BindingFlags.Instance | BindingFlags.NonPublic)!;

        rebuild.Invoke(view, [items]);

        menu.Items.Count.Should().Be(contributed ? 3 : 1);
        var manager = menu.Items[0].Should().BeOfType<MenuItem>().Subject;
        manager.Should().BeSameAs(view.FindControl<MenuItem>("AddInManagerMenuItem"));
        if (contributed)
        {
            menu.Items[1].Should().BeOfType<Separator>();
            menu.Items[2].Should().BeOfType<MenuItem>().Which.Header.Should().Be("Contribution");
        }
        items.Clear();
        rebuild.Invoke(view, [items]);
        menu.Items.Should().ContainSingle().Which.Should().BeSameAs(manager);
    }
}
