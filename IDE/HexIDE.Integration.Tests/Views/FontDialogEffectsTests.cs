using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Avalonia.Platform;
using System;
using System.Text.Json;
using HexIDE.Localization;
using HexIDE.Automation;
using Avalonia.Threading;
using HexIDE.Controls;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

// Design-time measurements: hexide-io/HexIDE#4153, oracle comment dated 2026-11-16.
public class FontDialogEffectsTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Effects_can_be_toggled_and_saved_for_a_form_and_TextBox(bool underline, bool strikeout)
    {
        var font = new VBFont("Arial", 12);
        var picker = new PropertyFontBox { Font = font };
        var vm = new FontDialogViewModel(picker.InitialFont);
        var view = new FontDialog { DataContext = vm };
        var window = new Window { Content = view };
        ContainerRuntimeHarness.MergeAppResources(window);
        using (var strings = AssetLoader.Open(new Uri("avares://HexIDE/Localization/Packs/en.json")))
            foreach (var (key, value) in JsonSerializer.Deserialize(strings, LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!)
                window.Resources[key] = value;
        window.Show();
        try
        {
            var underlineBox = view.FindControl<CheckBox>("UnderlineBox");
            var strikeoutBox = view.FindControl<CheckBox>("StrikeoutBox");
            underlineBox.Should().NotBeNull();
            strikeoutBox.Should().NotBeNull();
            if (underline) Click(window, underlineBox!);
            if (strikeout) Click(window, strikeoutBox!);
            var decorations = view.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Text == "AaBbCcDdEe 0123").TextDecorations!;
            decorations.Any(d => d.Location == TextDecorationLocation.Underline).Should().Be(underline);
            decorations.Any(d => d.Location == TextDecorationLocation.Strikethrough).Should().Be(strikeout);
            FontDialogResult? answer = null;
            vm.AcceptRequested += result => answer = result;
            Click(window, view.FindControl<Button>("OkButton")!);
            answer.Should().NotBeNull();
            var chosen = PropertyFontBox.Chosen(font, answer!);
            chosen.Underline.Should().Be(underline);
            chosen.Strikethrough.Should().Be(strikeout);

            var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), """
                VERSION 5.00
                Begin VB.Form Form1
                   Begin VB.TextBox Text1
                   End
                End
                Attribute VB_Name = "Form1"
                """, new Sink())!;
            form.Components.Single(c => c.BaseClass is FormComponentClass).SetProperty(VBProperties.FontProperty, chosen);
            form.Components.Single(c => c.BaseClass is TextBoxComponentClass).SetProperty(VBProperties.FontProperty, chosen);
            var saved = new FormSerializer().Serialize(form, "Form1.frm").Item1;
            var reloaded = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), saved, new Sink())!;
            foreach (var component in reloaded.Components)
            {
                component.GetPropertyOrDefault(VBProperties.FontProperty).Underline.Should().Be(underline);
                component.GetPropertyOrDefault(VBProperties.FontProperty).Strikethrough.Should().Be(strikeout);
            }
            saved.Should().Contain(underline ? "Underline       =   -1  'True" : "Underline       =   0   'False");
            saved.Should().Contain(strikeout ? "Strikethrough   =   -1  'True" : "Strikethrough   =   0   'False");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Existing_effects_are_shown_and_survive_OK()
    {
        var font = new VBFont("Arial", 12, underline: true, strikethrough: true);
        var vm = new FontDialogViewModel(new PropertyFontBox { Font = font }.InitialFont);
        var view = new FontDialog { DataContext = vm };
        var window = new Window { Content = view };
        ContainerRuntimeHarness.MergeAppResources(window);
        using (var strings = AssetLoader.Open(new Uri("avares://HexIDE/Localization/Packs/en.json")))
            foreach (var (key, value) in JsonSerializer.Deserialize(strings, LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!)
                window.Resources[key] = value;
        window.Show();
        try
        {
            view.FindControl<CheckBox>("UnderlineBox").Should().NotBeNull();
            view.FindControl<CheckBox>("UnderlineBox")!.IsChecked.Should().BeTrue();
            view.FindControl<CheckBox>("StrikeoutBox")!.IsChecked.Should().BeTrue();
            FontDialogResult? answer = null;
            vm.AcceptRequested += result => answer = result;
            Click(window, view.FindControl<Button>("OkButton")!);
            PropertyFontBox.Chosen(font, answer!).Should().Be(font);

            UiAutomationDriver.PressKey(view.FindControl<Button>("OkButton")!, "U", "Alt").Success.Should().BeTrue();
            UiAutomationDriver.PressKey(view.FindControl<Button>("OkButton")!, "K", "Alt").Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            view.FindControl<CheckBox>("UnderlineBox")!.IsChecked.Should().BeFalse();
            view.FindControl<CheckBox>("StrikeoutBox")!.IsChecked.Should().BeFalse();
            Click(window, view.FindControl<Button>("OkButton")!);
            PropertyFontBox.Chosen(font, answer!).Should().Be(new VBFont("Arial", 12));

            // A caller with no effect fields (runtime CommonDialog) cannot offer edits it discards.
            view.DataContext = new FontDialogViewModel(null);
            Dispatcher.UIThread.RunJobs();
            view.FindControl<CheckBox>("UnderlineBox")!.IsEffectivelyVisible.Should().BeFalse();
            view.FindControl<CheckBox>("StrikeoutBox")!.IsEffectivelyVisible.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    private static void Click(Window window, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var point = control.TranslatePoint(new Point(control is CheckBox ? 8 : control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) => throw new System.InvalidOperationException(error);
    }
}
