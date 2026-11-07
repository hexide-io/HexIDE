## Relational comparison + `ChDir`/`ChDrive` (2026-08-03 gap-audit fixes)

**String relational comparison** (`< <= > >=`), verified against `vb6.exe`:

| Case | Result | Rule |
|---|---|---|
| `"a" < "b"` | True | ordinal |
| `"B" < "a"` | **True** | **Binary/ordinal** ('B'=66 < 'a'=97) — VB6 default `Option Compare Binary`, *not* case-insensitive |
| `"10" < "9"` | **True** | **string** compare ('1' < '9') — *not* numeric (numeric would be False) |
| `"" < "a"` | True | empty string is least |
| `5 < "10"` | True | **string-vs-number → NUMERIC** (numeric string coerces): 5 < 10 |
| `"10" < 5` | False | numeric: 10 < 5 |
| `"abc" < 5` | **Err 13** | non-numeric string vs number → Type Mismatch |

Two strings → ordinal `String.Compare(..., Ordinal)`. **Landed:** the both-string case (interpreter previously
threw / mis-parsed numeric-looking strings numerically). **Still a gap:** string-vs-*number* comparison — the
interpreter throws Type Mismatch (its `GetTwoValuesSameTypes` rejects any type mismatch) where VB6 coerces a
numeric string and compares numerically. (`Option Compare Text` = case-insensitive is a separate wall.)

**`ChDir` / `ChDrive`** (the args coerce to String; oracle-verified):

| Statement | Result |
|---|---|
| `ChDir "<valid path>"` | ok |
| `ChDir "<bad path>"` / `ChDir 5` (→ `"5"`) / `ChDir ""` | **Path Not Found (76)** — all failures, incl. a coerced number |
| `ChDrive "C"` | ok |
| `ChDrive "Q"` (valid letter, no such drive) | **Device Unavailable (68)** |
| `ChDrive 5` (→ `"5"`, non-letter first char) | **Invalid Procedure Call (5)** |
| `ChDrive ""` | no-op (Err 0) |

(These were fully broken before — the string-unpack helper had no `String` branch, so both always threw. The
audit's "applies the side-effect then throws" description was inaccurate.)

