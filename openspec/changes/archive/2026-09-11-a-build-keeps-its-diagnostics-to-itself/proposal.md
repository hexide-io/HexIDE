# A build keeps its diagnostics to itself

## Why

Building with the real VB6 toolchain erased every form's language-server diagnostics, whether or not the
compiler had anything to say.

The clear itself is right and must stay: a build has to expire the errors the *previous* build injected, or
a fixed compile error stays underlined forever. What was wrong is its scope. The diagnostics channel has no
notion of who published what — `publishDiagnostics` is a whole-document replacement, and every consumer
implements it that way — so an empty set sent in the compiler's name is indistinguishable from "this
document is clean", and deletes whatever a server had published for the same form.

A successful build sent exactly the same erasures as a failed one. The worst shape is a failed build whose
errors reach no form URI — a compile error in a `.bas`, or output the error pattern does not match: the
developer dismisses a message box saying something is wrong, and finds an editor with no marks on it at
all.

Nothing tested this. The concrete toolchain service appears in the test tree only as a substitute, so no
test had ever asserted what a build puts on the diagnostics channel.

The same missing ownership shows up twice more, which is the argument for fixing the channel rather than
the call site. Two servers claiming one document already overwrite each other, by the same last-writer-wins
rule. And a build's clear is aimed at the project's *current* forms, so a form renamed since the last build
is never reached and keeps a stale compiler marker.

## What Changes

- **The diagnostics channel gains a source identity.** The last published set is held per
  `(document, source)` and the union is raised, so a source replaces only its own rows. What subscribers
  receive is unchanged in shape — one whole-document set — which is what keeps the marker pipeline, the
  addin cache and the automation server unable to disagree about which errors are current.

- **Injection names its source, and the parameter is required rather than defaulted.** An unattributed
  publish is the defect; omission must not be able to reintroduce it.

- **A source can withdraw everything it published, anywhere.** That is how a build expires the previous
  build's errors, and it is driven by what the source published rather than by a list the caller assembles
  — the two disagree exactly when it matters.

- **Each connection is a source of its own**, so two servers on one document stop overwriting each other.

- **The clear runs on the timeout path too.** It sat after the timeout bail-out, so a build that exceeded
  the limit was the one case that left the previous build's markers behind with nothing to remove them.

## What This Does Not Do

- **It does not widen what the compiler can report.** Errors that name no form are still dropped rather
  than shown in place — a `.bas` compile error remains message-box-only. That is a separate defect and is
  left where it is; the change here is what happens to *other* sources' marks while it stands.

- **It does not close the renamed-form issue.** The stale-marker half is fixed as a consequence of clearing
  by ownership, but the compiler's errors still never reach a renamed form in the first place, because the
  error text is matched against the base filename on disk and a Properties-window rename does not rename
  the file.

- **It does not attribute diagnostics in the UI.** Which source produced a mark is remembered for expiry,
  not surfaced. Rendering never needed to know, and still does not.

## Design Notes

**Why the union is raised rather than the event carrying its owner.** Every consumer already implements
whole-document replacement, correctly — a server that has fixed nothing must still be able to say "these
three errors are now two". Widening the event would push the merge into three consumers that would each
have to get it right; keeping it in the channel leaves them untouched and cannot be got wrong in one place
and not another.

**A source publishing an empty set drops its row rather than recording an empty one.** "Nothing to say" and
"never spoke" are the same thing to every reader of the union, and dropping keeps the ledger holding live
marks instead of an entry per source per document forever.

**One spelling of a URI per document, chosen when it is first seen.** A server is under no obligation to
echo a URI back byte-for-byte, so the compiler's `vb6://form/Form1` and a server's echoed
`vb6://form/form1` are one document. Raising the union under whichever spelling triggered it would give a
consumer keyed by raw string two entries reporting the same errors twice.

**Closing a document now evicts that server's marks rather than the document's.** That follows from
ownership and is the honest scope: a server that has closed a document has stopped having an opinion about
it, while a compile error in the same file has not stopped being true. The next build expires those.

**The compiler invocation is separated from the handling around it.** VB6 is Windows-only and installed on
developer machines rather than on CI, so a test that must start the real compiler is a test that never
runs — which is exactly how a build came to erase every form's diagnostics with nothing to catch it.
