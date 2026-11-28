# Check the revocation list hourly, and withdraw a loaded add-in it revokes (#5577)

## Why

HexIDE checked the add-in revocation list only at startup. A session left open for days or weeks went on using an
add-in build revoked after it started, and heard nothing until the next start
([#5577](https://github.com/hexide-io/HexIDE/issues/5577)).

## What changes

- **The check repeats about every hour while add-ins are installed,** in the background. The interval has no setting.
  An empty `revocationListUrl` turns this check off too, and it is read afresh each time.
- **The list is asked for conditionally,** with the ETag of the last list that verified, kept in memory only.
- **When a newer list revokes a loaded add-in:**
  - it is marked Revoked;
  - what it contributed is taken out;
  - its commands, menu items and event handlers stop running, whenever they were registered;
  - anything it registers afterwards is refused.

  Its code stays loaded until restart, and the next start refuses it.
- **The identity checked** is the one verified when the add-in loaded, so a publisher revocation still leaves a
  first-party add-in loaded.

The maintainer decided on 2026-11-25 that this ships in two parts. This one is the check and the withdrawal. The
notice, with a button that closes HexIDE, and the "last checked" line in Options follow separately.
