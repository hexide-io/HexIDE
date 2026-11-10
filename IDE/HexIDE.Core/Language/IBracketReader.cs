namespace HexIDE.Language;

/// <summary>Where a module's parentheses are and how deeply each is nested, for the code window to colour.</summary>
public interface IBracketReader
{
    /// <summary>The parentheses in <paramref name="source"/>'s code, in order, leaving out any in a string or a comment.</summary>
    IReadOnlyList<BracketMark> Brackets(string source);
}

/// <summary>One parenthesis.</summary>
/// <param name="Offset">Where it is in the text.</param>
/// <param name="Depth">How many pairs enclose it, from 0, or negative where it has no partner.</param>
public readonly record struct BracketMark(int Offset, int Depth);
