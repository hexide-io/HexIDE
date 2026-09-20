using System;
using System.Collections.Generic;
using Dock.Model.Mvvm.Controls;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Forms.ViewModels;

public abstract class BaseEditorWindowViewModel : Document, IMdiWindow, IDisposable
{
    private List<IDisposable>? disposables;

    // Subclasses implement this; Initialize calls Title = ComputeTitle() once set up.
    protected abstract string ComputeTitle();

    public abstract object? Icon { get; }

    /// <summary>
    /// The document this tab holds, or null when it holds none.
    /// </summary>
    /// <remarks>
    /// Null is the honest answer for a good many tabs: the Object Browser, the protocol inspector, the
    /// read-only project view and a carried text file are all documents to the dock and none of them is a
    /// form, module or class. It is also null for a code editor that has not been initialized yet.
    /// </remarks>
    public virtual DocumentIdentity? OpenDocument => null;

    // Explicit impl so IMdiWindow.Title routes to Document.Title without ambiguity.
    string IMdiWindow.Title => Title;

    public event Action<IMdiWindow>? CloseRequest;

    protected T AutoDispose<T>(T t) where T : IDisposable
    {
        disposables ??= new();
        disposables.Add(t);
        return t;
    }

    public virtual void Dispose()
    {
        if (disposables == null)
            return;

        for (int i = disposables.Count - 1; i >= 0; --i)
            disposables[i].Dispose();

        disposables.Clear();
    }

    protected void RequestClose() => CloseRequest?.Invoke(this);
}