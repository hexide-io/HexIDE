## Pmt, FV, PV, NPer, IPmt, PPmt and Rate (2026-10-28, #2523)

Measured on vb6.exe SP6, every row built native and as p-code, which logged the same lines. The oracle VM is an en-GB machine: `Format(1234.5, "Currency")` is `£1,234.50` and a date reads `28/10/2026`.

**What the rows say:**
- `Type` is 0 (end of period) or anything else (beginning): 2 and -1 both act as 1.
- A zero rate is the straight-line case (`Pmt(0, 12, 1200)` is -100).
- `NPer` 0 is 5 for `Pmt`, and a period outside 1 to `NPer` is 5 for `IPmt` and `PPmt`. `IPmt` of period 1 with `Type` 1
  is 0, and of a zero rate is -0 (`8000000000000000`).
- A numeric String is taken, a fractional `NPer` is used as it is (10.5), and Null is 94.
- `Rate` iterates from `Guess` (0.1 when omitted), so its last bits depend on the iteration: the two guesses differ in the
  last digits. It is 5 when it doesn't converge, for `NPer` 0, and for signs that allow no solution.

| Expression | Result | Type | Err | Bits |
|---|---|---|---|---|
| `Pmt(0.08 / 12, 120, 10000)` | `-121.327594355358` | Double | 0 | `C05E54F74E50A768` |
| `Pmt(0.08 / 12, 120, 10000, 0, 1)` | `-120.524100353004` | Double | 0 | `C05E218ADC34FE76` |
| `Pmt(0.08 / 12, 120, 10000, 5000)` | `-148.658058199703` | Double | 0 | `C062950ED011D2E3` |
| `Pmt(0, 12, 1200)` | `-100` | Double | 0 | `C059000000000000` |
| `Pmt(0, 12, 1200, 600, 1)` | `-150` | Double | 0 | `C062C00000000000` |
| `Pmt(0.1, 0, 1000)` |  |  | 5 |  |
| `Pmt(-0.05, 10, 1000)` | `-74.6065359345488` | Double | 0 | `C052A6D17C18AF17` |
| `Pmt(0.1, 10, 1000, 0, 2)` | `-147.950358984101` | Double | 0 | `C0627E69573E859B` |
| `Pmt(0.1, 10, 1000, 0, -1)` | `-147.950358984101` | Double | 0 | `C0627E69573E859B` |
| `Pmt("0.1", 10, 1000)` | `-162.745394882512` | Double | 0 | `C06457DA465E5FC5` |
| `Pmt(Null, 10, 1000)` |  |  | 94 |  |
| `Pmt(0.1, 10.5, 1000)` | `-158.128043036274` | Double | 0 | `C063C418EDB5A8E3` |
| `FV(0.06 / 12, 120, -100)` | `16387.9346806458` | Double | 0 | `40D000FBD1CEC57B` |
| `FV(0.06 / 12, 120, -100, -1000, 1)` | `18289.2710880813` | Double | 0 | `40D1DC515981D2E6` |
| `FV(0, 10, -100, -500)` | `1500` | Double | 0 | `4097700000000000` |
| `FV(0.1, 0, -100, -500)` | `500` | Double | 0 | `407F400000000000` |
| `PV(0.08 / 12, 240, -500)` | `59777.1458511878` | Double | 0 | `40ED3024AAD01C32` |
| `PV(0.08 / 12, 240, -500, 10000, 1)` | `58145.9462703541` | Double | 0 | `40EC643E47D8C3FB` |
| `PV(0, 10, -100)` | `1000` | Double | 0 | `408F400000000000` |
| `NPer(0.12 / 12, -100, 1000)` | `10.5886444594232` | Double | 0 | `40252D62CE7C6388` |
| `NPer(0.12 / 12, -100, 1000, 0, 1)` | `10.4781450851168` | Double | 0 | `4024F4CF6EBEA3C7` |
| `NPer(0, -100, 1000)` | `10` | Double | 0 | `4024000000000000` |
| `NPer(0.1, 100, 1000)` | `-7.27254089734172` | Double | 0 | `C01D1714F6039EE6` |
| `NPer(0, 0, 1000)` |  |  | 5 |  |
| `IPmt(0.1 / 12, 1, 36, 8000)` | `-66.6666666666667` | Double | 0 | `C050AAAAAAAAAAAB` |
| `IPmt(0.1 / 12, 36, 36, 8000)` | `-2.13336774835289` | Double | 0 | `C00111231C2C226A` |
| `IPmt(0.1 / 12, 1, 36, 8000, 0, 1)` | `0` | Double | 0 | `0000000000000000` |
| `IPmt(0.1 / 12, 2, 36, 8000, 0, 1)` | `-64.5332989183138` | Double | 0 | `C050222191C94996` |
| `IPmt(0.1 / 12, 0, 36, 8000)` |  |  | 5 |  |
| `IPmt(0.1 / 12, 37, 36, 8000)` |  |  | 5 |  |
| `IPmt(0, 1, 36, 8000)` | `0` | Double | 0 | `8000000000000000` |
| `PPmt(0.1 / 12, 1, 36, 8000)` | `-191.470830884034` | Double | 0 | `C067EF110BEE1BEF` |
| `PPmt(0.1 / 12, 36, 36, 8000, 1000, 1)` | `-285.624442341462` | Double | 0 | `C071D9FDB740AD19` |
| `PPmt(0, 1, 36, 8000)` | `-222.222222222222` | Double | 0 | `C06BC71C71C71C72` |
| `PPmt(0.1 / 12, 0, 36, 8000)` |  |  | 5 |  |
| `Rate(48, -200, 8000)` | `7.70147248821022E-03` | Double | 0 | `3F7F8B94477C8BB2` |
| `Rate(48, -200, 8000, 0, 1)` | `0.008052981923913` | Double | 0 | `3F807E14EFFCE5F5` |
| `Rate(48, -200, 8000, 0, 0, 0.5)` | `7.70147248820175E-03` | Double | 0 | `3F7F8B94477C6585` |
| `Rate(10, -100, 1000)` |  |  | 5 |  |
| `Rate(10, 100, 1000)` |  |  | 5 |  |
| `Rate(0, -100, 1000)` |  |  | 5 |  |
