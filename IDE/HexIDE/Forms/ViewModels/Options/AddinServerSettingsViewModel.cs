using System;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexIDE.IDE;
using HexIDE.Lsp;

namespace HexIDE.Forms.ViewModels.Options;

public sealed partial class AddinServerSettingsViewModel : ObservableObject, IDisposable
{
    private readonly AddinServerConnection _connection;
    private readonly ISettingsService _settings;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private bool _disposed;
    private readonly Dispatcher _dispatcher;

    public AddinServerSettingsViewModel(string name, AddinServerConnection connection, ISettingsService settings)
    {
        _dispatcher = Dispatcher.UIThread;
        Name = name;
        _connection = connection;
        _settings = settings;
        _settings.PropertyChanged += OnSettingsChanged;
    }

    public string Name { get; }
    public string Json => _connection.GetSettings() is { } value ? JsonSerializer.Serialize(value, JsonOptions) : "";
    public bool IsNone => _connection.GetSettings() is null;
    public bool IsDefault => !CanReset && !IsNone;
    public bool CanReset => _settings.HasLanguageServerSettings(_connection.Id);

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != "LanguageServerSettings") return;
        if (_dispatcher.CheckAccess()) Refresh();
        else _dispatcher.Post(Refresh);
    }

    private void Refresh()
    {
        if (_disposed) return;
        OnPropertyChanged(nameof(Json));
        OnPropertyChanged(nameof(IsNone));
        OnPropertyChanged(nameof(IsDefault));
        OnPropertyChanged(nameof(CanReset));
        ResetCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanReset))]
    private Task Reset() => _connection.ResetSettingsAsync();

    public void Dispose()
    {
        _disposed = true;
        _settings.PropertyChanged -= OnSettingsChanged;
    }
}
