# Replies escaped every apostrophe and non-ASCII character, as if bound for a web page (#753)

A tool's reply was written with `System.Text.Json`'s default encoder. That encoder escapes `'`, `+`, `<`, `>`, `&` and
anything outside ASCII, so JSON can sit safely in a web page. The text a caller read was full of `\u0027`, which the
replies use to quote every name. A module named `Ж1` arrived as `\u04161`. #729's code-page refusal names the one
character the code page cannot hold, and that character arrived escaped. The in-IDE AI chat wrote its tool replies the
same way.

> **Fixed.** Tool results, the reply `OpenDialogNote` rewrites to add an open dialog, and the AI chat's tool replies are
> written with `ReadableJsonEncoder`. It escapes a quote and a backslash, as JSON requires, and anything a reader would
> not see: control, format, line and paragraph separator characters. Everything else is written as itself, outside
> the Basic Multilingual Plane too. The relaxed encoder was not enough: it still escapes an emoji or a CJK Extension B
> character, and it writes a right-to-left override raw, where it would silently reorder the reply around it.
>
> Measured on Windows through the server's HTTP endpoint, with the JSON-RPC envelope decoded:
>
> ```
> add_file {"name":"Ж1","type":"Module"}
> → {"success":false,"error":"'Ж1' was not added: Ж1 is kept in code page 1252, which cannot hold 'Ж' (U+0416), …"}
> ```
>
> The envelope itself is still escaped on the wire. The SDK writes it with its own options, which a server cannot set,
> and a client decodes it before reading the text. `/health` is left as it was, since it is read by code.
>
> Guarded by `ReadableJsonEncoderTests`, which include a round trip of random text. `McpRestartTests` run through a real
> server and the SDK's client, and `OpenDialogNoteTests` cover the rewritten reply. A source test holds the IDE's
> server to the same options.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools:
> `add_file {"name":"Ж1","type":"Module"}` was refused as above. The text
> the client showed has `'Ж1'` and `'Ж' (U+0416)` as characters, and its apostrophes as themselves, with no `\u`
> escape anywhere. The only backslashes are JSON's own, before the quotes in `\"Ж1\"`.
