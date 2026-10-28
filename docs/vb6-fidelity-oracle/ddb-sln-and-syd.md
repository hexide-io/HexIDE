## DDB, SLN and SYD (2026-10-28, #2524)

Measured on vb6.exe SP6, every row built native and as p-code, which logged the same lines. The oracle VM is an en-GB machine: `Format(1234.5, "Currency")` is `£1,234.50` and a date reads `28/10/2026`.

**What the rows say:**
- `DDB`'s `Factor` defaults to 2. A period outside 1 to `Life` is 5, and so is a `Life` of 0; a fractional period
  (2.5) is used as it is. A salvage above the cost gives a negative amount, with no error.
- `SLN` with a `Life` of 0 is 5; a negative `Life` gives a negative amount.
- `SYD` with a period outside 1 to `Life` is 5; a fractional period is used as it is.

| Expression | Result | Type | Err | Bits |
|---|---|---|---|---|
| `DDB(2400, 300, 10 * 365, 1)` | `1.31506849315068` | Double | 0 | `3FF50A8542A150A8` |
| `DDB(2400, 300, 10 * 12, 1)` | `40` | Double | 0 | `4044000000000000` |
| `DDB(2400, 300, 10, 1)` | `480` | Double | 0 | `407E000000000000` |
| `DDB(2400, 300, 10, 2, 1.5)` | `306` | Double | 0 | `4073200000000000` |
| `DDB(2400, 300, 10, 10)` | `22.1225472000002` | Double | 0 | `40361F5F40D83797` |
| `DDB(2400, 300, 10, 11)` |  |  | 5 |  |
| `DDB(2400, 300, 10, 0)` |  |  | 5 |  |
| `DDB(2400, 300, 10, 2.5)` | `343.460041343968` | Double | 0 | `4075775C544FF263` |
| `DDB(2400, 3000, 10, 1)` | `-600` | Double | 0 | `C082C00000000000` |
| `DDB(2400, 300, 0, 1)` |  |  | 5 |  |
| `SLN(30000, 7500, 10)` | `2250` | Double | 0 | `40A1940000000000` |
| `SLN(30000, 7500, 0)` |  |  | 5 |  |
| `SLN(30000, 7500, -2)` | `-11250` | Double | 0 | `C0C5F90000000000` |
| `SYD(30000, 7500, 10, 1)` | `4090.90909090909` | Double | 0 | `40AFF5D1745D1746` |
| `SYD(30000, 7500, 10, 10)` | `409.090909090909` | Double | 0 | `407991745D1745D1` |
| `SYD(30000, 7500, 10, 11)` |  |  | 5 |  |
| `SYD(30000, 7500, 10, 0)` |  |  | 5 |  |
| `SYD(30000, 7500, 10, 2.5)` | `3477.27272727273` | Double | 0 | `40AB2A8BA2E8BA2F` |
