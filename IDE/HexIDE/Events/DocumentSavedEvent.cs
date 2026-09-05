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
/// miss all three.
/// </para>
///
/// <para>
/// It carries the definition rather than a path, because a path cannot be turned back into the identity a
/// language server knows the document by — that identity is fixed when the document is opened and is held
/// by the editor's session. A subscriber compares the definition with its own, which is what the editors
/// already do for every other event about their document.
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
