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
written chain. The maintainer returns these two files and the public `intermediate.json` and
`intermediate.sig` chain files. Issuance requires maintainer review; no marketplace service is implied.
The root supplied during issuance must be the root trusted by the intended HexIDE build.

Put those four public chain files in your `pack --trust <dir>` directory. The packer currently reads
your own private signing key from `firstparty.key` in that directory; the filename does not grant
first-party status. Pack with your own key, and keep it private.

Publish your add-in signing key's fingerprint somewhere users already trust you (your website, repo, or
release notes). A user can then compare it against the **Publisher** row in HexIDE's trust inspector.
The fingerprint identifies the key independently of the issued display name.
