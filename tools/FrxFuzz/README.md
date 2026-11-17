# Fuzzing a form's `.frx` (#1222)

A coverage-guided fuzz harness for the companion `.frx` that HexIDE reads beside a form. AFL++ drives it, and
[SharpFuzz](https://github.com/Metalnem/sharpfuzz) (MIT) instruments the .NET assemblies so AFL sees which branches
each input reaches. It's a tool for long runs on a machine of your own. It isn't part of the product, the solution, any
test run or CI.

## What it reaches

The `.frm` named on the command line stays fixed, and AFL supplies the `.frx` bytes. Each input goes through what
opening, showing and saving the form does without a window:

1. `FrxDeserializer.Read` cuts the companion at the offsets the form cites.
2. `FormDeserializer.Deserialize` builds the form. It holds each cited record and collects an ImageList's pictures.
3. Every record goes through `FrxImageHelper`: its kind, its payload, and Avalonia's Skia decode of a bitmap or an
   icon. The harness starts Avalonia headless with Skia, so the decode is the real one.
4. A StatusBar's panel pictures go through `PanelPicture.FromRecord`. A RichTextBox's `TextRTF` goes through
   `RichTextBoxPersistence.ReadDocument` and `RtfToWrite`, as the designer and a running form do.
5. When HexIDE would let the form be saved, `FormSerializer` renders it and its companion again, as a save does. The
   save path refuses any other form before rendering it, so the harness does too.
6. What that save wrote is read back and saved again.

With `--frm` in place of a form, AFL supplies the `.frm` itself instead, with no companion, as a form whose `.frx` is
missing is read. The same steps follow.

It doesn't reach anything that needs a window: drawing a control, or a running form's own use of a record.

**What counts as a finding:**
- any exception except `NotImplementedException`, which is HexIDE's deliberate refusal of a record it doesn't
  implement yet, naming the issue that would;
- a hang past AFL's timeout;
- an allocation the run's heap limit refuses;
- a save that damages the file: a form HexIDE saved that it can't read back, or then refuses to save, or whose second
  save writes different bytes.

`ProjectLoader` catches an exception from a form and reports the form as failing to load, so a finding seldom takes
the IDE down. It's still a defect, because a damaged `.frx` should be refused by name, not fail somewhere inside a
decoder.

## Running it

Linux, or WSL, with the .NET 10 SDK and AFL++ (`apt install afl++`; measured with 4.33c). From the repository root:

```sh
dotnet build tools/FrxFuzz/FrxFuzz.csproj -c Release
dotnet tool install --tool-path ~/.local/sharpfuzz SharpFuzz.CommandLine --version 2.3.0
B=tools/FrxFuzz/bin/Release/net10.0
~/.local/sharpfuzz/sharpfuzz $B/HexIDE.Runtime.dll
~/.local/sharpfuzz/sharpfuzz $B/HexIDE.Core.dll
```

Instrumenting rewrites those two files in place. Rebuild before instrumenting again.

Seed each run from one VB6-saved form in `corpus/inbox-controls/saved/`, outside the repository:

```sh
mkdir -p ~/fuzz/in/imagelist && cp corpus/inbox-controls/saved/imagelist/three/Form1.frx ~/fuzz/in/imagelist/
AFL_SKIP_BIN_CHECK=1 AFL_FORKSRV_INIT_TMOUT=60000 DOTNET_GCHeapHardLimit=0x20000000 \
  afl-fuzz -i ~/fuzz/in/imagelist -o ~/fuzz/out/imagelist -t 2000 -m none -- \
  dotnet $B/FrxFuzz.dll corpus/inbox-controls/saved/imagelist/three/Form1.frm
```

- `AFL_SKIP_BIN_CHECK` is needed because `dotnet` itself isn't instrumented.
- `AFL_FORKSRV_INIT_TMOUT` allows for the runtime's start-up.
- `-m none` is needed because the runtime reserves far more address space than it uses. The heap limit,
  `DOTNET_GCHeapHardLimit` (512 MB here), does that job instead.

To fuzz the `.frm` itself, seed from a few small forms of different shapes: intrinsic controls with a nested Frame, a
control array, a Font block and a menu, plus in-box controls and a module with member attributes. Run several
instances on one output directory (`-M main` for one, `-S s1` and so on for the others), and give the harness `--frm`
in place of the form:

```sh
afl-fuzz -M main -i ~/fuzz/frm-in -o ~/fuzz/frm-out -t 2000 -m none -- dotnet $B/FrxFuzz.dll --frm
```

Good `.frx` seeds hold different kinds of record: `imagelist/three` (ImageList pictures), `richtextbox` (`TextRTF`) and
`statusbar-picture` (a panel picture). Run each for as long as you can spare; AFL's own status says when it has stopped
finding new paths.

## Triage

Replay inputs to see what each does, with the full exception and stack:

```sh
dotnet $B/FrxFuzz.dll <form.frm> --replay ~/fuzz/out/imagelist/default/crashes/id*
```

Then:
1. Minimise each with `afl-tmin -i <crash> -o <min> -t 2000 -m none -- dotnet $B/FrxFuzz.dll <form.frm>`, using the
   same environment as the run.
2. Group the crashes by the top frames of the stack in HexIDE's code.
3. For each root cause, fix it, with a regression test in `HexIDE.Runtime.Tests` that holds the minimal input as bytes
   and fails before the fix.

Don't commit AFL's output directory, or any input beyond those minimal regression inputs.
