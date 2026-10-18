using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using HexIDE.Dap;
using HexIDE.Debugging;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

/// <summary>
/// One top-level row in the Watches tree — a UI wrapper over a <see cref="WatchExpression"/> plus its most recent
/// evaluation. The Expression + Context columns come from the definition; Value / Type / expandable Children come
/// from the last <see cref="DebugEvalResult"/> (reusing <see cref="LocalsVariableNode"/> for the object/array/UDT
/// child tree, exactly as Locals does). When not in Break mode the value shows a localized "&lt;Out of context&gt;".
/// </summary>
public partial class WatchRowViewModel
{
    private readonly ILocalizationService _localization;

    public WatchRowViewModel(WatchExpression watch, ILocalizationService localization)
    {
        Watch = watch;
        _localization = localization;
        _value = OutOfContext;
    }

    /// <summary>The underlying definition (used by Edit / Delete).</summary>
    public WatchExpression Watch { get; }

    public string Expression => Watch.Expression;
    private string? externalContext;
    public string Context => externalContext ?? Watch.Context;
    public void SetExternalContext(string context)
    {
        externalContext = context;
        PropertyChanged?.Invoke(this, new(nameof(Context)));
    }

    [Notify] private string _value;
    [Notify] private string _typeName = string.Empty;
    private IReadOnlyList<LocalsVariableNode> _children = Array.Empty<LocalsVariableNode>();
    private LocalsVariableNode? remoteRoot;
    [Notify] private bool _isExpanded;
    public IReadOnlyList<LocalsVariableNode> Children => remoteRoot?.Children ?? _children;
    private void OnIsExpandedChanged() { if (remoteRoot is not null) remoteRoot.IsExpanded = IsExpanded; }
    private void RemoteChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Children)) PropertyChanged?.Invoke(this, args);
    }

    public void UpdateExternal(ExternalVariable variable, DapStop stop, ExternalDebugService debugger)
    {
        Update(null);
        Value = variable.Value;
        TypeName = variable.Type;
        remoteRoot = new(variable, stop, debugger, _localization);
        remoteRoot.PropertyChanged += RemoteChanged;
        PropertyChanged?.Invoke(this, new(nameof(Children)));
        OnIsExpandedChanged();
    }

    public void UpdateError(string message) { Update(null); Value = message; }

    private string OutOfContext => _localization.GetString("Str.Tool.Watches.OutOfContext");

    /// <summary>Apply a fresh evaluation (from <see cref="IDebugController.EvaluateWatchAsync"/>), or null to blank
    /// the value columns (resumed / not in Break mode).</summary>
    public void Update(DebugEvalResult? result)
    {
        if (remoteRoot is not null) remoteRoot.PropertyChanged -= RemoteChanged;
        remoteRoot = null;
        if (result is null)
        {
            Value = OutOfContext;
            TypeName = string.Empty;
            _children = Array.Empty<LocalsVariableNode>();
            PropertyChanged?.Invoke(this, new(nameof(Children)));
            return;
        }
        Value = result.Display;
        TypeName = result.TypeName;
        _children = result.Node.Expand().Select(c => new LocalsVariableNode(c, _localization)).ToList();
        PropertyChanged?.Invoke(this, new(nameof(Children)));
    }
}
