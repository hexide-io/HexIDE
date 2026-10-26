using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Runtime.Interpreter;

public partial class BasicInterpreter
{
    private readonly Dictionary<LibraryClass, LibraryObject> libraryGlobals = new();
    private readonly List<LibraryObject> libraryCreationOrder = new();

    internal LibraryClass? LibraryClassFor(string name)
    {
        if (Modules.TryGet(name, out var module) && module.Kind == InterpreterModuleKind.Class)
            return null;
        foreach (var library in BuiltIns.ControlLibraries)
            foreach (var cls in library.Classes)
                if (name.Equals(cls.Name, StringComparison.OrdinalIgnoreCase)
                    || name.Equals(library.TypeName(cls.Name), StringComparison.OrdinalIgnoreCase))
                    return cls;
        return null;
    }

    internal LibraryClass? GlobalMember(string name, RuntimeLibrary? only = null)
    {
        foreach (var library in BuiltIns.ControlLibraries)
            if (only is null || ReferenceEquals(only, library))
                foreach (var cls in library.Classes)
                    if (cls.GlobalMultiUse && cls.Members.Any(m => m.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        return cls;
        return null;
    }

    internal Vb6Value NewLibraryObject(LibraryClass cls) => new(new LibraryObject(cls.Factory()));

    internal async Task<Vb6Value?> CallLibraryMember(LibraryClass cls, string name, List<Vb6Value> args)
    {
        if (!libraryGlobals.TryGetValue(cls, out var instance))
        {
            instance = new LibraryObject(cls.Factory()) { References = 1 };
            libraryGlobals.Add(cls, instance);
            libraryCreationOrder.Add(instance);
        }
        if (args.Count == 0 && instance.TryGetProperty(name, out var property))
            return property;
        return await CallProxy(instance, name, args);
    }

    private void ReleaseLibraryGlobals()
    {
        for (var i = libraryCreationOrder.Count - 1; i >= 0; i--)
            ReleaseLibraryObject(libraryCreationOrder[i]);
        libraryCreationOrder.Clear();
        libraryGlobals.Clear();
    }

    private void ReleaseLibraryObject(LibraryObject instance)
    {
        if (--instance.References <= 0 && !instance.Released)
        {
            instance.Released = true;
            if (!Ending && !unimplementedFault && DebugController?.IsAborting != true)
                (instance.Host as IDisposable)?.Dispose();
        }
    }

    internal sealed class LibraryObject(ICSharpProxy host) : ICSharpAsyncProxy, ICSharpPropertyBag
    {
        internal ICSharpProxy Host { get; } = host;
        internal int References;
        internal bool Released;
        public bool TryGetProperty(string name, out Vb6Value value)
        {
            value = default;
            return Host is ICSharpPropertyBag bag && bag.TryGetProperty(name, out value);
        }
        public bool TrySetProperty(string name, Vb6Value value) =>
            Host is ICSharpPropertyBag bag && bag.TrySetProperty(name, value);
        public void Call(string method, List<Vb6Value> args) => Host.Call(method, args);
        public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
        {
            if (Host is ICSharpAsyncProxy asyncHost) return asyncHost.CallAsync(method, args);
            Host.Call(method, args);
            return Task.FromResult<Vb6Value?>(null);
        }
    }
}
