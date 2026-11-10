using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Reactive;
using Avalonia.Threading;
using HexIDE.Controls;
using HexIDE.Forms.ViewModels;
using HexIDE.Language;
using HexIDE.Runtime.ProjectElements;
using HexIDE.TwinBasic;

namespace HexIDE.Forms.Views;

public partial class CodeEditorView
{
    // VBA's constants, not measured here: there is no VBA oracle. Preview only (#1113). Microsoft's page calls them
    // True, but VB6 measures 1 where its own documentation says True, and so does #1691's, so 1 it is.
    private static readonly IReadOnlyDictionary<string, object?> Vba32BuiltIns =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["VBA6"] = 1, ["VBA7"] = 1, ["Win32"] = 1 };

    private static readonly IReadOnlyDictionary<string, object?> Vba64BuiltIns =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["VBA6"] = 1, ["VBA7"] = 1, ["Win32"] = 1, ["Win64"] = 1 };

    private readonly DispatcherTimer _conditionalTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private InactiveCodeColorizer? _inactiveColorizer;
    private IDisposable? _inactiveBrushSubscription;
    private IReadOnlyList<LineRange> _inactiveLines = [];
    private int _evaluatedLineCount;

    // Evaluation runs synchronously on the dispatcher; once its timer is drained, the colours can be asserted.
    internal bool IsConditionalCompilationPending => _conditionalTimer.IsEnabled;

    private void InstallConditionalCompilation()
    {
        var colorizer = new InactiveCodeColorizer(() => _inactiveLines);
        _inactiveColorizer = colorizer;
        TextEditor.TextArea.TextView.LineTransformers.Add(colorizer);
        _inactiveBrushSubscription = this.GetResourceObservable("InactiveCodeTextBrush")
            .Subscribe(new AnonymousObserver<object?>(value =>
            {
                colorizer.Brush = value as IBrush;
                TextEditor.TextArea.TextView.Redraw();
            }));
        _conditionalTimer.Tick += OnConditionalTick;
        TextEditor.TextChanged += OnTextChangedForConditionalCompilation;
        ConditionalTargetCombo.SelectionChanged += ScheduleConditionalCompilation;
        RefreshConditionalCompilation();
    }

    // Most keystrokes leave every line where it was, so the greying stays until the next evaluation rather than flashing
    // off and on with each one. An edit that adds or removes lines clears it: the old line numbers would grey the wrong
    // text while the evaluation waits.
    private void OnTextChangedForConditionalCompilation(object? sender, EventArgs e)
    {
        if (TextEditor.Document.LineCount == _evaluatedLineCount) Reschedule();
        else ScheduleConditionalCompilation(sender, e);
    }

    private void ScheduleConditionalCompilation(object? sender, EventArgs e)
    {
        _inactiveLines = [];
        TextEditor.TextArea.TextView.Redraw();
        Reschedule();
    }

    private void Reschedule()
    {
        _conditionalTimer.Stop();
        _conditionalTimer.Start();
    }

    private void OnConditionalTick(object? sender, EventArgs e)
    {
        _conditionalTimer.Stop();
        RefreshConditionalCompilation();
    }

    private void RefreshConditionalCompilation()
    {
        if (DataContext is not CodeEditorViewModel vm) return;
        var source = vm.Document.Text;
        _evaluatedLineCount = vm.Document.LineCount;
        ConditionalTargetCombo.IsVisible = Regex.IsMatch(source, @"^\s*#If\b", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
        var project = vm.ModuleDefinition?.Owner ?? vm.FormDefinition?.Owner;
        var twinBasic = vm.ConditionalTargetIndex is 3 or 4;
        var builtIns = vm.ConditionalTargetIndex switch
        {
            1 => Vba32BuiltIns,
            2 => Vba64BuiltIns,
            3 or 4 => TwinBasicBuiltIns(vm.ConditionalTargetIndex == 4, project?.ProjectType),
            _ => null,
        };
        // A CondComp line vb6.exe refuses stops the program before it runs, so no line can be said to be left out.
        var arguments = project is null ? ConditionalConstants.NoArguments : project.ConditionalArguments();
        _inactiveLines = vm.ConditionalCode is { } reader && arguments is not null
            ? reader.InactiveLines(source, new ConditionalConstants(arguments)
            {
                BuiltIns = builtIns,
                CallsReadZero = twinBasic,
                NullIsUndetermined = twinBasic,
            })
            : [];
        TextEditor.TextArea.TextView.Redraw();
    }

    private static IReadOnlyDictionary<string, object?> TwinBasicBuiltIns(bool win64, VBProjectType? projectType)
    {
        var buildType = projectType switch
        {
            VBProjectType.EXE => "Standard EXE",
            VBProjectType.OleDll => "ActiveX DLL",
            VBProjectType.Control => "ActiveX Control",
            _ => null,
        };
        return TwinBasicCompilationConstants.BuiltIns(win64, build: null, buildType);
    }

    private void RemoveConditionalCompilation()
    {
        _conditionalTimer.Stop();
        _conditionalTimer.Tick -= OnConditionalTick;
        TextEditor.TextChanged -= OnTextChangedForConditionalCompilation;
        ConditionalTargetCombo.SelectionChanged -= ScheduleConditionalCompilation;
        _inactiveBrushSubscription?.Dispose();
        _inactiveBrushSubscription = null;
        if (_inactiveColorizer is not null)
            TextEditor.TextArea.TextView.LineTransformers.Remove(_inactiveColorizer);
        _inactiveColorizer = null;
        _inactiveLines = [];
    }
}
