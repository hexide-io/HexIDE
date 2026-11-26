# Add-in packer

## hexide-pack: the packer that asks

`hexide-pack` (`IDE/HexIDE.AddinPacker.Interactive`) takes the same commands and options as this packer. On a
terminal, it asks for any required option you leave out, and then the optional ones, with Enter to skip. It checks
each answer as you give it, shows the whole command, and asks before running it. With no command it offers the list.
It hands the finished command line to this packer's own dispatch, `PackerCli.Run`, so there is one signing path.

```sh
dotnet run --project IDE/HexIDE.AddinPacker.Interactive/ -- pack
```

- **It asks only on a terminal, outside CI.** `--no-input` turns asking off. Anywhere else, a missing option fails at
  once, every missing option is listed, and the message says why it didn't ask.
- **A complete command runs straight away,** without asking anything.
- **It refuses a folder for private keys inside a git clone,** the development chain's included. Run
  `HexIDE.AddinPacker genkeys` directly to put the development chain where a clone expects it. A home folder that is
  itself a git work tree, as some dotfiles setups are, counts as a clone. Choose a folder on another drive, such as
  removable media, or one outside the work tree.
- **It refuses some command lines this packer accepts:**
  - an option it doesn't know, which `pack`, `genkeys-release` and `sign-revocations` ignore;
  - a single-valued option given twice, where those three take the first value. `sign-publisher` and
    `verify-revocations` refuse both, as hexide-pack does;
  - a blank value, which `pack` takes for `--description` and `--author`.

Spectre.Console, which draws the prompts, is a package of `hexide-pack` only. This project takes no packages, because
the release's signing job runs it next to the first-party key.

## Requesting a publisher record

`request-publisher` makes a publisher's signed request for a record (#5321), and `verify-request` checks one. TRUST.md
says how a publisher uses them.

```sh
request-publisher --publisher-id <id> --display-name <name> --github <handle> --fingerprint-url <https url>
                  (--new-key <dir> | --key <file>) --out <dir> [--namespaces <a.,b.>] [--previous-key <file>]
verify-request --request <dir> [--ledger <dir>] [--github <handle>]
```

- **The request is `request.json`, one canonical line of ASCII.** The maintainer approves its SHA-256, so
  `verify-request` refuses any other form of the same content: whitespace, reordered or repeated fields, a byte-order
  mark, or an unknown field.
- **Every signature covers `hexide-publisher-request-v1` and a newline, then the request's bytes.** So a request signature
  can never pass for a manifest's or a record's, nor theirs for a request's.
- **A rotation is signed twice,** with the new key in `request.sig` and the key it replaces in `request.prev.sig`. Both
  sign the same bytes, which hold the id and both keys, so the old key's signature can't be lifted onto another request.
- **`--ledger <dir>`** is a folder of issued records, `<id>/publisher.json` each. With it, `verify-request` refuses an id,
  key or namespace another publisher already holds. Namespaces count as held when one is inside the other, ignoring
  case. A rotation must replace the key the ledger holds for its id. An entry that doesn't parse refuses.
- **`--github <handle>`** is the account that submitted the request. It must match the request's own, ignoring case.
- **`--root <file>`** chain-verifies every ledger record, live and retired. It also checks each record's stored request:
  it must be signed under the record's key, with the record's id and, byte for byte, the record's key. An entry that
  doesn't verify refuses.
- **`--revocations <json> --revocations-sig <sig>`** is the signed revocation list, verified against `--root`. A rotation
  of a revoked publisher id is refused.
- **Exit codes:**
  - **0:** verified.
  - **2:** refused.
  - **3:** verified, but something only a person can settle:
    - a rotation checked without the ledger or the revocation list;
    - a rotation whose ledger entry has no stored request, or a different handle or `fingerprintUrl`;
    - a revocation list more than 7 days old.
  - **1:** a usage or I/O error.

  Only 0 is a pass.

## Issuing a publisher record

`issue-publisher` turns an approved request into a signed record (#5322), offline, on the machine with the intermediate
key. It treats the request as hostile there too.

- **It runs every `verify-request` check again,** with the ledger, the root and the revocation list. A rotation needs the
  list. It signs only a request whose check exits 0, and whose SHA-256 equals `--approved-sha256`.
- **It shows the record and asks for the publisher id to be typed back,** compared ordinally. Input that isn't a
  terminal is refused, with no way around it.
- **It writes `--out`:** the four chain files, `hexide-root.pub`, the request with its signatures, and `receipt.md`. Then
  it writes the same, without the root key, to `<ledger>/<id>/`. A rotation first moves the live files to
  `<ledger>/<id>/retired/<n>/`, where `<n>` is 1, 2, 3 and so on with no gaps.
- **It verifies both written chains** against the root.
- **`receipt.md`** gives the fingerprints, and puts what the request said in a fenced block, so GitHub can't render it as
  links or HTML.

The ledger is `HexIDE.Publishers`' `publishers/` folder; committing it publishes the record.
- **It treats a request as hostile.** It refuses a request file over 16 KiB, or a link, before reading it. Every line it
  prints starts with its own words, so a request can't forge a line of a workflow's log.

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
`intermediate.json/.sig` and `publisher.json/.sig`. To pack with them, give `pack --trust` a folder holding
`hexide-root.pub` and the four JSON/signature records, and `pack --key` the `firstparty.key` file, or copy those five
beside `firstparty.key` and leave out `--key`. `pack` writes `hexide-root.pub` into each package as `root.pub`, and
refuses one that didn't sign the intermediate (#5316). Root and intermediate private keys are not inputs to `pack`.

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
- `dotnet run --project IDE/HexIDE.AddinPacker/ -- verify-revocations --json IDE/HexIDE/Addins/Trust/revocations.json --sig IDE/HexIDE/Addins/Trust/revocations.sig --root IDE/HexIDE/Addins/Trust/hexide-root.pub`, after the copy and on the committed files, since the check is on the bytes the build embeds
- publish the same two files at `https://hexide.io/revocations.json` and `.sig`, and set `REVOCATIONS_MUST_VERIFY` to 1 in `.github/workflows/publish.yml`
- `signing/public/hexide-root.pub` → `IDE/HexIDE.AddinPacker/firstparty/hexide-root.pub`
- `signing/public/hexide-firstparty.pub` → `IDE/HexIDE.AddinPacker/firstparty/hexide-firstparty.pub`
- `signing/public/intermediate.json` → `IDE/HexIDE.AddinPacker/firstparty/intermediate.json`
- `signing/public/intermediate.sig` → `IDE/HexIDE.AddinPacker/firstparty/intermediate.sig`
- `signing/public/publisher.json` → `IDE/HexIDE.AddinPacker/firstparty/publisher.json`
- `signing/public/publisher.sig` → `IDE/HexIDE.AddinPacker/firstparty/publisher.sig`
- `signing/firstparty/firstparty.key` → `HEXIDE_FIRSTPARTY_KEY` in the `addin-signing` environment, not the repository's secrets.
  Only `publish.yml`'s `sign-addins` job reads it, and that job builds nothing. `build.yml` packs with a throwaway chain it
  generates and deletes in each run (#4740). What remains trusted with the key is the packer binary, built in a job of its
  own whose build-time code is only the SDK's and HexIDE.Core's one analyzer package.
- `IDE/HexIDE.AddinPacker/firstparty/README.md`
- `TRUST.md` — `HexIDE root`, `First-party publisher` SHA-256 fingerprints

`pack` refuses a key that isn't the one its trust folder's `publisher.json` names, whether `--key` gives it or it is the
trust folder's `firstparty.key` (#5318), before it writes anything,
and the error gives both fingerprints. So a release whose `HEXIDE_FIRSTPARTY_KEY` and committed records come from different
chains fails at packing instead of shipping first-party add-ins that HexIDE refuses. A Desktop build fails the same way
when `firstparty/` holds a key left over from an earlier chain: remove it, or get the current key (#4109).
`CommittedFirstPartyChainTests` checks that the records chain to the embedded root and first-party key, and that both
copies of each public key agree.

## Checking a revocation list

`verify-revocations --json <file> --sig <file> --root <public key>` needs no private key. It reads the list as the IDE's
revocation store does: the JSON's exact bytes, the signature's text trimmed, then the JSON parsed. It exits 0 when the
list would be accepted, 2 when it wouldn't, and 1 for a usage or file error. A list re-saved with other line endings no
longer verifies, so check the bytes that ship, not a copy.

`.github/workflows/publish.yml` runs it against hexide.io's list on every release, and a list that doesn't verify fails
the release. `REVOCATIONS_MUST_VERIFY: 0` in that workflow makes it a warning instead.

## Replacing package output

`pack --out` accepts a new or empty directory, or an unchanged package signed with the supplied trust directory's
`firstparty.key`. It checks the signed manifest, every payload hash, the supplied trust-chain bytes,
and the exact inventory before replacement. Added files, extra directories, edits, and symbolic links
in the output (or the output itself being a link) cause refusal. Links above the output folder are allowed.
A refusal names the path, explains whether it is linked or not unchanged tool output, confirms nothing
was changed, and asks you to choose another folder or move this one aside.
Choose a fresh path or explicitly move an unrelated/edited folder aside; changing keys or trust files
also needs a fresh output path.

Packing happens in a unique sibling first, so a failed build leaves the previous package in place.
After a successful installation, the unchanged earlier package is deleted. If the installation rename
fails, the previous output is restored from its unique sibling before the error is reported.

Cleanup after a successful installation is best-effort: if it cannot remove the old package's temporary
container, `pack` prints that container's path on stderr and succeeds. The container has no root manifest,
so the IDE ignores it. It can be removed manually once no longer needed.
