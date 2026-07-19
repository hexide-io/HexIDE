using System;
using System.Collections.Generic;
using Serilog;

namespace HexIDE.IDE;

public class EventBus : IEventBus
{
    private Dictionary<Type, List<BaseHandler>> handlers = new Dictionary<Type, List<BaseHandler>>();

    public void Publish<T>(T @event) where T : IEvent
    {
        if (this.handlers.TryGetValue(typeof(T), out var handlers))
        {
            Log.Debug("EventBus: Publishing {EventType} to {HandlerCount} handler(s)", typeof(T).Name, handlers.Count);
            for (var index = handlers.Count - 1; index >= 0; index--)
            {
                var handler = (Handler<T>)handlers[index];
                try
                {
                    handler.Execute(@event);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Unhandled exception in EventBus handler for {EventType}", typeof(T).Name);
                }
            }
        }
        else
        {
            Log.Debug("EventBus: Publishing {EventType} — no subscribers", typeof(T).Name);
        }
    }

    public IDisposable Subscribe<T>(Action<T> action) where T : IEvent
    {
        if (!this.handlers.TryGetValue(typeof(T), out var handlers))
        {
            handlers = this.handlers[typeof(T)] = new List<BaseHandler>();
        }
        var handler = new Handler<T>(handlers, action);
        handlers.Add(handler);
        Log.Debug("EventBus: Subscribed to {EventType} (now {Count} handler(s))", typeof(T).Name, handlers.Count);
        return handler;
    }

    private abstract class BaseHandler : System.IDisposable
    {
        public abstract void Dispose();
    }

    private class Handler<T> : BaseHandler where T : IEvent
    {
        private List<BaseHandler>? list;
        private Action<T>? action;

        public Handler(List<BaseHandler> list, Action<T> action)
        {
            this.list = list;
            this.action = action;
        }

        public void Execute(T @event)
        {
            action?.Invoke(@event);
        }

        public override void Dispose()
        {
            list?.Remove(this);
            list = null;
            action = null;
        }
    }
}