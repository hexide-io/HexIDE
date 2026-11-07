# A missing or mistyped tool argument is reported as a defect in the tool, asking to be reported

**Symptom.** (Windows, 2026-10-20.) The SDK binds a call's arguments without checking them against the schema, and
`ToolFailures` reported what that threw as an unhandled defect:

```
inspect_element {"path":"Window"}           (the parameter is 'target')
→ "'inspect_element' failed with an unhandled System.ArgumentException: The arguments dictionary is missing a value
   for the required parameter 'target'. ... This is a defect in the tool, not a refusal of your arguments: please
   report it."
take_snapshot {"inline":"true"}
→ "... System.Text.Json.JsonException: The JSON value could not be converted to System.Boolean ... This is a defect
   in the tool ..."
```

The reply said the opposite of what happened and asked for a bug report, for every tool and every parameter.
[#1270](https://github.com/hexide-io/HexIDE/issues/1270).

> **Fixed.** Before a tool runs, `ArgumentCheck` checks the call's arguments against the tool's input schema, which the
> SDK never does, and refuses with every problem it finds:
>
> - each required parameter that is missing;
> - JSON of the wrong type, including null where the parameter cannot hold it, and an array item, named by position;
> - an argument the tool does not take, named only beside a real problem, as the likely misspelling, because the SDK
>   ignores it otherwise.
>
> The check is no stricter than the binding: a number in a string passes, as the SDK's serializer reads it. What the
> schema cannot see, such as an integer too big for an `int`, still fails in the SDK's binding. `ToolFailures` turns
> that into a refusal too, naming the argument out of range. In both cases the reply says nothing was done and does
> not ask to be reported. An `ArgumentException` a tool throws itself is still a defect.
>
> The first version classified only what the binding threw. Its review found that a null for a non-null parameter
> bound and failed inside the tool, still asking to be reported. It also found that a numeric string was flagged though
> it binds, and that array items and overflow fell back to the serializer's raw message.
>
> Measured on Windows through the server's HTTP endpoint:
>
> ```
> inspect_element {"path":"Window"}
> → "'inspect_element' refused its arguments, and nothing was done: 'target' is required; it takes no 'path'
>    parameter. ..."
> set_bookmarks {"name":"Form1","lines":null}
> → "... 'lines' must be array, not null. ..."
> ```
>
> Guarded by `McpRestartTests`, through a real server and the SDK's client.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools:
>
> ```
> inspect_element {"path":"Window"}
> → 'inspect_element' refused its arguments, and nothing was done: 'target' is required; it takes no 'path'
>    parameter. Its input schema names each parameter and its type.
> set_bookmarks {"name":"Form1","lines":null}
> → 'set_bookmarks' refused its arguments, and nothing was done: 'lines' must be array, not null. …
> hover {"target":"…/Custom/List/Button[Mouse]","dwellMs":3000000000,"window":"ide"}
> → 'hover' refused its arguments, and nothing was done: 'dwellMs': The JSON value could not be converted to
>    System.Int32. Path: $ | LineNumber: 0 | BytePositionInLine: 10. …
> ```
>
> - **The third fell short of the fix above (#1965), now corrected.** Integral overflow names the value,
>   whether it is too large or too small, and the whole-number range; parameter guidance adds the tool
>   limit where declared (hover: 0 to 10000 ms). Linux SDK-client tests cover int, long and array items;
>   a direct binding test checks hover guidance. A live repeat of the call above remains to be checked.
> - **`take_snapshot {"inline":"true"}` never reached the server as a string.** Claude Code converted it to the
>   boolean the schema asks for, and the call succeeded. That refusal is checked only by the SDK-client tests in
>   `McpRestartTests`.
