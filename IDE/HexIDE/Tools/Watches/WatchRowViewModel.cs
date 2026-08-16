using System;
using System.Collections.Generic;
using System.Linq;
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
    public string Context => Watch.Context;

    [Notify] private string _value;
    [Notify] private string _typeName = string.Empty;
    [Notify] private IReadOnlyList<LocalsVariableNode> _children = Array.Empty<LocalsVariableNode>();

    private string OutOfContext => _localization.GetString("Str.Tool.Watches.OutOfContext");

    /// <summary>Apply a fresh evaluation (from <see cref="IDebugController.EvaluateWatchAsync"/>), or null to blank
    /// the value columns (resumed / not in Break mode).</summary>
    public void Update(DebugEvalResult? result)
    {
        if (result is null)
        {
            Value = OutOfContext;
            TypeName = string.Empty;
            Children = Array.Empty<LocalsVariableNode>();
            return;
        }
        Value = result.Display;
        TypeName = result.TypeName;
        Children = result.Node.Expand().Select(c => new LocalsVariableNode(c, _localization)).ToList();
    }
}
