namespace HexIDE.Language;

/// <summary>What replacing a document's content came to.</summary>
/// <param name="NewText">The whole buffer to put in place, or null when the replacement was refused.</param>
/// <param name="Refusal">Why it was refused, in words a caller can act on; null when it was not.</param>
/// <param name="HeaderKept">
/// True when the incoming text carried no header of its own, or only part of it, so the document's header was
/// put in front of it. A caller that reports on its write can say so.
/// </param>
public readonly record struct ContentReplacement(string? NewText, string? Refusal, bool HeaderKept);

