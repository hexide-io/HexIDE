using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A class says where its control goes when its tool is double-clicked, through <see cref="IDesignerAddedPlacement"/>
/// (#5681). The class's code is the provider's or an add-in's, so the designer contains the call: a throw, or bounds it
/// can't place and save, is logged, and the control goes where a class without the contract puts it.
/// </summary>
/// <remarks>
/// A CommandButton centred on a 6000 x 4500 twip form on an 8-pixel grid saves as Left 2400, Top 2040, Width 1215,
/// Height 495: <c>FormatCommandTwipsTests</c>' measured row for the class (#929, #1473). Off the grid it's 2393 and 2003,
/// half the form less half the button, in whole twips.
/// </remarks>
public class AddedPlacementTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    // A CommandButton whose class answers the placement contract its own way.
    private sealed class PlacedButton(Func<DesignerBounds?> bounds, Func<bool> onGrid)
        : CommandButtonComponentClass, IDesignerAddedPlacement
    {
        public DesignerBounds? AddedBounds(double formWidth, double formHeight) => bounds();
        public bool AddedOnGrid => onGrid();
    }

    private sealed class Captured : Serilog.Core.ILogEventSink, IDisposable
    {
        private readonly Serilog.ILogger previous = Serilog.Log.Logger;
        public List<Serilog.Events.LogEvent> Events { get; } = [];
        public Captured() => Serilog.Log.Logger = new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(this).CreateLogger();
        public void Emit(Serilog.Events.LogEvent logEvent) { lock (Events) Events.Add(logEvent); }
        public void Dispose() => Serilog.Log.Logger = previous;

        public List<Serilog.Events.LogEventLevel> Reports()
        {
            lock (Events)
                return Events.Where(e => e.MessageTemplate.Text.Contains("failed its", StringComparison.Ordinal)
                        && e.Properties.TryGetValue("Contract", out var contract)
                        && contract.ToString() == "\"IDesignerAddedPlacement\"")
                    .Select(e => e.Level).ToList();
        }
    }

    private const string Source =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n" +
        "   ClientHeight    =   4500\r\n" +
        "   ClientLeft      =   60\r\n" +
        "   ClientTop       =   345\r\n" +
        "   ClientWidth     =   6000\r\n" +
        "   ScaleHeight     =   4500\r\n" +
        "   ScaleWidth      =   6000\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    private static (FormEditViewModel Vm, FormDefinition Form) Open(bool alignToGrid = true)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, Source, new NullSink())!;
        project.AddForm(form);
        var settings = Substitute.For<ISettingsService>();
        settings.GridWidth.Returns(8);
        settings.GridHeight.Returns(8);
        settings.AlignToGrid.Returns(alignToGrid);
        // A toolbox, because adding a control puts the pointer tool back.
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            InBoxRegistry.Create(), Substitute.For<ILocalizationService>());
        var vm = new FormEditViewModel(toolbox, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!,
            settings, Substitute.For<ILocalizationService>()).Initialize(form);
        return (vm, form);
    }

    private static (string Left, string Top, string Width, string Height) Added(IDesignerComponent componentClass,
        bool alignToGrid = true)
    {
        var (vm, form) = Open(alignToGrid);
        vm.SpawnControlCenter(componentClass);
        var name = vm.AllComponents.Single(c => ReferenceEquals(c.Instance.BaseClass, componentClass)).Name;
        vm.CommitLayout();
        var (text, _) = new FormSerializer().Serialize(form, "Form1.frm");
        var block = text[text.IndexOf($" {name} \r\n", StringComparison.Ordinal)..];
        string Saved(string property) =>
            Regex.Match(block, $@"^\s*{property}\s*=\s*(\S+)", RegexOptions.Multiline).Groups[1].Value;
        return (Saved("Left"), Saved("Top"), Saved("Width"), Saved("Height"));
    }

    private static readonly (string, string, string, string) Centred = ("2400", "2040", "1215", "495");

    [AvaloniaFact]
    public void A_class_without_the_contract_is_centred_and_put_on_the_grid()
    {
        Added(CommandButtonComponentClass.Instance).Should().Be(Centred);
    }

    [AvaloniaFact]
    public void A_class_that_gives_bounds_has_its_control_placed_there()
    {
        Added(new PlacedButton(() => new DesignerBounds(10, 20, 30, 40), () => true))
            .Should().Be(("150", "300", "450", "600"));
    }

    [AvaloniaFact]
    public void A_class_that_gives_no_bounds_has_its_control_centred_as_any_other()
    {
        Added(new PlacedButton(() => null, () => true)).Should().Be(Centred);
    }

    [AvaloniaFact]
    public void A_class_that_keeps_its_control_off_the_grid_is_centred_in_whole_twips()
    {
        Added(new PlacedButton(() => null, () => false)).Should().Be(("2393", "2003", "1215", "495"));
    }

    [AvaloniaFact]
    public void Align_to_Grid_off_leaves_every_class_off_the_grid()
    {
        Added(CommandButtonComponentClass.Instance, alignToGrid: false).Should().Be(("2393", "2003", "1215", "495"));
    }

    [AvaloniaFact]
    public void A_throw_for_its_bounds_is_logged_and_the_control_is_centred()
    {
        DesignerClassCalls.ForgetReported();
        using var log = new Captured();
        var throwing = new PlacedButton(() => throw new InvalidOperationException("no bounds"), () => true);

        Added(throwing).Should().Be(Centred);
        Added(throwing).Should().Be(Centred);

        log.Reports().Should().Equal(Serilog.Events.LogEventLevel.Warning, Serilog.Events.LogEventLevel.Debug);
    }

    [AvaloniaFact]
    public void A_throw_for_whether_it_goes_on_the_grid_is_logged_and_the_grid_applies()
    {
        DesignerClassCalls.ForgetReported();
        using var log = new Captured();

        Added(new PlacedButton(() => null, () => throw new InvalidOperationException("no answer"))).Should().Be(Centred);

        log.Reports().Should().Equal(Serilog.Events.LogEventLevel.Warning);
    }

    // Each is bounds the designer can't place, or can't save as whole twips: the control is centred instead, and logged.
    public static TheoryData<double, double, double, double> Unusable => new()
    {
        { double.NaN, 20, 30, 40 },
        { 10, double.PositiveInfinity, 30, 40 },
        { 10, 20, -1, 40 },
        { 10, 20, 30, -0.5 },
        { 1e12, 20, 30, 40 },
        { 10, 20, 30, 1e12 },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Unusable))]
    public void Bounds_it_cannot_place_are_logged_and_the_control_is_centred(double left, double top, double width,
        double height)
    {
        DesignerClassCalls.ForgetReported();
        using var log = new Captured();

        Added(new PlacedButton(() => new DesignerBounds(left, top, width, height), () => true)).Should().Be(Centred);

        log.Reports().Should().Equal(Serilog.Events.LogEventLevel.Warning);
    }
}
