## Rnd and Randomize (2026-10-26, #2117)

Measured with vb6.exe SP6, `/make`, native and p-code identical on every row. Each probe printed the `Single` that
`Rnd` returned and the 24-bit state behind it, `Fix(CDbl(r) * 2^24)`. That figure is exact, because every value `Rnd`
returns is its state divided by 2^24 (see "`Rnd`, the exact 24-bit LCG" in [Verified findings](verified-findings.md)).
Below, `LCG(s)` is one step of that generator: `(s * &H43FD43FD + &HC39EC3) And &HFFFFFF`.

### The rules, derived from the measurements and then predicted

The rows were inverted through the LCG to recover the state each statement left. That gave two rules. Both were
then tested against six seeds they had not been fitted to, and both predicted every value bit for bit.

- **`Rnd(x)` with `x` negative** sets the state to `(b + (b >> 24)) And &HFFFFFF`, where `b` is the 32 bits of
  `x` as a `Single`, and then returns `LCG` of that, as any other draw does.
- **`Randomize n`** keeps the state's low byte and replaces its upper 16 bits with
  `((hi >> 16) Xor (hi And &HFFFF))`, where `hi` is the high 32 bits of `n` as a `Double`. The low 32 bits of the
  `Double` play no part: `123456789` and `123456789.123` give the same draw. Nothing is drawn, so `Randomize n`
  twice in a row is the same as once. A `Single` or a `String` argument is converted to `Double` first.

Neither rule is a reset. `Randomize 42` lands somewhere different each time unless the low byte is the same, which is
why the same seed twice, with a draw between, does not repeat. `Rnd(-1)` just before it fixes the low byte, and only
then does the sequence repeat.

### Randomize 42 twice from the start (the issue's first probe)

| Program, at the start of `Sub Main` | vb6.exe |
|---|---|
| `Randomize 42: a = Rnd: Randomize 42: b = Rnd` | `a` 0.3391077, `b` 0.2562481 |

From the fresh state `&H50000`, the rule gives `&H404500` and then 0.3391077. After that draw, the second
`Randomize 42` keeps a different low byte, and gives 0.2562481. Both agree.

### `Rnd(-1)` then `Randomize n`: the first three draws

The state after `Rnd(-1)` is `&H395886`.

| `n` | Draw 1 | Draw 2 | Draw 3 | State `Randomize` left |
|---|---|---|---|---|
| 0 | 0.3328429 = 5584177 | 0.3203611 = 5374768 | 8.723754E-02 = 1463603 | `&H000086` |
| 1 | 0.3335753 = 5596465 | 6.816387E-02 = 1143600 | 0.5938293 = 9962803 | `&H3FF086` |
| 7 | 1.906115E-02 = 319793 | 0.9492064 = 15925040 | 0.1382019 = 2318643 | `&H401C86` |
| 42 | 0.9078093 = 15230513 | 0.6110868 = 10252336 | 0.1681854 = 2821683 | `&H404586` |
| 1000 | 0.3106719 = 5212209 | 0.4337492 = 7277104 | 0.6064484 = 10174515 | `&H008F86` |
| 123456789 | 0.6766692 = 11352625 | 0.6170073 = 10351664 | 0.2129242 = 3572275 | `&H2EA986` |
| -3 | 0.2074767 = 3480881 | 0.3214598 = 5393200 | 0.2089416 = 3505459 | `&HC00886` |
| 2.5 | 0.6451598 = 10823985 | 0.1959105 = 3286832 | 2.308959E-02 = 387379 | `&H400486` |

`Randomize n` twice in a row, with no draw between, gave the same three draws as once, for 1, 7 and 1000.

### The six seeds the rules predicted

Each one follows `Rnd(-1)`. They were chosen for a non-zero low word, a `Single` and a `String`, and each value was
written down from the rule before the probe ran.

| `Randomize` argument | Predicted | vb6.exe |
|---|---|---|
| `0.1` | 0.8860655 = 14865713 | 0.8860655 = 14865713 |
| `1 / 3` | 0.08479601 = 1422641 | 8.479601E-02 = 1422641 |
| `123456789.123` | 0.6766692 = 11352625 | 0.6766692 = 11352625, the same as `123456789` |
| `1E+10` | 0.3832732 = 6430257 | 0.3832732 = 6430257 |
| `CSng(0.1)` | 0.8860655 = 14865713 | 0.8860655 = 14865713 |
| `"42"` | 0.9078093 = 15230513 | 0.9078093 = 15230513, the same as `42` |

### `Rnd` with a negative argument

| Call | Returned | Then three more draws |
|---|---|---|
| `Rnd(-1)` | 0.224007 = 3758214 | 3.584582E-02 = 601393, 8.635235E-02 = 1448752, 0.1642639 = 2755891 |
| `Rnd(-2)` | 0.7133257 = 11967619 | 0.6624333 = 11113786, 0.7354596 = 12338965, 0.1928332 = 3235204 |
| `Rnd(-0.5)` | 0.724007 = 12146822 | 0.5358458 = 8990001, 0.5863523 = 9837360, 0.6642639 = 11144499 |
| `Rnd(-42)` | 0.223213 = 3744893 | 0.3218582 = 5399884, 0.8149242 = 13672159, 0.9062217 = 15203878 |
| `Rnd(-1000)` | 0.2409129 = 4041847 | 0.4578456 = 7681374, 0.4647012 = 7796393, 0.9086728 = 15245000 |

For `Rnd(-1)`, `b` is `&HBF800000`, so the state is `&H8000BF`, and `LCG(&H8000BF)` is `&H395886` = 3758214.

### What HexIDE did

`Reseed` built the whole state from the seed's bits, so the same seed always gave the same draw (0.7641413 for
`Randomize 42`, twice). `Rnd(x)` with `x` negative took the `Single`'s low 24 bits. Both comments said they weren't
bit-identical to VB6. #2117 has the rules above to replace them.
