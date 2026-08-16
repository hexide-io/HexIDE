using System.Collections.ObjectModel;

namespace HexIDE.Debugging;

/// <summary>
/// How a watch behaves — VB6's three Watch Types (Debug → Add Watch). In P6a only <see cref="Expression"/> is
/// functional (evaluate + display in the Watches window); the two Break-* types are stored + shown but their
/// gate-side "break" behaviour lands in P6b.
/// </summary>
public enum WatchType
{
    /// <summary>Display the expression's value at each break.</summary>
    Expression,
    /// <summary>(P6b) Break when the expression coerces to True.</summary>
    BreakWhenTrue,
    /// <summary>(P6b) Break when the expression's value changes.</summary>
    BreakWhenChanged,
}

/// <summary>One watch definition: an expression, its <see cref="WatchType"/>, and the context string it was added
/// in (VB6 shows this — e.g. "Form1.Calc" or "(All Procedures)"). Mutable so an Edit can rewrite it.</summary>
public sealed class WatchExpression
{
    public WatchExpression(string expression, WatchType type, string context)
    {
        Expression = expression;
        Type = type;
        Context = context;
    }

    public string Expression { get; set; }
    public WatchType Type { get; set; }
    public string Context { get; set; }
}

/// <summary>
/// Session-scoped store of the user's watches — one per IDE session (mirrors <see cref="BreakpointService"/>). The
/// Watches window renders + evaluates these; the Add/Edit Watch dialog mutates them. An
/// <see cref="ObservableCollection{T}"/> so the window reacts to add/remove directly.
/// </summary>
public sealed class WatchService
{
    public ObservableCollection<WatchExpression> Watches { get; } = new();

    public void Add(WatchExpression watch) => Watches.Add(watch);
    public void Remove(WatchExpression watch) => Watches.Remove(watch);
    public void Clear() => Watches.Clear();
}
