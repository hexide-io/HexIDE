using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.VisualTree;
using Avalonia.Platform;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.Controls;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

// Design-time measurements at 96 dpi: https://github.com/hexide-io/HexIDE/issues/4153.
public class FontDialogSizeTests
{
    [AvaloniaTheory]
    [InlineData("13", 12.75)]
    [InlineData("10", 9.75)]
    [InlineData("1", 1.5)]
    [InlineData("1638", 1638)]
    [InlineData("1639", 1638.75)]
    [InlineData("2048", 2048.25)]
    [InlineData("2049", 2049)]
    [InlineData("2160", 2160)]
    [InlineData("2161", 2160.75)]
    [InlineData("2200", 2199.75)]
    [InlineData("2236", 2235.75)]
    [InlineData("0", 8.25)]
    [InlineData("", 8.25)]
    public void Typed_sizes_reach_the_saved_font(string text, double expected)
    {
        WithDialog((window, view, vm, font) =>
        {
            TypeSize(window, view, text);
            FontDialogResult? answer = null;
            vm.AcceptRequested += result => answer = result;
            UiAutomationDriver.PressKey(view.FindControl<TextBox>("FontSizeBox")!, "Enter", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            answer.Should().NotBeNull();
            var chosen = PropertyFontBox.Chosen(font, answer!);
            chosen.Size.Should().Be(expected);
            RoundTrip(chosen).Size.Should().Be(expected);
        });
    }

    [AvaloniaTheory]
    [InlineData("8.5", "Size must be a number.")]
    [InlineData("13.7", "Size must be a number.")]
    [InlineData("abc", "Size must be a number.")]
    [InlineData("-1", "Size must be between 1 and 2236.")]
    [InlineData("2237", "Size must be between 1 and 2236.")]
    [InlineData("2239", "Size must be between 1 and 2236.")]
    [InlineData("2242", "Size must be between 1 and 2236.")]
    [InlineData("2249", "Size must be between 1 and 2236.")]
    [InlineData("2250", "Size must be between 1 and 2236.")]
    [InlineData("2251", "Size must be between 1 and 2236.")]
    [InlineData("2300", "Size must be between 1 and 2236.")]
    [InlineData("2457", "Size must be between 1 and 2236.")]
    [InlineData("2458", "Size must be between 1 and 2236.")]
    [InlineData("2600", "Size must be between 1 and 2236.")]
    [InlineData("2800", "Size must be between 1 and 2236.")]
    [InlineData("3000", "Size must be between 1 and 2236.")]
    [InlineData("3276", "Size must be between 1 and 2236.")]
    [InlineData("3277", "Size must be between 1 and 2236.")]
    [InlineData("4096", "Size must be between 1 and 2236.")]
    [InlineData("5000", "Size must be between 1 and 2236.")]
    [InlineData("999999999999999999999999", "Size must be between 1 and 2236.")]
    public void Refusal_keeps_the_dialog_and_text_and_restores_size_focus(string text, string expected)
    {
        WithDialog((window, view, vm, _) =>
        {
            TypeSize(window, view, text);
            var accepted = false;
            vm.AcceptRequested += _ => accepted = true;
            UiAutomationDriver.PressKey(view.FindControl<TextBox>("FontSizeBox")!, "Enter", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            var messageWindow = window.OwnedWindows.Single();
            messageWindow.Title.Should().Be("Font");
            var message = (HexIDE.Dialogs.MessageBox)messageWindow.Content!;
            message.Text.Should().Be(expected);
            UiAutomationDriver.PressKey(message, "Enter", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            accepted.Should().BeFalse();
            window.IsVisible.Should().BeTrue();
            var size = view.FindControl<TextBox>("FontSizeBox")!;
            size.Text.Should().Be(text);
            window.FocusManager!.GetFocusedElement().Should().BeSameAs(size);
        });
    }

    [AvaloniaFact]
    public void Size_box_is_named_and_Alt_S_reaches_it_and_choices_synchronize()
    {
        WithDialog((window, view, vm, _) =>
        {
            var size = view.FindControl<TextBox>("FontSizeBox");
            size.Should().NotBeNull();
            ControlAutomationPeer.CreatePeerForElement(size!)!.GetName().Should().Be("Size:");
            UiAutomationDriver.PressKey(view.FindControl<Button>("OkButton")!, "S", "Alt").Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            window.FocusManager!.GetFocusedElement().Should().BeSameAs(size);
            TypeSize(window, view, "10");
            view.FindControl<ListBox>("SizeList")!.SelectedItem.Should().Be(10d);
            vm.PreviewFontSize.Should().Be(13);
            view.FindControl<ListBox>("SizeList")!.SelectedItem = 14d;
            Dispatcher.UIThread.RunJobs();
            size!.Text.Should().Be("14");
            TypeSize(window, view, "13");
            view.FindControl<ListBox>("SizeList")!.SelectedItem.Should().BeNull();
        });
    }

    [AvaloniaFact]
    public void Western_script_is_named_and_reaches_Charset_in_the_saved_form()
    {
        WithDialog((window, view, vm, font) =>
        {
            var script = view.FindControl<ComboBox>("ScriptBox");
            script.Should().NotBeNull();
            ControlAutomationPeer.CreatePeerForElement(script!)!.GetName().Should().Be("Script:");
            script!.SelectedIndex.Should().Be(-1);
            FontDialogResult? answer = null;
            vm.AcceptRequested += result => answer = result;
            UiAutomationDriver.PressKey(view.FindControl<Button>("OkButton")!, "Enter", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            RoundTrip(PropertyFontBox.Chosen(font, answer!)).Charset.Should().Be(1);
            script.SelectedIndex = 0;
            script.SelectedItem.Should().NotBeNull();
            UiAutomationDriver.PressKey(view.FindControl<Button>("OkButton")!, "Enter", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            RoundTrip(PropertyFontBox.Chosen(font, answer!)).Charset.Should().Be(0);
        }, charset: 1);
    }

    [AvaloniaFact]
    public void Script_choices_are_localized_and_follow_the_family_in_the_view()
    {
        WithDialog((window, view, vm, font) =>
        {
            var script = view.FindControl<ComboBox>("ScriptBox")!;
            script.SelectedIndex.Should().Be(1);
            script.GetVisualDescendants().OfType<TextBlock>().Should().Contain(text => text.Text == "Greek");
            window.Resources["Str.Dialog.Font.Script.Greek"] = "Ελληνικά";
            Dispatcher.UIThread.RunJobs();
            script.GetVisualDescendants().OfType<TextBlock>().Should().Contain(text => text.Text == "Ελληνικά");
            FontDialogResult? answer = null;
            vm.AcceptRequested += result => answer = result;
            UiAutomationDriver.PressKey(view.FindControl<Button>("OkButton")!, "Enter", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            RoundTrip(PropertyFontBox.Chosen(font, answer!)).Charset.Should().Be(161);
            vm.FamilyText = "MS Sans Serif";
            Dispatcher.UIThread.RunJobs();
            script.Items.Count.Should().Be(1);
            script.SelectedIndex.Should().Be(0);
            view.FindControl<ListBox>("SizeList")!.Items.Cast<double>().Should().Contain(15).And.NotContain(9);
            UiAutomationDriver.PressKey(view.FindControl<Button>("OkButton")!, "Enter", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            RoundTrip(PropertyFontBox.Chosen(font, answer!)).Charset.Should().Be(0);
        }, charset: 161, coverage: _ => FontCodePageCoverage.Latin1 | FontCodePageCoverage.Greek);
    }

    [AvaloniaFact]
    public void Closed_script_box_reports_the_localized_selection_to_automation()
    {
        WithDialog((window, view, vm, font) =>
        {
            var script = view.FindControl<ComboBox>("ScriptBox")!;
            script.IsDropDownOpen.Should().BeFalse();
            script.SelectedIndex.Should().Be(1);
            ControlAutomationPeer.CreatePeerForElement(script)!.GetProvider<IValueProvider>()!
                .Value.Should().Be("Greek");
            script.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            ControlAutomationPeer.CreatePeerForElement(script.ContainerFromIndex(1)!)!
                .GetName().Should().Be("Greek");
            script.IsDropDownOpen = false;
            Dispatcher.UIThread.RunJobs();
            ControlAutomationPeer.CreatePeerForElement(script)!.GetProvider<IValueProvider>()!
                .Value.Should().Be("Greek");
        }, charset: 161, coverage: _ => FontCodePageCoverage.Latin1 | FontCodePageCoverage.Greek);
    }

    [AvaloniaFact]
    public void An_unavailable_saved_script_leaves_the_box_empty()
    {
        WithDialog((window, view, vm, font) =>
        {
            var script = view.FindControl<ComboBox>("ScriptBox")!;
            script.SelectedIndex.Should().Be(-1);
            script.PlaceholderText.Should().BeNullOrEmpty();
            script.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible).Should().OnlyContain(text => string.IsNullOrEmpty(text.Text));
        }, charset: 161, coverage: _ => FontCodePageCoverage.Latin1);
    }

    private static VBFont RoundTrip(VBFont font)
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), """
            VERSION 5.00
            Begin VB.Form Form1
            End
            Attribute VB_Name = "Form1"
            """, new Sink())!;
        form.Components.Single().SetProperty(VBProperties.FontProperty, font);
        var saved = new FormSerializer().Serialize(form, "Form1.frm").Item1;
        saved.Should().Contain("Charset         =   " + font.Charset);
        return new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), saved, new Sink())!
            .Components.Single().GetPropertyOrDefault(VBProperties.FontProperty);
    }

    private static void TypeSize(Window window, FontDialog view, string text)
    {
        var size = view.FindControl<TextBox>("FontSizeBox");
        size.Should().NotBeNull();
        size!.Focus();
        size.SelectAll();
        if (text.Length == 0) UiAutomationDriver.PressKey(size, "Back", null).Success.Should().BeTrue();
        else window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }

    private static void WithDialog(Action<Window, FontDialog, FontDialogViewModel, VBFont> check, int charset = 0,
        Func<string, FontCodePageCoverage>? coverage = null)
    {
        var font = new VBFont("Arial", 8.25, charset: charset);
        var initial = new PropertyFontBox { Font = font }.InitialFont;
        var vm = coverage == null ? new FontDialogViewModel(initial) : new FontDialogViewModel(initial, coverage);
        if (coverage != null && !vm.FontFamilies.Contains("MS Sans Serif"))
            ((List<string>)vm.FontFamilies).Add("MS Sans Serif");
        using var english = AssetLoader.Open(new Uri("avares://HexIDE/Localization/Packs/en.json"));
        var stringsByKey = JsonSerializer.Deserialize(english, LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!;
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => stringsByKey[call.Arg<string>()]);
        var manager = new WindowManager(localization);
        var view = manager.CreateFontDialog(vm);
        var window = manager.MakeDialogWindow("Font", view);
        ContainerRuntimeHarness.MergeAppResources(window);
        using (var strings = AssetLoader.Open(new Uri("avares://HexIDE/Localization/Packs/en.json")))
            foreach (var (key, value) in JsonSerializer.Deserialize(strings, LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!)
                window.Resources[key] = value;
        var app = Application.Current!;
        var lifetimeField = typeof(Application).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(IApplicationLifetime));
        var previous = lifetimeField.GetValue(app);
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        desktop.MainWindow.Returns(window);
        desktop.Windows.Returns(new[] { window });
        lifetimeField.SetValue(app, desktop);
        window.Show();
        try { Dispatcher.UIThread.RunJobs(); check(window, view, vm, font); }
        finally
        {
            foreach (var owned in window.OwnedWindows.ToArray()) owned.Close();
            window.Close();
            lifetimeField.SetValue(app, previous);
        }
    }

    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) => throw new InvalidOperationException(error);
    }
}
