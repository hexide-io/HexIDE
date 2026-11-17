# Shipping HexIDE under your own name

HexIDE is MIT-licensed, and an adopter can ship it as another product without a fork. Put a `brand.json` beside
the executable, and that installation becomes your product: its own name, logo, credits and per-user state.

The schema was accepted on 2026-11-09 as part of [#2042](https://github.com/hexide-io/HexIDE/issues/2042).
The design is in [`design/white-label.md`](design/white-label.md).

## The file

```json
{
  "schemaVersion": 1,
  "id": "acme-ide",
  "name": "Acme IDE",
  "tagline": "VB6 for widget makers",
  "copyright": "© 2026 Acme Ltd.",
  "credits": ["Thanks to the Acme widget team."],
  "logo": "branding/logo.png",
  "links": {
    "home": "https://acme.example/",
    "support": "https://acme.example/help",
    "repository": "https://example.com/acme/ide"
  }
}
```

| Member | Required | Meaning |
|---|---|---|
| `schemaVersion` | Yes | Must be `1`. |
| `id` | Yes | Names the folder holding this product's settings, logs and recovery files, so **don't change it** once you've shipped. Lower-case letters, digits and inner hyphens, up to 64 characters. It can't be `hexide` or a name Windows reserves. |
| `name` | Yes | What people read: title bar, dialogs, menus, `--help`. Up to 64 characters, with no `_`, `{` or `}`. You can change it at any time without moving anybody's state. |
| `tagline` | No | Shown under the name in About, and in `--help`. |
| `copyright` | No | Your copyright line in About. |
| `credits` | No | Up to 32 entries, shown in About **after** HexIDE's own. |
| `logo` | No | A PNG of up to 1024×1024 pixels, inside the installation folder, written with `/`, for About, New Project and the window icon. |
| `links` | No | `home`, `support` and `repository`, each an absolute `https` URL. Home and support appear in Help under your product's name when supplied; the home entry is labeled “Home Page”, distinct from the existing repository web entry. Without `repository`, that web entry keeps HexIDE's name and opens HexIDE's repository. |
| `defaultProfile` | No | The profile used when the command line names none: a profile's name, or a folder inside the installation written with `/`, such as `branding/profile`. A profile sets your theme, keymap and defaults, and never your identity. `--profile`, including `--profile none`, still wins. [Your default profile](#your-default-profile) says what the folder holds. |

A member that isn't in this table stops the IDE from starting. So does anything else wrong with the file: the IDE
reports the file and the problem, and doesn't fall back to being HexIDE, because that would open HexIDE's settings
and recovery files inside your product. `--help` still answers on a broken installation, and exits with code 2.
All startup refusals a person reads use the operating system’s language before settings load (#4065), including
the descriptor’s detailed reasons, command-line flag errors and native dialog caption. Field names, paths, sizes
and limits stay unchanged. With `--help`, a broken installation prints the localized refusal before the help
text. Descriptor exceptions keep their English messages for programs and logs.

Logo capture checks every PNG chunk's length and CRC, the header's format, and the final IEND. Its IDAT
stream must inflate to exactly the scanline bytes required by the dimensions, including Adam7 passes.
Validation runs before the UI toolkit starts, within the existing 1 MiB file and 1024×1024 pixel limits;
a corrupt logo refuses startup with both the descriptor and logo file named (#4063).

A running program belongs to its author, not to the IDE's brand. In every distribution, a form with
no `Icon` line uses the runner's neutral `form.ico` at F5, with or without a brand logo, just as in its
made EXE (#4187). This is HexIDE's choice: the default is big only; a form's own icon and `Icon = (None)`
keep their existing behaviour. The IDE keeps its own or the brand's icon. The measured VB6 behaviour is in
[the form-icon oracle](vb6-fidelity-oracle/a-forms-window-icon-no-icon-line.md).

## Translating your tagline and credits

Each `tagline` or `credits` entry can be an ordinary string or an object with a required `text` fallback
and an optional `translations` map keyed by language id:

```json
{
  "tagline": {
    "text": "VB6 for widget makers",
    "translations": { "fr": "VB6 pour les fabricants de widgets" }
  },
  "credits": [
    { "text": "The widget team", "translations": { "fr": "L’équipe des widgets" } },
    "Our sponsors"
  ]
}
```

When About opens, each entry uses the selected region, then its neutral language (including the Chinese
script mapping used by language packs), then `en`, then that entry's `text`. Missing translations never
remove a credit or change its position. System language is resolved first; pseudo languages use English.
The same text limits apply to translations: 300 characters on one line for a tagline, 2000 characters per
credit with newlines allowed. Empty translations are refused. `--help` continues to use the plain `text`
fallback, since it runs before the user's language settings are loaded.

## Your default profile

A profile is a folder holding a `profile.json`. Put yours beside `brand.json`, for example as
`branding/profile/profile.json`, and name the folder in `defaultProfile`. This one offers two project types in New
Project, and starts without the New Project dialog:

```json
{
  "schemaVersion": 1,
  "id": "acme",
  "projectTypes": ["standard-exe", "activex-dll"],
  "settingsDefaults": "defaults.json"
}
```

with `defaults.json` in the same folder:

```json
{ "promptForProjectOnStartup": false, "tabWidth": 2 }
```

| Member | Required | Meaning |
|---|---|---|
| `schemaVersion` | Yes | Must be `1`. |
| `id` | Yes | Lower-case letters, digits and hyphens, starting with a letter, and not `none`. |
| `projectTypes` | No | What New Project and Add Project offer, in this order. Any of `standard-exe`, `activex-exe`, `activex-dll`, `activex-control`, `application-wizard`, `wizard-manager`, `data-project` and `enterprise-controls`. |
| `menuVisibility` | No | `project.references` and `tools.references`, each `true` or `false`: which menu offers References. |
| `toolbarVisibility` | No | `standard`, `edit`, `debug` and `form-editor`, each `true` or `false`. |
| `settingsDefaults` | No | A file of starting settings, described below. |
| `theme` | No | A theme file, in the format of the built-in packs in `IDE/HexIDE/Themes/Packs`. |
| `keymap` | No | A keymap file, in the format of the built-in packs in `IDE/HexIDE/Keymaps/Packs`. |
| `languageOverrides` | No | An object from a language id, such as `fr`, to a file of strings in the format of the language packs in `IDE/HexIDE/Localization/Packs`. It changes the wording of a language the IDE ships, and can't add one. |

Every file a profile names is a path inside its folder. A `settingsDefaults` file may set:
- the numbers `tabWidth`, `gridWidth` and `gridHeight`;
- the names `activeTheme`, `activeKeymap` and `activeLanguage`;
- `true` or `false` for `requireVariableDeclaration`, `autoListMembers`, `autoQuickInfo`, `autoIndent`,
  `formatOnSave`, `showGrid`, `alignToGrid`, `promptForProjectOnStartup`, `isStandardToolbarVisible`,
  `isEditToolbarVisible`, `isDebugToolbarVisible`, `isFormEditorToolbarVisible` and `isMinimapVisible`.

A profile sets where each user starts, and anything a user has chosen and saved wins over it. A member or setting
not listed here, or a file the profile can't read, stops the IDE from starting with exit code 2, and the refusal
names the profile and the file. The rules are in
[`openspec/specs/profiles/spec.md`](../openspec/specs/profiles/spec.md).

## What it doesn't do

- **HexIDE's credits stay.** You can add to them, and nothing removes, replaces or hides them. A translation
  override of them is ignored. Your About box also says what it's built on, with HexIDE's version and notice.
- **It grants no trust.** The add-in trust root and the first-party key don't change, and About keeps showing them.
  The lines that name them can't be relabeled either: About's provenance and signing-key labels, and the add-in trust
  inspector's root, trust-anchor and first-party rows. A profile, including your `defaultProfile`, and a user's
  translation override of them are ignored and logged, as for the credits (#4060). Nor can your profile change
  what tells a person whether to trust an add-in: the consent dialog's verdicts, warnings and buttons, the
  revocation and signing lines, and the add-in page's trust and consent status (#4121). Only a user's own
  translation of those applies. A translation file copied into the user's own translations folder counts as the
  user's; a pack you distribute goes through the package tier, which refuses a third party's text for these (#4181).
- **Engine statements keep HexIDE's name.** Messages about what the interpreter or serializer implements,
  including unsupported components and faithful-save refusals, describe the engine in every distribution (#4061).
- **Compatibility names don't change.** Assembly names, protocol and tool ids, `HEXIDE_*` environment variables,
  and what's written into projects stay as they are, so existing clients and projects keep working.
- **`--user-data-dir` still wins** over the folder `id` chooses. Pointing two products at one directory opts out of
  keeping them apart.

## Making the Windows build your own

`brand.json` changes what the running IDE says. The executable's own name, icon and version details, and the
installer's identity, are in the files Windows reads, so a tool rewrites those from the same `brand.json`. Run it on
Windows, from a clone of the tag you're repacking:

```sh
cd IDE
dotnet run --project HexIDE.Repack -- --input <unpacked release> --brand <path to brand.json> --output <new directory>
```

`--input` is a published x64 build, such as an unpacked `hexide-windows-amd64-selfcontained.zip`. Use the
self-contained build if you'll ship the setup program. The framework-dependent `hexide-windows-amd64.zip` repacks
too, but the setup program checks for no .NET runtime, so on a machine without .NET 10 your product won't start. `--output` must not
exist. The result is built beside it and moved into place only when it's complete, and the input is never changed.

| In the output | Comes from |
|---|---|
| `app\<id>.exe` | Stock's `HexIDE.exe`, renamed from your `id`. |
| Its icon | Your `logo`, which is required here, at 16, 32, 48 and 256 pixels. Nothing of stock's icon stays. |
| Its product and company names and file description | Your `name`. |
| Its copyright | Your `copyright`, or stock's if you have none. |
| Its version numbers | The stock build's, because that's the code it holds. |
| `app\brand.json`, your logo and a `defaultProfile` folder | Copied from beside your `brand.json`. |
| The setup program's icon and publisher | Your `logo`, at the same four sizes, and your `name`. |
| The setup program's copyright and home/support links | Your `copyright`, `links.home` and `links.support`, when supplied. |
| `installer\brand.iss` | Compile it with [Inno Setup](https://jrsoftware.org/isinfo.php)'s `ISCC` to make your setup program. |

The installer's AppId is a GUID derived from your `id`, so an upgrade finds the installed product and it installs
alongside HexIDE. Windows groups your windows and shortcut under `HexIDE.Distribution.<id>`. Both come from `id`, which
is another reason not to change it.

The run stops if your name can't be a Windows folder name, or if the build is missing `LICENSE` or
`THIRD-PARTY-NOTICES.md`. Those two are copied unchanged, as is every other file. That includes the helper programs
such as `HexIDE.VbLspServer.exe`, which keep their names.

**The output is not signed.** If the stock executable had a signature, rewriting the file invalidated it, so the tool
removes it and says so. Sign the executable and your setup program yourself. The tool never touches a key or a trust
anchor, and there's no update service: your support link is yours to use for that.

## Including your own files

Add `--include <folder>` to any platform's repack command. Its contents go into the program directory,
keeping relative paths: `app/` on Windows and Linux, or `<name>.app/Contents/MacOS/` on macOS.
The Windows installer's recursive payload, the Linux package's `app/` tree and the macOS bundle
therefore carry them. The report gives the source folder and file count.

The option adds files only. It refuses collisions with stock files (including the old executable name),
the renamed executable, descriptor, logo or copied profile. Put your notices beside HexIDE's notices.
The output must be outside the included folder.
Links at or within the included folder are refused; a link above it is allowed. A refusal names the
path and leaves no output or unfinished staging directory.

It grants no trust: included add-ins still need verification and consent. At every depth, names are
checked without regard to case against `hexide-root.pub`, `hexide-firstparty.pub`, `revocations.json`,
`revocations.sig`, `revocations-cache.json`, `revocations-cache.sig` and `revocations-cache.signed`. These are refused even when
absent from stock. This list follows `TrustAnchors.cs` (embedded anchors) and `RevocationStore.cs`
(embedded revocation floor and user-data cache); it does not prohibit a package's publisher certificates.

## Making the Linux build your own

A Linux executable has no icon or version details inside it, so here the tool renames the executable and writes the
files that describe your product to the desktop. Run it on Linux, from a clone of the tag you're repacking, because
only there does a copy keep the execute permission the programs need:

```sh
cd IDE
dotnet run --project HexIDE.Repack -- --input <unpacked release> --brand <path to brand.json> --output <new directory>
```

`--input` is a published build for any Linux processor, unpacked with `tar`. The tool picks Windows or Linux from what
the build holds. `--output` must not exist, and the input is never changed.

| In the output | Comes from |
|---|---|
| `app/<id>` | Stock's `HexIDE`, renamed from your `id`. Its bytes don't change. |
| `app/brand.json`, your logo and a `defaultProfile` folder | Copied from beside your `brand.json`. |
| `share/applications/<id>.desktop` | Your `name` and `id`. |
| `share/icons/hicolor/<size>/apps/<id>.png` | Your `logo`, which is required here, at 16, 32, 48 and 256 pixels. |

The `share` folder is laid out as it is under `/usr/share` or `~/.local/share`, so installing it means copying it
there. The menu entry starts `<id>` from `PATH`. If your package doesn't put a link to the executable on `PATH`, change
the entry's `Exec` line to the full path. For one user that could be:

```sh
mkdir -p ~/.local/opt ~/.local/bin
cp -R app ~/.local/opt/<id>
ln -s ~/.local/opt/<id>/<id> ~/.local/bin/<id>
cp -R share/. ~/.local/share/
```

Don't install the program itself in `~/.local/share/<id>`, because your product keeps each user's logs there. It keeps
their settings in `~/.config/<id>` and recovery files in `~/.local/state/<id>`. Those three folders are the user's, so
removing the program leaves them alone. Removing the product means deleting four things by name: the program's folder,
the link, the `.desktop` file and the icons.

A branded build sets its X11 window class to your `id`, and the menu entry's `StartupWMClass` names the same value, so
a desktop groups the running window with its menu entry. It comes from `id`, which is one more reason not to change it.

The run stops if the build is missing `LICENSE` or `THIRD-PARTY-NOTICES.md`, or if unpacking lost its execute
permissions. Every other file is copied unchanged, including the helper programs such as `HexIDE.VbLspServer`, which
keep their names.

**The output is not signed.** A Linux executable has no signature for the tool to remove. Sign your archive or package
yourself. As on Windows, the tool never touches a key or a trust anchor, and there's no update service.

## Making the macOS build your own

On macOS a product's identity is its bundle, so the tool renames the bundle and the executable and rewrites the bundle's
`Info.plist` and icon. Run it on macOS, from a clone of the tag you're repacking, because it checks the result with
macOS's own tools:

```sh
cd IDE
dotnet run --project HexIDE.Repack -- --input <path to HexIDE.app> --brand <path to brand.json> --output <new directory>
```

`--input` is `HexIDE.app` from a release archive for either processor, unpacked with `tar`. `--output` must not exist,
and the input is never changed.

| In the output | Comes from |
|---|---|
| `<name>.app` | Your `name`. |
| `Contents/MacOS/<id>` | Stock's `HexIDE`, renamed from your `id`, then ad-hoc signed with the bundle. |
| `Contents/Info.plist` | Stock's, with the bundle's name, display name, identifier, executable and icon changed. The version numbers stay the build's. |
| `Contents/Resources/<id>.icns` | Your `logo`, which is required here, at every size from 16 to 1024 pixels. Nothing of stock's icon stays. |
| `Contents/MacOS/brand.json`, your logo and a `defaultProfile` folder | Copied from beside your `brand.json`. |

The bundle identifier is `io.hexide.distribution.<id>`, so it installs alongside HexIDE and can't be stock's. It comes
from `id`, so don't change that once you've shipped. To install the product, move the `.app` to `Applications`, and to
remove it, delete the `.app`. Each user's settings and logs stay where macOS keeps them for the `id`.

The run stops if your name can't be a bundle's name, or if the build is missing `LICENSE` or `THIRD-PARTY-NOTICES.md`.
Every other file is copied unchanged, including the helper programs such as `HexIDE.VbLspServer`, which keep their
names. It also stops if macOS doesn't accept the rewritten `Info.plist`, the ad-hoc re-sealing, or strict deep bundle verification.

**The output has no signing identity.** The rewritten bundle is re-sealed ad hoc on macOS, which identifies nobody.
Signing with your own Developer ID and notarizing are yours to do, and [the macOS signing notes](macos-signing.md) describe what that takes. The tool never touches a key
or a trust anchor, and there's no update service.
