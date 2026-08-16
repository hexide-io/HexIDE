using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Mvvm.Controls;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

/// <summary>
/// The Locals window — a columned tree (Expression / Value / Type) of the paused frame's variables. Rebuilds from
/// <see cref="IDebugController.GetLocals"/> on every <see cref="IDebugController.Stopped"/> and clears on
/// <see cref="IDebugController.Continued"/>. Reads live execution state via the runtime inspector; it performs no
/// static analysis (in-bounds under the CST-not-AST limit).
///
/// Rendered with a native Avalonia <c>TreeView</c> (MIT) — deliberately NOT TreeDataGrid, which is a commercial
/// "Avalonia Accelerate" product in 12.x (see Directory.Packages.props). Columns are a per-row template.
/// </summary>
public partial class LocalsToolViewModel : Tool
{
    private readonly ILocalizationService _localization;

    // The "<Ready>" header shown when not paused (matches the previous stub's placeholder).
    private string ReadyContext => _localization.GetString("Str.Tool.Locals.ContextReady");

    /// <summary>Header line — "Module.Procedure" while paused, "&lt;Ready&gt;" otherwise. (Named ContextLabel, not
    /// Context, because Dock's <see cref="Tool"/> base already owns a <c>Context</c> member.)</summary>
    [Notify] private string _contextLabel = string.Empty;

    /// <summary>The top-level Locals rows (bound to the TreeView; each node lazily yields its children).</summary>
    [Notify] private IReadOnlyList<LocalsVariableNode> _roots = Array.Empty<LocalsVariableNode>();

    public LocalsToolViewModel(ILocalizationService localization, IDebugController debugController)
    {
        _localization = localization;
        localization.BindTitle(this, "Str.Tool.Locals.Title");
        CanPin = false;
        CanClose = true;
        _contextLabel = ReadyContext;

        // Update synchronously: the controller raises Stopped/Continued from the interpreter's own (UI-thread)
        // execution, so we are already on the UI thread — same contract the CodeEditorView current-line bridge relies on.
        debugController.Stopped += _ => Refresh(debugController);
        debugController.Continued += () => Clear();
    }

    // Rebuild the tree from the paused frame, PRESERVING which nodes were expanded (D10): snapshot the expanded
    // paths from the old tree, rebuild, then re-expand matching paths. The lazy child providers keep the rebuild
    // cheap — only realized (expanded) subtrees are walked.
    private void Refresh(IDebugController debugController)
    {
        var scope = debugController.GetLocals();
        if (scope is null)
        {
            Clear();
            return;
        }

        var expanded = new HashSet<string>(StringComparer.Ordinal);
        CollectExpanded(Roots, string.Empty, expanded);

        ContextLabel = scope.Context;
        var roots = scope.Locals.Select(n => new LocalsVariableNode(n, _localization)).ToList();
        RestoreExpanded(roots, string.Empty, expanded);
        Roots = roots;
    }

    // Record the path (chain of Expression names) of every EXPANDED node. An expanded node's Children are already
    // realized, so recursing into them forces no extra work; a collapsed node is skipped (its subtree isn't walked).
    private static void CollectExpanded(IReadOnlyList<LocalsVariableNode> nodes, string prefix, HashSet<string> into)
    {
        foreach (var node in nodes)
        {
            if (!node.IsExpanded)
                continue;
            var path = prefix + "/" + node.Expression;
            into.Add(path);
            CollectExpanded(node.Children, path, into);
        }
    }

    // Re-expand any node whose path was expanded before the rebuild. Top-down: setting IsExpanded realizes the node's
    // children (lazily), so deeper restores can then match. Robust to a node that no longer exists (just not found).
    private static void RestoreExpanded(IReadOnlyList<LocalsVariableNode> nodes, string prefix, HashSet<string> expanded)
    {
        foreach (var node in nodes)
        {
            var path = prefix + "/" + node.Expression;
            if (node.HasChildren && expanded.Contains(path))
            {
                node.IsExpanded = true;
                RestoreExpanded(node.Children, path, expanded);
            }
        }
    }

    private void Clear()
    {
        ContextLabel = ReadyContext;
        Roots = Array.Empty<LocalsVariableNode>();
    }
}
