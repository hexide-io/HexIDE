## Why

RDCore and other language servers have tools an assistant could use: reading a project's structure, running a
check. AI Chat had no way to reach them. Decided on the Team Desk (q-rd-chat-tools, #4257): through the
language-server connection HexIDE already holds, so no second channel, port or credential is opened.

## What Changes

- A server declares `experimental.hexide.chatTools` and names its own list, call and optional context methods,
  in MCP's shapes.
- Only registrations made from a configuration entry, the bundled server or a consented add-in's server offer
  tools; a new `chatTools` entry field sets `hints` (default), `ask` or `off`.
- The host decides whether a call asks, from the server's own listing on each call: a call asks unless the tool
  says it only reads and doesn't say it runs code. `ask` asks for every call and takes no context.
- Tools are attributed to their server; names, descriptions, schemas, results and context are limited.
- The privacy notice says what those tools return, and the context, are sent too, and its version moves.

## Impact

- Affected spec: `ai-chat`.
- Code: `HexIDE.Core` (capability, policy, host interface), `HexIDE.Lsp` (registry, service, factory, loader),
  `HexIDE.AIChat` (loop, approval, notice), every shipped language pack.
