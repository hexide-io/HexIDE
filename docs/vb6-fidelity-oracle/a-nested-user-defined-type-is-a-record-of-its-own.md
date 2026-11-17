## A nested user-defined type is a record of its own (2026-11-16, #2518)

**The question.** When a UDT holds another UDT, does the inner one lay out as a record of its own, aligned and with its
size rounded up before the next field, or are its fields simply spliced into the outer type?

**The earlier reading, overturned.** Oracle PR #4241's section on `lset-between-two-user-defined-types.md` says "a nested
type lays out inline, exactly as its fields would". Its only nested row can't tell the two apart: `(Integer, Long),
Integer` comes to 12 bytes either way. Reviewing #2518's implementation, which treats a nested type as a record of its
own, needed a row where the two readings differ.

**The probe.**
- **The compiler:** vb6.exe SP6, native and p-code, through the oracle harness.
- **The types:**
  - `InLB` is a Long then a Byte, and `OutA` is an `InLB` then a Byte;
  - `InIB` is an Integer then a Byte, and `OutB` is an `InIB` then a Byte.
- **The read:** `Len` and `LenB` of each type. Then each outer type, with its fields set, was copied with `LSet` over
  16 bytes pre-filled with `&HEE`, and the bytes were read back.

| Row | As a record of its own | Flattened | vb6.exe, native and p-code |
|---|---|---|---|
| `LenB(InLB)`, `LenB(OutA)` | 8, 12 | 8, 8 | **8, 12** |
| `LenB(InIB)`, `LenB(OutB)` | 4, 6 | 4, 4 | **4, 6** |
| `Len(OutA)`, `Len(OutB)` | 6, 4 | 6, 4 | 6, 4 |
| `OutA` with `L = &H4030201`, `B = 5`, outer Byte 6 | the outer Byte at offset 8 | at offset 5 | `01 02 03 04 05 00 00 00 06 00 00 00`, then `EE EE EE EE` untouched |
| `OutB` with `N = &H201`, `B = 3`, outer Byte 4 | the outer Byte at offset 4 | at offset 3 | `01 02 03 00 04 00`, then untouched |

**Readings:**
- **A nested type is a record of its own.** It's aligned to its largest field, and its size is rounded up to that before
  the next field: the outer Byte follows `InLB`'s padding at offset 8, not its Byte at offset 5.
- **The padding inside it is copied,** as `00`, like any other padding.
- **`Len` still counts only the fields:** 6 and 4.

**Not measured:** a nested type whose largest field is a Double or a Currency (aligned to 4, as a lone one is, on the
rule above, but not checked); a fixed array of a UDT.
