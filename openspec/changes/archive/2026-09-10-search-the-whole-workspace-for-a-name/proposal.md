# Search the whole workspace for a name, not just the libraries the browser holds

## Why

The Object Browser's search box filters what the browser has already loaded: type libraries, project modules,
form and class names. That is a list of *containers*. The thing a developer is usually hunting for is a
*procedure* — a `Sub` in some module they cannot name — and no amount of filtering the container list finds
one, because the browser never held it.

`workspace/symbol` is the protocol's answer to exactly that question, and the client now speaks it. Nothing
surfaced it, so a server advertising `workspaceSymbolProvider` had a capability nobody could reach.

The Object Browser is the right home rather than a new Go-To-Symbol window. It is already the window a
developer opens to ask "what exists and where", it already has a search box, and adding a second search
surface would split one question across two places.

## What Changes

- **The search box asks every running server that offers `workspace/symbol`, alongside the local filter it
  already runs.** The two answer different questions and both are wanted: the local filter narrows the
  loaded libraries, the server reaches inside files.
- **Results appear in their own pane**, collapsed until a search is run, showing each hit's name, the
  container the server named, and the document and line where it lives.
- **A hit opens where the server said it was** — by URI and position, because a hit is usually a procedure
  the browser has no view-model for.
- **An empty result says which of three things happened.** This is the substance of the change rather than
  a nicety. Servers start lazily here, so "nothing has started yet", "nothing running offers this feature"
  and "nothing matched" all produce an empty list, and only the last one means the name is not there.

## Impact

- `openspec/specs/object-browser/spec.md` — one added requirement.
- No behaviour change to browsing, the library filter, navigation history, or the members pane.
