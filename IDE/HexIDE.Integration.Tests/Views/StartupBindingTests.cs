using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Logging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Mvvm.Controls;
using Dock.Settings;
using HexIDE.Integration.Tests.Automation;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

public class StartupBindingTests
{
    [AvaloniaTheory]
    [InlineData("Layout.Id")]
    [InlineData("Layout.Title")]
    [InlineData("Layout.CanDrag")]
    [InlineData("Layout.CanDrop")]
    [InlineData("Layout.DockGroup")]
    public void Dock_theme_accepts_a_layout_that_has_not_arrived(string path)
    {
        var previous = Logger.Sink;
        var log = new BindingLog();
        Logger.Sink = log;
        try
        {
            var window = ToolbarAutomationNameTests.ShowMainView();
            try
            {
                log.Messages.Should().NotContain(m => m.Contains(path, StringComparison.Ordinal));
                var dock = ((MainView)window.Content!).FindControl<DockControl>("DockControl")!;
                DockProperties.GetIsDragEnabled(dock).Should().BeTrue();
                DockProperties.GetIsDropEnabled(dock).Should().BeTrue();
                dock.Layout = new RootDock { Id = "test-dock", Title = "Test dock", CanDrag = false, CanDrop = false, DockGroup = "test-group" };
                Dispatcher.UIThread.RunJobs();
                AutomationProperties.GetAutomationId(dock).Should().Be("test-dock");
                AutomationProperties.GetName(dock).Should().Be("Test dock");
                DockProperties.GetIsDragEnabled(dock).Should().BeFalse();
                DockProperties.GetIsDropEnabled(dock).Should().BeFalse();
                DockProperties.GetDockGroup(dock).Should().Be("test-group");
                dock.Layout = null;
                Dispatcher.UIThread.RunJobs();
                DockProperties.GetIsDragEnabled(dock).Should().BeTrue();
                DockProperties.GetIsDropEnabled(dock).Should().BeTrue();
                log.Messages.Should().NotContain(m => m.Contains(path, StringComparison.Ordinal));
            }
            finally { window.Close(); }
        }
        finally { Logger.Sink = previous; }
    }

    [AvaloniaFact]
    public void Lock_controls_is_unchecked_without_a_focused_form_and_logs_no_error()
    {
        var window = ToolbarAutomationNameTests.ShowMainView();
        var previous = Logger.Sink;
        var log = new BindingLog();
        try
        {
            var button = window.GetVisualDescendants().OfType<ToggleButton>()
                .Single(b => AutomationProperties.GetAutomationId(b) == "FormEditor.LockControls");
            // Only this binding is under test; avoid constructing the shell's services and opening projects.
            var vm = (MainViewViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewViewModel));
            var focus = Substitute.For<IFocusedProjectUtil>();
            typeof(MainViewViewModel).GetField("<FocusedProjectUtil>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(vm, focus);
            Logger.Sink = log;
            button.DataContext = vm;
            Dispatcher.UIThread.RunJobs();
            log.Messages.Should().NotContain(m => m.Contains("FocusedForm", StringComparison.Ordinal));
            button.IsChecked.Should().BeFalse();
            var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "Test"), [], "") { LockControls = true };
            focus.FocusedForm.Returns(form);
            focus.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(focus, new PropertyChangedEventArgs(nameof(focus.FocusedForm)));
            button.IsChecked.Should().BeTrue();
            form.LockControls = false;
            button.IsChecked.Should().BeFalse();
            focus.FocusedForm.Returns((FormDefinition?)null);
            focus.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(focus, new PropertyChangedEventArgs(nameof(focus.FocusedForm)));
            button.IsChecked.Should().BeFalse();
            log.Messages.Should().NotContain(m => m.Contains("FocusedForm", StringComparison.Ordinal));
        }
        finally
        {
            Logger.Sink = previous;
            window.Close();
        }
    }

    private sealed class BindingLog : ILogSink
    {
        public List<string> Messages { get; } = [];
        public bool IsEnabled(LogEventLevel level, string area) => area == LogArea.Binding;
        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Messages.Add(messageTemplate);
        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
            => Messages.Add(messageTemplate + " " + string.Join(" ", propertyValues));
    }
}
