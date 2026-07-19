using System;
using System.Collections.Generic;
using System.ComponentModel;
using HexIDE.Forms.ViewModels;

namespace HexIDE.IDE;

public interface IDocumentDockService : INotifyPropertyChanged
{
    IReadOnlyList<BaseEditorWindowViewModel> OpenDocuments { get; }
    BaseEditorWindowViewModel? ActiveDocument { get; }
    bool TryActivate<T>(Func<T, bool> predicate) where T : BaseEditorWindowViewModel;
    void OpenDocument(BaseEditorWindowViewModel vm);
    void CloseDocument(BaseEditorWindowViewModel vm);
    void CloseAll();
}
