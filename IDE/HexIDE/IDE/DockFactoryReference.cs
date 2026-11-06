namespace HexIDE.IDE;

// No constructor dependency on the factory: Lazy/Func edges still form a cycle in Pure.DI.
public sealed class DockFactoryReference
{
    internal MainViewViewModel.DockFactory? Value { get; set; }
}
