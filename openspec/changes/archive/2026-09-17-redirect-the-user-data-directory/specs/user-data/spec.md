## ADDED Requirements

### Requirement: Per-user files SHALL live in one directory
Every file HexIDE keeps for a user — settings, window layout, recent projects, language server
configuration, add-in consent and revocations, user translations — SHALL be read from and written to a
single per-user directory, and that directory SHALL be an absolute path.

By default it is `%AppData%\HexIDE` on Windows, `$XDG_CONFIG_HOME/HexIDE` on Linux and macOS where
`XDG_CONFIG_HOME` is set, and `~/.config/HexIDE` otherwise. Where no absolute location can be determined, the
IDE SHALL fail rather than fall back to a relative path, because every consequence of a relative one is
silent: settings not persisting, consent asked again, a revocation not found.

Logs are not per-user files for this purpose.

#### Scenario: A file is always found where it was written
- **WHEN** a setting is written and later read in the same session
- **THEN** both use the same absolute directory, whatever the working directory is

### Requirement: A session SHALL be able to keep its per-user files elsewhere
When HexIDE is started with `--user-data-dir <path>`, every per-user file for that session SHALL be read from
and written to that directory instead of the default, and the default directory SHALL be neither read nor
written.

A directory that does not exist SHALL be created on first use, so a new one behaves as a first install.

The redirect SHALL apply for the whole session or not at all. A session SHALL NOT read some per-user files
from one directory and others from another.

Add-ins SHALL still load from beside the executable, which is where they are installed; only the record of
which ones the user allowed or blocked moves with the directory.

#### Scenario: A demo runs on its own configuration
- **WHEN** HexIDE is started with `--user-data-dir` naming a directory that holds its own `lsp-servers.json`
- **THEN** the language servers configured there are the ones started
- **AND** nothing in the default per-user directory changes

#### Scenario: A new directory starts from defaults
- **WHEN** HexIDE is started with `--user-data-dir` naming a directory that does not exist
- **THEN** the directory is created and the session starts with default settings

### Requirement: A malformed user data option SHALL stop the IDE starting
When `--user-data-dir` is given with no directory after it, or with another option where the directory
should be, HexIDE SHALL NOT start. It SHALL state why and exit with a non-zero status.

Every other malformed argument is skipped and the IDE starts normally. This one is not, because the option
is given to keep a session away from the user's real settings, and starting normally would run the session
on exactly those.

#### Scenario: The directory is missing
- **WHEN** HexIDE is started with `--user-data-dir` as its last argument
- **THEN** it prints that the option needs a directory and exits with status 2
- **AND** no window opens and no per-user file is read or written

#### Scenario: Another option is where the directory should be
- **WHEN** HexIDE is started with `--user-data-dir --newproject`
- **THEN** it exits with status 2 rather than creating a directory named `--newproject`
