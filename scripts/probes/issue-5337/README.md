# #5337: the x87 fast path

This is a model comparison and performance probe, **not a VB6 measurement**. The
unchanged reference is [#5259](https://github.com/hexide-io/HexIDE/issues/5259)'s
`X87Math`, based on oracle PRs [#5258](https://github.com/hexide-io/HexIDE/pull/5258)
and [#5294](https://github.com/hexide-io/HexIDE/pull/5294).
Run from the repository root, with the test environment described in `CLAUDE.md`:

```sh
dotnet run --project scripts/probes/issue-5337/Probe.csproj -c Release --disable-build-servers -- 10000000
```

The argument generator is shared with `X87MathFastTests`. It covers both signs,
all binary exponents from -33 through 62, and neighbours within three ulps of
multiples of the host's double π/2, including multiplier 2^20. These arguments
exercise cancellation; the host π is never used by the implementation.
The probe prints disagreements and returns a failing exit code if any occur.
It also warms both implementations and times 10,000 calls of each over identical
inputs. `Sqrt`, 64-bit VBA7 and arguments outside the existing x87 domain are unchanged.

## Error budget

Let u = 2^-53. Nonzero polynomial evaluation quantities stay in the normal-double
range; the smallest admitted
input is 2^-33, the smallest nonzero reduced argument is at least 2^-85, and
the largest tangent magnitude is below 2^87. Thus neither intermediate overflow
nor underflow enters the double-double operations.

Reduction still uses the original 256-bit integers and the original 66-bit π/2.
It is exact, even after cancellation near a large multiple. Conversion to a
pair takes a double high part and converts the **integer residual**, rather than
subtracting two approximate doubles. Its relative error is at most 4u².
The same conversion builds the factorial reciprocals and true π/4 once.

The addition uses a general TwoSum on the high components, then incorporates
the low components and renormalizes. Multiplication uses FMA for the exact
high-product residual, adds the two cross terms, and renormalizes; the omitted
low-product is of order u². Division computes a high quotient and one residual
correction. For inputs with |lo| <= 2u|hi|, conservative local bounds are:

- addition: 64u² times the sum of the operand magnitudes;
- multiplication: 64u² times the product magnitude;
- division: 256u² times the quotient magnitude.

These include rounding of the residual, cross terms, and renormalization; a
converted pair initially allows 2u rather than u in its low component. Subsequent
renormalization restores the usual u bound. The addition bound is absolute and
continues to apply when high components cancel.

The sine and cosine Horner polynomials have 16 coefficients each. On
|r| <= π/4 < 0.786, their omitted terms are bounded by |r|^33/33! and
|r|^32/32!, respectively, each below 2^-126 relative to the result.
The absolute-coefficient sums are at most sinh(|r|)/|r| < 1.11 and
cosh(|r|) < 1.33. The Horner recurrence contracts errors by r² < 0.618;
sine's final multiplication preserves a relative bound even for tiny r.
A stage contributes less than 192u² in absolute error (the coefficient sums
above bound both the product and addition). Even ignoring contraction, 16
stages contribute less than 3072u². The squared-argument error contributes less
than 64u² through the polynomial derivative; coefficient conversion, argument
conversion and the final multiplication each add less than 128u². Dividing
by the 0.7 lower bounds leaves less than 2^15 u² relative error in either
component. Cosine on this interval exceeds 0.7, as does sin(r)/r. Quadrant changes are exact; division for
tangent keeps the total below 2^17 u² relative, including a small denominator.

Atan first uses reciprocal symmetry and, above 1/2, the identity about 1.
Its series argument has magnitude at most 1/2, allowing one double ulp for
rounding in the reduction. The alternating polynomial
uses 10, 16, 32 or 49 coefficients when |r| is at most 1/64, 1/16, 1/4
or 1/2 respectively. Interval selection uses the next double above the high
component, enclosing the low component too. For N coefficients the relative
tail is at most |r|^(2N) / (0.9(2N+1)), using |atan(r)| >= 0.9|r|.
Each selected interval keeps the tail below 2^-104 relative to atan(r), including
that one-ulp allowance. The Horner contraction is 1/4 up to that allowance,
and the absolute-coefficient sum is at most
4/3. Using 256u² per stage and ignoring contraction gives less than 12544u²
for the 49 stages. Coefficient conversion, the squared argument and the final
multiplication contribute less than 1024u² together. The reduction divisions
and reconstruction add less than 4096u² relative to the final result. Near
r = 0 after the near-one transform, use an absolute argument-error bound: the
final atan magnitude is at least atan(1/2), and |atan'(r)| <= 1. This avoids
claiming relative accuracy in a nearly zero transformed argument. The total
relative error is less than 2^17 u². The reconstruction's only subtraction
has result at least atan(1/2), so it cannot amplify cancellation without bound.
At exactly 1/2, either reduction branch is within this same budget.

The full model's fixed-point truncations also fit within this budget. Fewer than
1024 series operations each truncate by less than 2^-256; their propagated
absolute error is below 2^-240. Dividing this component error by a component
of magnitude at least 2^-86 keeps its relative contribution below 2^-154.
For tangent, quotient conditioning adds the relative errors of sine and cosine,
rather than multiplying the two inverse component magnitudes. The final fixed
quotient truncation contributes less still. Atan's reciprocal and near-one
reductions likewise leave a relative reference error below 2^-150.
Use the looser **2^-88 times |Hi| bound** for all four fast results. This exceeds
2^17 u² = 2^-89 times the result magnitude and the tail and reference-truncation
errors combined: the normalized high component differs from that magnitude by
less than one double ulp, leaving almost a factor of two of slack.

## Certifying both rounds

Scale the pair by 2^(63 - exponent), using the exponent of its magnitude.
When the high component is an exact power of two and the low component points
towards zero, use the lower exponent: spacing immediately below a power of two
is half the spacing above it. The scaled high component is an exact integer in
[2^63, 2^64], with a negative tail at the upper endpoint. If the approximation
error straddles the power of two itself, both first-round results are that same
power of two; that point is far from either adjacent rounding midpoint. The error
bound on this scale is at most 2^-24. If the low component is within that
distance of a half integer, use the full model. Otherwise its nearest integer
certifies exactly the model's 64-bit rounding result.

Add that integer to the scaled high double. IEEE addition performs the second,
53-bit round, including exact halfway cases. There is no approximate low part
left at that stage: the sum's exact value is the certified 64-bit significand.
Power-of-two rescaling is exact in the admitted output range. Negative values
use the same magnitude calculation and restore the sign. Tiny inputs retain
the original shortcut, including signed zero and subnormals.

The suite checks the guard at a 64-bit boundary on both sides of a power of two
and checks both sides of a
53-bit midpoint whose first rounding lands on that midpoint. Those are tests
of the model's storage rule, not newly asserted VB6 measurements.

## Results (2026-11-23)

Release, .NET 11, WSL Ubuntu on a shared host. Each timing is 10,000 calls after warm-up, over the same arguments
for both implementations; absolute costs vary with host load. The full-model column is the unchanged #5259 code.
These were taken with the probe's first timing order: each function warmed, then timed three times, before the next.

| Function | Full model, µs/call | Fast, µs/call | Fast/full |
|---|---:|---:|---:|
| Sin | 44.299 | 1.339 | 0.0302 |
| Cos | 34.157 | 0.951 | 0.0279 |
| Tan | 51.182 | 1.342 | 0.0262 |
| Atn | 34.826 | 0.360 | 0.0103 |

**10,000,000 comparisons per function, zero disagreements** with the full model. These compare two models of the
same hardware; they don't extend the VB6 oracle.

With the four entry points routed back to the full model, the timing test fails: every ratio exceeds 0.1.

## Timing order (#5590, 2026-11-26)

The probe now warms all four functions before it times any, then times each over five passes in turn, keeping each
function's fastest. Otherwise the function listed first pays the penalty measured below. Measurements recorded in [#5590](https://github.com/hexide-io/HexIDE/issues/5590), Release, .NET 11.0.100 SDK.

**main's probe on an Apple M4 Mac mini** (arm64, macOS, 10 cores):

| Condition | Runs | Result |
|---|---:|---|
| Idle | 5 | Sin 0.0391–0.0405, Cos 0.0381–0.0400, Tan 0.0407–0.0423, Atn 0.0493–0.0524. Exit 0 every time. |
| Idle, Cos listed first | 5 | The first slot's fast time was 0.470, 0.370, 0.470, 0.473 and 0.370 µs. Sin, second, was 0.349–0.352 µs. |
| 14 busy loops on the 10 cores | 3 | Sin (first) 0.565–0.694 µs fast, Cos 0.397–0.401 µs. Every ratio was 0.045 or less. |
| Every method held at tier 0 (`DOTNET_TC_CallCounting=0`) | 3 | Fast 1.50–1.55 µs for Sin and Cos, about 4.3 times the tiered 0.35 µs. Ratios 0.055–0.061. |

So on that machine Sin's fast path costs what Cos's does, and nothing reached 0.1. A per-architecture bound would widen the test with no cause, so it's out.

The function timed first does pay a measured penalty, in 3 of the 5 idle Cos-first runs and all 3 loaded runs: 1.3 to 1.8 times the others' fast time. CI's 4.4 times is close to the tier-0 factor. That this is the CI cause is reasoned, not reproduced: nothing here produced CI's size of effect.

**Updated timing order, in a scratch copy of main's probe:**

| Machine and condition | Runs | Result |
|---|---:|---|
| M4, idle | 5 | Sin 0.0388–0.0393, Cos 0.0388–0.0395, Tan 0.0403–0.0418, Atn 0.0219–0.0227 |
| M4, idle, Cos listed first | 5 | Cos 0.346–0.349 µs fast, Sin 0.346–0.350 µs: no first-slot penalty |
| M4, 14 busy loops | 3 in each order | Every ratio 0.0102–0.0224. The first slot was 0.391–0.395 µs. |
| Windows x64 workstation | 3 in each order | Every ratio 0.0197–0.0308 |
| `X87Math.cs:25-28` routed to the four `*Exact` methods | 1 on each machine | Ratios 0.99–1.02, exit 1 |

The tenth bound is unchanged. CI's 4.4-times excess was not reproduced; its cause is reasoned from these
measurements. The issue stays open pending sustained green arm64 CI legs.
