using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

/// <summary>
/// A form's file both ways: a reader that can also render what it read. Where a project's forms are read by one of these,
/// the IDE writes them; where only by a reader, it never does. A form is read and rendered by the same object, because the
/// text says how the companion divides, and whatever a form holds that the IDE can't read is read again only by the format
/// that made it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A render writes nothing and changes nothing.</b> The IDE does the writing, the companion before the text, so an
/// interrupted save leaves a pair that refuses to load rather than one that loads wrongly. It renders to save, and also to
/// compare a form with its file, to check its file's code page can hold it, to keep a copy for crash recovery, and to show
/// the header over the code.
/// </para>
/// <para>
/// <b>The same form renders the same.</b> A form counts as edited when a fresh render differs from the last, so a render
/// that varies shows every form as edited.
/// </para>
/// <para>
/// <b>The code comes last, as it was given.</b> The code window shows the header and then the code, and takes the header to
/// be the render less the code. A render that doesn't end with the code isn't written.
/// </para>
/// <para>
/// <b>A form this can't reproduce is refused when it's read,</b> by marking it with
/// <see cref="FormDefinition.MarkUnfaithfulToSave"/>, and <see cref="UnfaithfulSaveCause.RefusedByFormat"/> where none of
/// the other causes is the reason. The designer and the code window then open it read-only, and a save leaves its file
/// alone.
/// </para>
/// </remarks>
public interface IFormFileFormat : IFormFileReader
{
    /// <summary>A form's file, as it would be written under this name, by which its text cites its companion.</summary>
    RenderedFile RenderForm(FormDefinition form, string fileName);

    /// <summary>A UserControl's or PropertyPage's file, its designer half and its code, as it would be written under this name.</summary>
    RenderedFile RenderDesignerFile(ModuleDefinition module, string fileName);

    /// <summary>The part of a form's file above its code, which the code window shows in front of the code.</summary>
    string RenderDesignerHeader(FormDefinition form, string fileName);
}
