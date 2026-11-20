# Add-in host API

An add-in receives `IHexIdeHost` in `Initialize`. Use that host for IDE capabilities; keep it for work
that happens after initialization. The [addin-system spec](../openspec/specs/addin-system/spec.md)
describes the contribution, editor, project, diagnostic and lifecycle surfaces. The interface and its XML
documentation are in [IHexIdeHost.cs](../IDE/HexIDE.Core/Addins/IHexIdeHost.cs).

## Reporting a failure after loading

Call `host.ReportFailure(message, exception)` when a loaded add-in encounters a failure the user should
see. The exception is optional. For example, report a language-server registration failure from a later
project-open callback:

```csharp
try
{
    connection = host.RegisterLanguageServer(server);
}
catch (Exception exception)
{
    host.ReportFailure("The project's language server could not be registered.", exception);
}
```

HexIDE logs the complete message at Warning with the exception and the add-in's manifest title and id.
Options → Add-ins shows the latest report below that add-in's status, with the time and up to 500 message
characters (followed by an ellipsis when shortened). The add-in supplies the message; HexIDE localizes
the surrounding label. The status stays unchanged: reporting does not unload the add-in.

Only the latest report is retained, including the exception's type and message and the report time.
It stays in memory until the next start; disabling and enabling do not clear it. Reopen Options to read
a report made while the page was open. Calls from unknown or removed add-ins are logged and otherwise
ignored. The interface's empty default implementation keeps existing host implementations compatible.
