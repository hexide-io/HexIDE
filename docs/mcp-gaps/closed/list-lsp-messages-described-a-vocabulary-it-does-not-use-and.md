# `list_lsp_messages` described a vocabulary it does not use, and promised data that was not there — **CLOSED** (#400, 2026-09-22)

> **Closed by the guard this entry asked for.** A tool whose reply renders an enum now carries
> `[DescribesEnum(typeof(T))]`, and `ToolDescriptionEnumTests` fails the build when its description leaves
> a member out. Opting in the four tools that render one found sixteen more members no description named:
> `get_debug_state`'s stop reasons, `get_watches`' watch types, `get_diagnostics`' severities (which it
> never mentioned at all), and `list_lsp_messages`' outcomes. All are now described. The check is for
> omission only: a stale name left in prose cannot be told from an ordinary capitalised word, but a
> renamed member is still caught, because its new name is missing.

**Symptom.** The tool's own description enumerates what a `kind` can be, because a caller who reads
`Unconsumed` in a reply has no other way to learn what it means. `ConversationEntryKind` has **nine**
members; the description named **seven** of them, and got one of those seven wrong:

- **`StandardError` and `Note` were absent entirely.** A caller shown `"kind": "StandardError"` had been
  told the set and it was not in the set, which reads as a bug in the tool rather than a gap in the
  sentence.
- **`Lifecycle` was described as "a process starting, stopping, its standard error and exit code".** It
  covers none of standard error and, at the time the sentence was written, no exit code either — nothing
  in the tree read one. So the description was the only place in the repository claiming the record held
  data it did not hold, and a caller who trusted it would have concluded the *server* was silent.
- **`direction` said "Sent, Received, or Local for the entries that are not messages at all"**, which
  puts every non-message under `Local`. A standard error line is `Received`, and the vocabulary's own
  definition says so.

**Why it survived.** The two omissions were kinds the enum had and the wire never produced — `StandardError`
because nothing raised it, `Note` because the only path to it was an undecodable frame that the recorder
never saw. A description drifts exactly where the code is unreachable, so the sentence agreed with the
observable behaviour and disagreed with the design. Nothing checks a `[Description]` string against the
enum it enumerates; the coverage guard that would have caught it is the one this repository applies to
`docs/lsp-client.md` and to the language packs, and tool descriptions have no equivalent.

**Fixed** by rewriting both sentences to the full set of nine — four for wire traffic, five that are not
messages — and to what each kind actually carries. The
underlying data gaps were closed in the same change: standard error and the exit code now reach the record
(`ILspTransport.Notice`), and an undecodable frame is recorded as a `Note` with its bytes rather than
dropped. Filed as hexide-io/HexIDE#400 for the general problem — a tool description that enumerates
a C# enum should be guarded against it, the way the LSP coverage table is guarded against the
specification.
