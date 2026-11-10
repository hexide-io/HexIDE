namespace HexIDE.Language;

/// <summary>
/// Which of a module's lines its conditional compilation leaves out, for the code window and the archive view to grey.
/// It only answers a question about the text: nothing here runs the module, and the module's text is not changed.
/// </summary>
public interface IConditionalCodeReader
{
    /// <summary>
    /// The lines <paramref name="source"/>'s <c>#If</c>s leave out, less any in a block whose branch cannot be decided.
    /// None when a directive cannot be read.
    /// </summary>
    IReadOnlyList<LineRange> InactiveLines(string source, ConditionalConstants constants);
}

/// <summary>What a module's <c>#If</c>s can name besides its own <c>#Const</c>s.</summary>
/// <param name="Arguments">The project's Conditional Compilation Arguments, by name in any case.</param>
public sealed record ConditionalConstants(IReadOnlyDictionary<string, short> Arguments)
{
    /// <summary>No project arguments.</summary>
    public static readonly IReadOnlyDictionary<string, short> NoArguments = new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The compiler's built-in constants, or null for the ones the reader's own compiler defines. Each value is an
    /// <see cref="int"/> or a <see cref="string"/>, or null for a constant that compiler defines and the project does not record.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? BuiltIns { get; init; }

    /// <summary>A function call in an <c>#If</c> reads as 0, whatever it names, rather than being called.</summary>
    public bool CallsReadZero { get; init; }

    /// <summary>A condition that comes to Null leaves its branches undetermined, so nothing is greyed on a guess.</summary>
    public bool NullIsUndetermined { get; init; }
}
