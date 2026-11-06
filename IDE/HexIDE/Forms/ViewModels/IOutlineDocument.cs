using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using HexIDE.Lsp.Messages;

namespace HexIDE.Forms.ViewModels;

internal interface IOutlineDocument : ISearchableDocument, INotifyPropertyChanged
{
    IReadOnlyList<DocumentSymbol> OutlineSymbols { get; }
    event Action? LanguageServerStateChanged;
    Task RefreshSymbolsAsync();
    void NavigateToSymbol(DocumentSymbol symbol);
}
