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
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Interpreter.ConditionalCompilation;

namespace HexIDE.Forms.Views;

public partial class CodeEditorView
{
    private readonly DispatcherTimer _conditionalTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private InactiveCodeColorizer? _inactiveColorizer;
    private IDisposable? _inactiveBrushSubscription;
    private IReadOnlyList<LineRange> _inactiveLines = [];
    private int _evaluatedLineCount;

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
        var builtIns = vm.ConditionalTargetIndex switch
        {
            1 => CompilationConstants.Vba32BuiltIns,
            2 => CompilationConstants.Vba64BuiltIns,
            _ => CompilationConstants.Vb6BuiltIns,
        };
        var project = vm.ModuleDefinition?.Owner ?? vm.FormDefinition?.Owner;
        try
        {
            var arguments = project is null ? CompilationArguments.None : CompilationArguments.ForProject(project);
            var result = ConditionalCompiler.Preprocess(source, new CompilationConstants(arguments, builtIns), vm.Title);
            _inactiveLines = result.InactiveLines.Where(range =>
                !result.UndeterminedLines.Any(unknown => unknown.First <= range.Last && unknown.Last >= range.First)).ToArray();
        }
        catch (VBCompileErrorException)
        {
            _inactiveLines = [];
        }
        TextEditor.TextArea.TextView.Redraw();
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
