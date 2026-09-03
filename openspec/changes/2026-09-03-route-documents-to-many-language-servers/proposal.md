# Route documents to many language servers, and gate on what each one advertises

## Why

This capability's own Purpose says the contract exists so the backend "can be replaced without touching the
editor", and hides "which server is answering". Both halves are true today only because there is exactly one
server. The seam has never been asked the question it was built for.

Two things forced it. A second *language* — Markdown, HTML, CSS alongside VB6 — is the direction of travel,
and no VB6-family backend can test it: a second server for the same language exercises the transport and
nothing above it. And the transport itself is now proven, so what remains untested is everything else.

Driving the client against a server the project did not write found two defects within hours, both invisible
against our own server and both already fixed or filed:

- **#236** — diagnostics were matched by exact URI string, so a server that normalised the URI it echoed back
  had every diagnostic silently discarded. Ours echoes verbatim, so this could not be reached from inside.
- **A total, silent blackout on the initialize handshake.** Seven `boolean | Options` capability fields are
  modelled as `bool?`. A conformant backend advertising *any one* of them in its options form throws during
  deserialization; the surrounding catch swallows it, initialization never completes, and **every language
  feature including diagnostics goes dark with no error**. This exists today, before any of the work below.

The pattern in both is the same, and it is the argument for this change rather than an argument against the
seam: the contract is sound, and its untested assumptions are all of the form *"the server behaves like ours"*.

## What Changes

- **Documents are routed to every server that claims their language, not to one server.** Results are
  aggregated per feature. The dominant editor ecosystem settled this question years ago — merging is the
  default there, and the mechanism to opt out of it has stayed unshipped for eight years — so a router that
  picks one winner would be a design to unwind the first time anyone wants a linter beside a language server.
  A merging router can always be configured down to one; a picking router cannot be widened without changing
  every call site.
- **Formatting and rename are the exceptions**, because merging two answers is meaningless. They pick one
  server by a stable identity assigned at registration. That identity is the cheap half and lands now; a
  user-facing override is deliberately deferred.
- **Requests are gated on the capability the answering server advertised**, instead of every method being
  called unconditionally. This is what makes a foreign backend usable at all, and it is a prerequisite that
  our own server first advertise honestly — it currently advertises nothing.
- **Connections become observable** — identity, kind, state, the languages served, and the raw advertised
  capabilities. Not to build a panel now, but because a router built as a private detail forecloses one, and
  a registry with observable state does not.
- **Servers start lazily**, on the first document of their language.

## Impact

- Modifies `lsp-client`: routing by language, capability gating, per-server transports, an observable
  connection surface, and lazy start. The "IDE depends only on the contract" and "features degrade rather
  than fail" requirements are unchanged and are what make gating cheap — a gated-out feature returns the
  same empty result the existing degradation path already returns, so no caller changes.
- Requires a companion change to `language-server`: the bundled server must advertise exactly the
  capabilities it implements. That has no requirement about capabilities today.
- **Blocked on that companion change.** Gating against a server that advertises nothing would black out
  every feature. A compatibility rule treating "advertises nothing" as "supports everything" was considered
  and rejected: it is backwards from the protocol, nothing ever removes a compatibility rule once shipped,
  and it would mask the identical defect in every foreign server — which is the defect gating exists to catch.
- **Not covered here.** How servers are discovered on disk, and which languages ship configured out of the
  box. Both are answerable only against a concrete second server and would be speculation now.
- The connection descriptor is protocol-neutral so a debug-adapter connection can implement the same shape
  later. The *clients* stay separate — a shared client abstraction across two different protocols would be
  a fiction — but what a UI binds to should not have to be rebuilt to gain a second row type.
