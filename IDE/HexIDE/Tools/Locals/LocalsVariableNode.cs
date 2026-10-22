using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;
using HexIDE.Dap;
using HexIDE.Debugging;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

/// <summary>
/// One row in the Locals tree — a UI wrapper over a runtime <see cref="DebugNode"/>. Children are realized
/// LAZILY on first access (which is when the TreeView asks, i.e. when the row is expanded), so a large or cyclic
/// object graph is only walked as far as the user opens it (the runtime inspector also guards depth + cycles).
/// </summary>
public sealed partial class LocalsVariableNode
{
    private readonly DebugNode _node;
    private readonly ILocalizationService _localization;
    private IReadOnlyList<LocalsVariableNode>? _children;
    private ExternalVariable? remote;
    private DapStop? remoteStop;
    private ExternalDebugService? externalDebugger;
    private bool loading;

    public LocalsVariableNode(ExternalVariable variable, DapStop stop, ExternalDebugService debugger, ILocalizationService localization)
        : this(new DebugNode(variable.Name, variable.Value, variable.Type), localization)
    {
        remote = variable;
        remoteStop = stop;
        externalDebugger = debugger;
    }

    /// <summary>Bound TwoWay to the row's <c>TreeViewItem.IsExpanded</c> — the user's expand/collapse flows in, and
    /// the VM sets it to restore expansion after a break-rebuild (D10).</summary>
    [Notify] private bool _isExpanded;

    public LocalsVariableNode(DebugNode node, ILocalizationService localization)
    {
        _node = node;
        _localization = localization;
    }

    /// <summary>The Expression column — the variable/field/element name, or the localized "… N more" marker for a
    /// truncated array tail.</summary>
    public string Expression => _node.TruncatedRemaining > 0
        ? string.Format(_localization.GetString("Str.Tool.Locals.MoreElements"), _node.TruncatedRemaining)
        : _node.Name;

    public string Value => _node.Value;
    public string TypeName => _node.TypeName;
    public bool HasChildren => remote is { Reference: > 0 } || _node.HasChildren;

    public IReadOnlyList<LocalsVariableNode> Children
    {
        get
        {
            if (remote is { Reference: > 0 } && _children is null)
                _children = [new LocalsVariableNode(new DebugNode("…", "", ""), _localization)];
            return _children ??= _node.Expand().Select(c => new LocalsVariableNode(c, _localization)).ToList();
        }
    }

    private void OnIsExpandedChanged()
    {
        if (IsExpanded && remote is { Reference: > 0 } && !loading) _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        loading = true;
        try
        {
            var values = await externalDebugger!.LoadVariablesAsync(remoteStop!, remote!.Reference);
            if (!externalDebugger.IsCurrent(remoteStop!)) return;
            _children = values.Select(value => new LocalsVariableNode(value, remoteStop!, externalDebugger, _localization)).ToArray();
        }
        catch (Exception exception)
        {
            if (!externalDebugger!.IsCurrent(remoteStop!)) return;
            _children = [new LocalsVariableNode(new DebugNode("!", exception.Message, ""), _localization)];
        }
        finally { PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Children))); }
    }
}
