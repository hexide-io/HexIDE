using System;
using System.Linq;
using System.Text.Json;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Integration.Tests.Views;

// Opening focus and mnemonics measured in hexide-io/HexIDE#4153 (2026-11-16).
public class FontDialogKeyboardTests
{
    [AvaloniaFact]
    public void Opening_focus_selects_the_font_name_and_OK_is_default()
    {
        WithDialog((window, view, _) =>
        {
            var name = view.FindControl<TextBox>("FontNameBox");
            name.Should().NotBeNull();
            window.FocusManager!.GetFocusedElement().Should().BeSameAs(name);
            name!.SelectedText.Should().Be("Arial");
            view.FindControl<Button>("OkButton")!.IsDefault.Should().BeTrue();
            FontDialogResult? answer = null;
            ((FontDialogViewModel)view.DataContext!).AcceptRequested += result => answer = result;
            UiAutomationDriver.PressKey(name, "Enter", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            answer.Should().NotBeNull();
        });
    }

    [AvaloniaFact]
    public void Typing_a_name_or_style_selects_the_list_and_list_choices_fill_the_boxes()
    {
        WithDialog((window, view, vm) =>
        {
            var name = view.FindControl<TextBox>("FontNameBox");
            var style = view.FindControl<TextBox>("FontStyleBox");
            name.Should().NotBeNull();
            style.Should().NotBeNull();
            var families = view.GetLogicalDescendants().OfType<ListBox>().Single(l => ReferenceEquals(l.ItemsSource, vm.FontFamilies));
            var styles = view.GetLogicalDescendants().OfType<ListBox>().Single(l => ReferenceEquals(l.ItemsSource, vm.FontStyles));
            name!.Focus();
            name.SelectAll();
            window.KeyTextInput(vm.FontFamilies.First(f => !string.Equals(f, "Arial", StringComparison.OrdinalIgnoreCase)));
            style!.Focus();
            style.SelectAll();
            window.KeyTextInput("Bold");
            Dispatcher.UIThread.RunJobs();
            families.SelectedItem.Should().Be(name.Text);
            styles.SelectedItem.Should().Be("Bold");
            families.SelectedItem = "Arial";
            styles.SelectedItem = "Italic";
            Dispatcher.UIThread.RunJobs();
            name.Text.Should().Be("Arial");
            style.Text.Should().Be("Italic");
        });
    }

    [AvaloniaFact]
    public void Every_list_has_its_caption_as_a_name_and_caption_keys_reach_the_controls()
    {
        WithDialog((window, view, _) =>
        {
            var lists = view.GetLogicalDescendants().OfType<ListBox>().ToArray();
            lists.Select(l => ControlAutomationPeer.CreatePeerForElement(l)!.GetName()).Should().BeEquivalentTo("Font:", "Font style:", "Size:");
            var checkBoxes = view.GetLogicalDescendants().OfType<CheckBox>().ToArray();
            checkBoxes.Select(c => ControlAutomationPeer.CreatePeerForElement(c)!.GetName()).Should().BeEquivalentTo("Strikeout", "Underline");
            foreach (var (key, target) in new[] { ("F", "FontNameBox"), ("Y", "FontStyleBox"), ("S", "FontSizeBox") })
            {
                UiAutomationDriver.PressKey(view.FindControl<Button>("OkButton")!, key, "Alt").Success.Should().BeTrue();
                Dispatcher.UIThread.RunJobs();
                var focused = window.FocusManager!.GetFocusedElement();
                var control = view.FindControl<Control>(target)!;
                (ReferenceEquals(focused, control) || control.GetLogicalDescendants().Any(c => ReferenceEquals(c, focused))).Should().BeTrue($"Alt+{key} should focus {target}, but focused {focused}");
            }
        });
    }

    [AvaloniaFact]
    public void Caption_mnemonics_follow_each_shipped_packs_convention_and_are_unique()
    {
        foreach (var pack in LanguageManifest.Packs)
        {
            using var stream = AssetLoader.Open(new Uri($"avares://HexIDE/Localization/Packs/{pack.Id}.json"));
            var strings = JsonSerializer.Deserialize(stream, LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!;
            var captions = new[] { "Label.Font", "Label.FontStyle", "Label.Size", "Strikeout", "Underline", "Label.Script" }
                .Select(key => strings["Str.Dialog.Font." + key]).ToArray();
            var mnemonicKeys = captions.Where(c => c.Contains('_')).Select(c => char.ToUpperInvariant(c[c.IndexOf('_') + 1])).ToArray();
            mnemonicKeys.Should().OnlyHaveUniqueItems($"{pack.Id} font controls need distinct access keys");
            if (pack.Id is not ("ar" or "bg" or "el" or "he" or "ko" or "ru" or "th" or "uk"))
                mnemonicKeys.Should().HaveCount(6, $"{pack.Id} uses caption mnemonics");
        }
    }

    private static void WithDialog(Action<Window, FontDialog, FontDialogViewModel> check)
    {
        var vm = new FontDialogViewModel(new FontDialogResult(new FontFamily("Arial"), FontStyle.Normal, FontWeight.Normal, 12, false, false));
        var view = new FontDialog { DataContext = vm };
        var window = new WindowManager(Substitute.For<ILocalizationService>()).MakeDialogWindow("Font", view);
        ContainerRuntimeHarness.MergeAppResources(window);
        using (var strings = AssetLoader.Open(new Uri("avares://HexIDE/Localization/Packs/en.json")))
            foreach (var (key, value) in JsonSerializer.Deserialize(strings, LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!)
                window.Resources[key] = value;
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            check(window, view, vm);
        }
        finally { window.Close(); }
    }
}
