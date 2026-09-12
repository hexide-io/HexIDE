using System;
using System.Collections.Generic;
using System.ComponentModel;
using Dock.Model.Core;
using HexIDE.Forms.ViewModels;

namespace HexIDE.IDE;

public interface IDocumentDockService : INotifyPropertyChanged
{
    IReadOnlyList<BaseEditorWindowViewModel> OpenDocuments { get; }
    BaseEditorWindowViewModel? ActiveDocument { get; }
    bool TryActivate<T>(Func<T, bool> predicate) where T : BaseEditorWindowViewModel;

    /// <summary>
    /// EVERY tab in the document region, not only the editors this service opened.
    /// </summary>
    /// <remarks>
    /// <b>The two lists differ and the difference is invisible from outside.</b> This service tracks the
    /// editors it was asked to open; the Object Browser, the connection list and the protocol inspector are
    /// documents added straight to the dock by the shell. They are real tabs, in the same strip, and
    /// <see cref="OpenDocuments"/> has never known about them — so anything answering "what is open" from
    /// that list reports three tabs fewer than the user can see.
    ///
    /// <para>
    /// Measured while verifying the protocol inspector: the tab was on screen and the automation surface
    /// said it did not exist.
    /// </para>
    /// </remarks>
    IReadOnlyList<IDockable> AllTabs { get; }

    /// <summary>The frontmost tab, whatever kind it is.</summary>
    IDockable? ActiveTab { get; }

    /// <summary>Brings any tab forward, editor or not.</summary>
    bool TryActivateAny(Func<IDockable, bool> predicate);

    /// <summary>Closes any tab, editor or not.</summary>
    bool TryCloseAny(Func<IDockable, bool> predicate);
    void OpenDocument(BaseEditorWindowViewModel vm);
    void CloseDocument(BaseEditorWindowViewModel vm);
    void CloseAll();
}
