# Name the root an unverified add-in's chain leads to (#5316)

## Why

An add-in signed under a root this HexIDE doesn't carry was reported exactly like an unsigned or broken one
([#5316](https://github.com/hexide-io/HexIDE/issues/5316)). That happens to every third-party add-in during the move to
the v1 root (#4109), in both directions, and a publisher with a valid record gets no hint of the cause.

A package didn't carry its root's public key, so HexIDE couldn't say which root. The maintainer decided on 2026-11-24
that packages carry it, as HexIDE is still alpha and older builds refusing new packages is no concern.

## What changes

- **The packer** writes the trust folder's `hexide-root.pub` into each package as `root.pub`. It refuses a root that
  didn't sign the intermediate.
- **The verifier**, when the intermediate's signature fails under the embedded root, checks it under the package's
  root, then the rest of the chain. The package is refused either way.
- **The refusal** names that root's fingerprint only when the whole chain verifies under it.
- **Options and the consent line** say the add-in was signed under a root this build doesn't carry, and ask the user to
  compare the fingerprint with a newer release's TRUST.md.

The rules agreed with the maintainer:
- the carried key is never trusted, remembered or used for any decision;
- nothing offers to trust it;
- the wording never says the add-in is genuine, because a newer HexIDE's root and a forger's look alike.
