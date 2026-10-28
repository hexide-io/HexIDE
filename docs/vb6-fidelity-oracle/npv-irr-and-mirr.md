## NPV, IRR and MIRR (2026-10-28, #2525)

Measured on vb6.exe SP6, every row built native and as p-code, which logged the same lines. The oracle VM is an en-GB machine: `Format(1234.5, "Currency")` is `£1,234.50` and a date reads `28/10/2026`.

**What the rows say:**
- The arrays are `Double()`: `a` is -10000, 3000, 4200, 6800, 1000; `b` is -70000, 12000, 15000, 18000, 21000, 26000;
  `cs` is 100, 200, 300; `z` is never allocated.
- `NPV` discounts every value, the first one too. A rate of -1 is 5, and so is an unallocated array.
- `IRR` iterates from `Guess` (0.1 when omitted), so its last bits depend on the iteration, as the two rows for `a` show.
  Cash flows with no change of sign are 5.
- **`MIRR` of flows with no negative value is 11, Division by zero**, not 5. A finance rate of -1 is 5.

| Expression | Result | Type | Err | Bits |
|---|---|---|---|---|
| `NPV(0.1, a)` | `1809.36473539438` | Double | 0 | `409C45757D31FA2E` |
| `NPV(0, a)` | `5000` | Double | 0 | `40B3880000000000` |
| `NPV(-1, a)` |  |  | 5 |  |
| `NPV(0.1, cs)` | `481.592787377911` | Double | 0 | `407E197C0E9E19C5` |
| `NPV(0.1, z) (unallocated)` |  |  | 5 |  |
| `IRR(a)` | `0.191099160089404` | Double | 0 | `3FC875EFF170437B` |
| `IRR(a, 0.2)` | `0.191099160089389` | Double | 0 | `3FC875EFF1704153` |
| `IRR(b)` | `8.66309480331796E-02` | Double | 0 | `3FB62D72209FBD49` |
| `IRR(cs) (all positive)` |  |  | 5 |  |
| `MIRR(b, 0.1, 0.12)` | `9.86691073371503E-02` | Double | 0 | `3FB94260ED237997` |
| `MIRR(a, 0.1, 0.12)` | `0.159885792310914` | Double | 0 | `3FC477233C8903B7` |
| `MIRR(cs, 0.1, 0.12)` |  |  | 11 |  |
| `MIRR(b, -1, 0.12)` |  |  | 5 |  |
