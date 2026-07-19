using System;
using System.Collections.Generic;
using Dock.Model.Mvvm.Controls;
using HexIDE.IDE;

namespace HexIDE.Forms.ViewModels;

public abstract class BaseEditorWindowViewModel : Document, IMdiWindow, IDisposable
{
    private List<IDisposable>? disposables;

    // Subclasses implement this; Initialize calls Title = ComputeTitle() once set up.
    protected abstract string ComputeTitle();

    public abstract object? Icon { get; }

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