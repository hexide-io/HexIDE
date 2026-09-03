# Tasks

Ordered by dependency. Phase 1 is a prerequisite for Phase 4 and is the only phase that touches the server
half; Phases 2 and 3 are safe to land against the current single-server world.

## 1. Make the bundled server advertise honestly (prerequisite)

Blocks Phase 4. Until this lands, gating would black out every feature against our own server.

- [ ] Advertise exactly the capabilities the server implements, and nothing it does not. Advertising an
      unimplemented feature recreates the same defect in the opposite direction.
- [ ] Refuse a ranged content change rather than mis-applying it, and evict the affected document so that
      no stale buffer remains. **This must land in the same commit as the advertisement**: declaring document
      sync is precisely what makes a client send changes, and the current code takes a ranged change's
      replacement text as the whole document. Whole-document formatting then returns an edit spanning the
      real file, so the failure is a destructive write, not a degraded feature.
- [ ] Replace the initialize smoke test's assertion. It asserts only that a capabilities key exists, which an
      empty object satisfies, under a comment stating the opposite of what the code does — plausibly why the
      gap survived. Assert each advertised capability by name and value, **and** assert that unimplemented
      ones are absent.
- [ ] Assert the same set against the packaged binary, not only an in-process host. This is the only check
      that a stale or mis-packaged server is not what the IDE resolved.

## 2. Correct the capability model (independent; land early)

Fixes a live blackout that exists today, before any routing work. Worth landing on its own merits.

- [ ] Model every capability the protocol defines as `flag | options` in a form that accepts both. Seven are
      currently modelled as a nullable flag, and a conformant server sending any one of them as an options
      object fails deserialization.
- [ ] Ensure a failure to interpret one capability cannot prevent initialization. Today it leaves the
      connection uninitialized, which silently disables every feature including diagnostics.
- [ ] Add the capabilities missing from the model entirely, without which their features cannot be gated.
- [ ] Retain the initialization result rather than discarding it, and clear it whenever the connection drops —
      a capability set surviving a reconnect to a different backend describes the wrong server.

## 3. Introduce the registry (independent of Phases 1–2)

- [ ] Define the protocol-neutral connection description and the registry contract alongside the existing
      language-service contract, so a debug connection can implement the same shape without moving it.
- [ ] Move server ownership into the registry, keeping each connection a single-server client. The existing
      client is correct for one connection; giving it several would make routing, per-server capability state
      and several transports the responsibility of a class whose current job is one connection.
- [ ] Give each registration a stable identity and an optional explicit priority.
- [ ] Route by language identity derived from extension, offering the document to every claiming server.
- [ ] Aggregate per feature; select one server for formatting and rename.
- [ ] Make transport configuration per-server; demote the current global selection to the default for the
      bundled server.
- [ ] Start a server on the first document of a language it claims.
- [ ] Expose connection state, including configured-but-not-started.

## 4. Gate on capabilities (last)

- [ ] Gate each request on the answering server's advertised capability, returning the same result the
      existing degradation path returns when no server is running.
- [ ] Log once, at warning, naming the resolved server, when a wanted capability is absent — a stale server
      binary otherwise presents as a silent blackout. This changes no behaviour and is not a compatibility
      rule; it only makes the chosen refusal visible.

## 5. Close out

- [ ] `openspec validate 2026-09-03-route-documents-to-many-language-servers --type change --strict`
- [ ] Update the language-service capability documentation, which predates the current server and describes
      a grammar that is no longer used.
- [ ] `openspec archive`, then rewrite the merged spec's Purpose — the CLI replaces it with a placeholder.
