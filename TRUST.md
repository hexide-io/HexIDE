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

HexIDE refuses an add-in build, a publisher or a marketplace intermediate that has been revoked, and an add-in signed
with a revoked key. The list of revocations is signed by the HexIDE root, and HexIDE checks that signature before using
it.

- **A revoked key** is named by the SHA-256 fingerprint that **Trust details** shows. Revoking a publisher's stolen key
  leaves the record issued for their new key standing. A revoked publisher key refuses every add-in it signed, the
  first-party key's included. A revoked intermediate key refuses the add-ins under it, except first-party ones, so a
  stolen marketplace key can't switch off the add-ins HexIDE ships with. HexIDE versions from before key revocation
  ignore it and still apply the rest of the list, so a malicious publisher's id is revoked as well.

- **What is requested:** `https://hexide.io/revocations.json` and its signature,
  `https://hexide.io/revocations.json.sig`. They are plain downloads with no identifier, settings or project
  information. After the first, the list is asked for with an `If-None-Match` header carrying the tag hexide.io gave
  for the last list that verified, so an unchanged list isn't downloaded again. HexIDE keeps that tag in memory only.
  hexide.io sees the requests' address and time, as any website does.
- **When:** at every start with at least one add-in installed, before any add-in loads, and then about every hour
  while HexIDE runs. A default install counts, because HexIDE ships with the AI Chat add-in. At startup HexIDE
  waits at most three seconds; the hourly check runs in the background. If hexide.io can't be reached, HexIDE goes
  on with the list built into your version and the last list it fetched.
- **When a loaded add-in is revoked:** HexIDE marks it Revoked and takes out its menus, commands, tool windows,
  language servers and the rest of what it contributed, and it hears no more events. Its code stays loaded until
  HexIDE closes, so a notice names the add-in and offers to close HexIDE, through the usual prompts to save. The next
  start refuses it.
- **Seeing it work:** Options → Add-Ins says when the list was last checked and which list is in use, that the last
  check failed and when one last worked, or that checking is off.
- **Turning it off:** set `revocationListUrl` to `""` in `settings.json`, in HexIDE's settings directory (see
  [`--user-data-dir`](docs/command-line.md)). Removing the line, or setting it to `null`, restores the default. Another
  address works too, for a mirror, but the list must still be signed by the HexIDE root. A profile can't change this
  setting. The hourly check reads the setting each time, so turning it off needs no restart. The interval itself
  has no setting.
- **What turning it off costs:** HexIDE then knows only the list built into your version, and any list it fetched
  before. An add-in build revoked after your version was released, a first-party one included, keeps loading until
  you update HexIDE.

## For third-party publishers

To get a publisher record, make a signed request with the add-in packer:

```sh
request-publisher --publisher-id <id> --display-name <name> --github <handle> --fingerprint-url <https url>
                  --new-key <dir> --out <dir> [--namespaces <a.,b.>]
```

- **`--new-key <dir>`** makes your private key, as `<id>.key` in that folder, readable only by you where the system
  supports it. Keep it out of anything you publish or commit, and keep a backup: nobody can recover it. To use a key you
  already have, give `--key <file>` instead.
- **The request is `request.json` and `request.sig`,** written to `--out`. The request holds your public key, and the
  signature proves you hold the private key that goes with it. The command prints the key's fingerprint and the
  request's SHA-256.
- **Publish the fingerprint at `--fingerprint-url`,** an https address on your own site. That shows the request comes
  from whoever controls that site.
- **Check a request** with `verify-request --request <dir>`. It needs no private key, and it is the same check the
  maintainer runs.
- **Submit** `request.json` and `request.sig` through the HexIDE repository's issue tracker, from the GitHub account the
  request names. A request form in `hexide-io/HexIDE.Publishers` will replace this (#5323). Everything you submit is
  public. Never send your private key.
- **To replace your key** under an id you already hold, add `--previous-key <file>` with the key it replaces. The request
  is then signed with both keys. If the old key was stolen and has been revoked, its signature proves nothing, so the
  maintainer checks the replacement by hand against your GitHub account and the fingerprint on your site.

**The rules each field must meet:**
- **Ids** are 3–64 characters matching `^[a-z0-9]+([.-][a-z0-9]+)*$`: lowercase letters and digits in parts joined by
  single dots or hyphens.
- **Display names** must be trimmed, non-empty, at most 128 characters, and contain no control characters or invisible
  format characters (such as a right-to-left override), which could make one name read as another.
- **Namespaces** are names followed by `.`, such as `acme.`. They are the prefixes your add-ins' commands and messages
  may use. No two publishers hold overlapping namespaces, and `hexide.` is HexIDE's own.

The maintainer issues the record offline from the approved request:

```sh
issue-publisher --request <dir> --approved-sha256 <hash> --root-public-key <file> --intermediate-key <file>
                --intermediate <dir> --ledger <dir> --out <dir> [--revocations <json> --revocations-sig <sig>]
```

- **It signs only what was approved.** The request's SHA-256 must be the one the maintainer approved, and every
  `verify-request` check runs again against the ledger and the root, and against the revocation list, which a key
  replacement needs. It refuses anything that needs a person's judgement, rather than signing it.
- **It asks for your publisher id to be typed back** before it signs.
- **It writes your trust folder:** `publisher.json` and `.sig`, `intermediate.json` and `.sig`, `hexide-root.pub`, a
  copy of your request, and `receipt.md` with the fingerprints. It also adds the record to the public ledger,
  `publishers/<id>/`.
- **It never issues an id or a key twice.** A key that has been replaced stays held under `retired/`.

`sign-publisher` stays for first-party records, and for a record a person decides to issue by hand. It writes only
`publisher.json` and `.sig`, so the maintainer returns those with the two intermediate files and `hexide-root.pub`, the
root they chain to. Issuance requires maintainer review, and no marketplace service is implied. The root supplied
during issuance must be the root trusted by the intended HexIDE build.

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
