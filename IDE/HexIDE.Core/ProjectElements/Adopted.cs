namespace HexIDE.Runtime.ProjectElements;

/// <summary>Why a file the developer already has could not join the project.</summary>
public enum AdoptionRefusal
{
    /// <summary>It did join.</summary>
    None,

    /// <summary>The file could not be read or parsed, so there was nothing to add.</summary>
    CouldNotRead,

    /// <summary>
    /// Its own name is already a form, module or class of this project — which VB6 refuses to build.
    /// </summary>
    NameTaken,

    /// <summary>Its own name is not a VB6 name, so nothing in the project could refer to it.</summary>
    NameNotValid,
}

/// <summary>
/// The outcome of adopting a file that already exists on disk.
/// </summary>
/// <param name="Document">What joined the project, or null when nothing did.</param>
/// <param name="Refusal">Why nothing joined, or <see cref="AdoptionRefusal.None"/>.</param>
/// <param name="Name">
/// The name the file asked to be known by — from its <c>Attribute VB_Name</c>, or a form's designer line.
/// Kept even when it was the reason for the refusal, because a refusal that does not say which name it
/// objected to is not usable: Add File takes several files at once.
/// </param>
public sealed record Adopted<T>(T? Document, AdoptionRefusal Refusal, string? Name) where T : class
{
    public static Adopted<T> Added(T document, string name) => new(document, AdoptionRefusal.None, name);

    public static Adopted<T> Unreadable() => new(null, AdoptionRefusal.CouldNotRead, null);

    public static Adopted<T> Refused(AdoptionRefusal refusal, string name) => new(null, refusal, name);
}
