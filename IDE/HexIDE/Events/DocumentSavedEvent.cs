using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Events;

/// <summary>
/// A form or module was written to its own file on disk.
///
/// <para>
/// Raised where the bytes are actually written rather than from an editor, because the IDE's primary save
/// gesture involves no editor at all: saving a project writes every form and module, as does saving every
/// project and the prompt shown when something closes with unsaved work. An editor-raised signal would
/// miss all three. Saving a project into another directory raises it for every form and module too, since
/// that repoints each of them (hexide-io/HexIDE#273 task 2.4a).
/// </para>
///
/// <para>
/// It carries the definition rather than a path, because the subscriber has to find its own document, and a
/// path is no help there: the name a language server knows the document by — its <c>file:</c> URI, or
/// <c>untitled:&lt;Project&gt;/&lt;Name&gt;.&lt;ext&gt;</c> while it has none — is fixed when the editor's
/// session opens, and a save is the commonest moment it stops matching the path. A first save gives the
/// document a file, a Save As or a save into another directory moves it. So the code window compares the
/// definition with its own, as it does for every other event about its document, then compares its session's
/// name with the one the document now has, and where they differ closes the document under the old name and
/// reopens it under the new one before announcing the save under the new one. The new name is worked out from
/// the document's identity, never from the event's path: a UserControl's two halves can hold different paths
/// (#474).
/// </para>
///
/// <para>
/// <b>By the time it is raised, the IDE's copy is the text the save wrote.</b> A save VB6 would also make
/// rewrites members' <c>Attribute</c> qualifiers into the model before rendering, and the save of a form,
/// UserControl or PropertyPage adopts the designer half it rendered as the header; an open code window takes
/// both before this is raised
/// (hexide-io/HexIDE#273 tasks 3.3a and 4.4). So a server reading the buffer and one reading the file see the
/// same header and the same qualifiers.
/// </para>
///
/// <para>
/// <b>Writing a copy somewhere else is not a save.</b> Building an executable writes every document to a
/// temporary directory through the same code and then puts the model back; announcing those would report
/// saves the developer never made, to a server that would then re-analyse a file that is about to vanish.
/// </para>
/// </summary>
public sealed class DocumentSavedEvent(FormDefinition? form, ModuleDefinition? module) : IEvent
{
    /// <summary>The form that was written, if this was a form.</summary>
    public FormDefinition? Form { get; } = form;

    /// <summary>The module that was written, if this was a module.</summary>
    public ModuleDefinition? Module { get; } = module;
}
