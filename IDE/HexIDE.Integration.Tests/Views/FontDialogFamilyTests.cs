using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using HexIDE.Controls;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Integration.Tests.Views;

public class FontDialogFamilyTests
{
    [AvaloniaFact]
    public void An_uninstalled_family_is_offered_selected_and_kept_on_OK()
    {
        const string family = "HexIDE missing font #912";
        FontManager.Current.SystemFonts.Select(f => f.Name).Should().NotContain(family);
        var vm = new FontDialogViewModel(new FontDialogResult(new FontFamily(family), FontStyle.Normal, FontWeight.Normal, 12));
        vm.FontFamilies.Should().ContainSingle(f => f == family).And.BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        vm.SelectedFamily.Should().Be(family);

        var window = new Window { Content = new HexIDE.Forms.Views.FontDialog { DataContext = vm } };
        window.Show();
        try
        {
            var families = window.GetLogicalDescendants().OfType<ListBox>().Single(l => ReferenceEquals(l.ItemsSource, vm.FontFamilies));
            families.SelectedItem.Should().Be(family);
            FontDialogResult? answer = null;
            vm.AcceptRequested += result => answer = result;
            vm.Accept();
            answer.Should().NotBeNull();
            answer!.Family.Name.Should().Be(family);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_property_picker_passes_the_plain_font_name()
    {
        var picker = new PropertyFontBox { Font = VBFont.Default };
        picker.InitialFont.Family.ToString().Should().Be(VBFont.Default.FontFamilyName);
    }
}
