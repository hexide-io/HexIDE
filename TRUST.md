# HexIDE Add-In Trust Anchors

HexIDE verifies every add-in against a baked-in chain of trust (publisher → marketplace intermediate →
HexIDE root). The IDE can prove an add-in *chains to* a given key; only **you** can confirm that a key
really belongs to who it claims. This page publishes the canonical fingerprints so you can close that loop
out of band — the thing MSIX hides and ClickOnce let you check.

## How to verify

Open an add-in's **Trust details…** (from the first-load consent prompt) or the **Trust** section on
*Tools → Options → Add-Ins*. Each link shows a **SHA-256 key fingerprint**. Compare:

- the **HexIDE root** fingerprint against the value below — this confirms your copy of HexIDE trusts the
  genuine root and hasn't been tampered to trust a rogue one;
- a **publisher** fingerprint against the value the publisher publishes themselves (for first-party
  add-ins, the value below);
- the **build hash** against the release hash the publisher publishes, to confirm it's the exact build.

The same fingerprints are shown in **Help → About → Add-in signing keys**.

A fingerprint is `SHA-256` of the key's DER SubjectPublicKeyInfo, shown as upper-case hex in
space-separated groups of four.

## Canonical fingerprints

| Anchor | SHA-256 fingerprint |
|--------|---------------------|
| **HexIDE root** | `DF36 B067 BF3A C047 CC28 B9E4 D5D4 B323 A491 84D4 C28E B0ED F9E8 9928 89D3 F491` |
| **First-party publisher** (`com.hexide.firstparty`) | `7929 689A 38A1 DF58 9869 3FD3 2B67 1249 909C 99D5 C7F6 6092 22D4 FB35 7312 B4C8` |

## The revocation list HexIDE fetches

HexIDE refuses an add-in build, a publisher or a marketplace intermediate that has been revoked. The list of
revocations is signed by the HexIDE root, and HexIDE checks that signature before using it.

- **What is requested:** `https://hexide.io/revocations.json` and its signature,
  `https://hexide.io/revocations.json.sig`. They are two plain downloads with no data from HexIDE added: no
  identifier, settings or project information. hexide.io sees the requests' address and time, as any website
  does.
- **When:** at every start with at least one add-in installed, before any add-in loads. A default install counts,
  because HexIDE ships with the AI Chat add-in. HexIDE waits at most three seconds. If hexide.io can't be reached,
  it starts anyway with the list built into your version and the last list it fetched.
- **Turning it off:** set `revocationListUrl` to `""` in `settings.json`, in HexIDE's settings directory (see
  [`--user-data-dir`](docs/command-line.md)). Removing the line, or setting it to `null`, restores the default. Another
  address works too, for a mirror, but the list must still be signed by the HexIDE root. A profile can't change this
  setting.
- **What turning it off costs:** HexIDE then knows only the list built into your version, and any list it fetched
  before. An add-in build revoked after your version was released, a first-party one included, keeps loading until
  you update HexIDE.

## For third-party publishers

Request a publisher record from the HexIDE maintainer through the repository's issue tracker. Send
only your **public key** (base64 ECDSA P-256 SubjectPublicKeyInfo), your publisher id and display name.
Never send your private key. Publisher and intermediate ids are 3–64 characters matching
`^[a-z0-9]+([.-][a-z0-9]+)*$`: lowercase letters and digits in parts joined by single dots or hyphens.
Display names must be trimmed, non-empty, at most 128 characters, and contain no control characters or
invisible format characters (such as a right-to-left override), which could make one name read as another.

The maintainer issues the record offline with the add-in packer:

```sh
sign-publisher --root-public-key <file> --intermediate-key <file> --intermediate <dir> --publisher-id <id> --display-name <name> --public-key <file> --out <dir>
```

The command verifies the intermediate against the supplied root and refuses a mismatched intermediate
private key or existing output files. It writes `publisher.json` and `publisher.sig`, then verifies the
written chain. The maintainer returns these two files, the public `intermediate.json` and `intermediate.sig` chain
files, and `hexide-root.pub`, the root they chain to. Issuance requires maintainer review; no marketplace service is
implied.
The root supplied during issuance must be the root trusted by the intended HexIDE build.

`hexide-pack`, built from `IDE/HexIDE.AddinPacker.Interactive`, runs every packer command and asks for any option
you leave out, so `hexide-pack pack` walks you through signing an add-in.

Put those five public files in your `pack --trust <dir>` directory, and give your private signing key with
`pack --key <file>`, wherever you keep it. Keep the key out of any folder you publish or commit. `pack` refuses a key
that isn't the one your `publisher.json` names, and a key named among the files to bundle. Without `--key`, `pack`
reads `firstparty.key` from the trust folder; that filename does not grant first-party status.

Every package carries its root's public key as `root.pub`, and `pack` refuses a `hexide-root.pub` that didn't sign the
intermediate. A HexIDE that doesn't trust that root still refuses the add-in, as before. It now says the add-in was
signed under a root it doesn't carry, and shows that root's fingerprint, so a user can compare it with the
[canonical fingerprints](#canonical-fingerprints) a newer release publishes. HexIDE shows the fingerprint only when the
add-in's chain really verifies under that key, and never trusts it.

Publish your add-in signing key's fingerprint somewhere users already trust you (your website, repo, or
release notes). A user can then compare it against the **Publisher** row in HexIDE's trust inspector.
The fingerprint identifies the key independently of the issued display name.

## Signing record identifiers

New publisher and intermediate ids issued by the packer use 3–64 characters: lowercase ASCII letters
and digits, in parts joined by single dots or hyphens (`^[a-z0-9]+([.-][a-z0-9]+)*$`). Leading, trailing
or repeated separators, path separators, whitespace and control characters are refused. This issuance
rule does not change verification of existing packages. Both issuance commands require display names
to be trimmed, non-empty, at most 128 characters and free of control, Unicode format, line-separator
and paragraph-separator characters.

Release key generation, the self-contained Windows publish command and the root rotation file checklist
are in the [packer README](IDE/HexIDE.AddinPacker/README.md). The embedded revocation floor must be
re-signed when rotating the root: `RevocationStore` verifies its signature against that root.
