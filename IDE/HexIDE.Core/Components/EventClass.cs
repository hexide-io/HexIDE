using System;

namespace HexIDE.Runtime.Components;

public class EventClass
{
    public EventClass(string name, params ReadOnlySpan<EventClassArgument> arguments)
    {
        Name = name;
        Arguments = arguments.ToArray();
    }

    public string Name { get; }
    public EventClassArgument[] Arguments { get; }
}

public class EventClassArgument
{
    public EventClassArgument(string defaultName, string type)
    {
        DefaultName = defaultName;
        Type = type;
    }

    public string DefaultName { get; }
    public string Type { get; }
}