# Add-in packer

## Release key generation

`genkeys-release` is separate from `genkeys <outDir>`, which retains its development defaults.
All four output directories must be separate and must not contain one another. The command resolves symbolic links and junctions when checking overlap, refuses
existing destination files, reserves every file before writing, and removes its new files on failure.
It prints the root, intermediate and publisher public-key fingerprints, never private key material.
Private-key folders get default file permissions; keep them on removable media.

```sh
dotnet run --project IDE/HexIDE.AddinPacker/ -- genkeys-release \
  --root-out signing/root --intermediate-out signing/intermediate \
  --firstparty-out signing/firstparty --public-out signing/public \
  --intermediate-id hexide-marketplace --intermediate-name "HexIDE Marketplace" \
  --firstparty-id com.hexide.firstparty --firstparty-name "HexIDE (First-Party)"
```

```text
HexIDE.AddinPacker.exe genkeys-release --root-out signing/root --intermediate-out signing/intermediate --firstparty-out signing/firstparty --public-out signing/public --intermediate-id hexide-marketplace --intermediate-name "HexIDE Marketplace" --firstparty-id com.hexide.firstparty --firstparty-name "HexIDE (First-Party)"
```

The three private directories contain only `root.key`, `intermediate.key` and `firstparty.key`,
respectively. `--public-out` contains `hexide-root.pub`, `hexide-firstparty.pub`,
`intermediate.json/.sig` and `publisher.json/.sig`. To use the existing `pack --trust` interface,
copy the four JSON/signature records beside `firstparty.key`; root and intermediate private keys
are not inputs to `pack`.

Ids follow the grammar in [TRUST.md](../../TRUST.md). Display names must be non-empty, already
trimmed, at most 128 characters, and contain no control, Unicode format, line-separator or
paragraph-separator characters. The same rule applies to `sign-publisher`.

## Self-contained Windows publish

From the repository root:

```sh
dotnet publish IDE/HexIDE.AddinPacker/ -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=None -p:DebugSymbols=false -o artifacts/addin-packer-win-x64
```

`artifacts/addin-packer-win-x64/HexIDE.AddinPacker.exe` includes the .NET runtime. It accepts the
same commands without a `dotnet` prefix. No CI artifact is added by this command.

## Root rotation checklist

- `signing/public/hexide-root.pub` → `IDE/HexIDE/Addins/Trust/hexide-root.pub`
- `signing/public/hexide-firstparty.pub` → `IDE/HexIDE/Addins/Trust/hexide-firstparty.pub`
- `dotnet run --project IDE/HexIDE.AddinPacker/ -- sign-revocations --in IDE/HexIDE/Addins/Trust/revocations.json --root-key signing/root/root.key --out signing/revocations`
- `HexIDE.AddinPacker.exe sign-revocations --in IDE/HexIDE/Addins/Trust/revocations.json --root-key signing/root/root.key --out signing/revocations`
- `signing/revocations/revocations.json` → `IDE/HexIDE/Addins/Trust/revocations.json`
- `signing/revocations/revocations.sig` → `IDE/HexIDE/Addins/Trust/revocations.sig`
- `signing/public/hexide-root.pub` → `IDE/HexIDE.AddinPacker/firstparty/hexide-root.pub`
- `signing/public/hexide-firstparty.pub` → `IDE/HexIDE.AddinPacker/firstparty/hexide-firstparty.pub`
- `signing/public/intermediate.json` → `IDE/HexIDE.AddinPacker/firstparty/intermediate.json`
- `signing/public/intermediate.sig` → `IDE/HexIDE.AddinPacker/firstparty/intermediate.sig`
- `signing/public/publisher.json` → `IDE/HexIDE.AddinPacker/firstparty/publisher.json`
- `signing/public/publisher.sig` → `IDE/HexIDE.AddinPacker/firstparty/publisher.sig`
- `signing/firstparty/firstparty.key` → `HEXIDE_FIRSTPARTY_KEY` (CI secret)
- `IDE/HexIDE.AddinPacker/firstparty/README.md`
- `TRUST.md` — `HexIDE root`, `First-party publisher` SHA-256 fingerprints
