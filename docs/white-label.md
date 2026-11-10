# Shipping HexIDE under your own name

HexIDE is MIT-licensed, and an adopter can ship it as another product without a fork. Put a `brand.json` beside
the executable, and that installation becomes your product: its own name, logo, credits and per-user state.

This is the first slice of [#2042](https://github.com/hexide-io/HexIDE/issues/2042). The design is in
[`design/white-label.md`](design/white-label.md). The file's schema is provisional until that design is accepted.

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
| `logo` | No | A PNG of up to 1024×1024 pixels, inside the installation folder, for About, New Project and the window icon. |
| `links` | No | `home`, `support` and `repository`, each an absolute `https` URL. Home and support appear in Help under your product's name when supplied; the home entry is labeled “Home Page”, distinct from the existing repository web entry. Without `repository`, that web entry keeps HexIDE's name and opens HexIDE's repository. |
| `defaultProfile` | No | The profile used when the command line names none: a profile's name, or a folder inside the installation written with `/`, such as `branding/profile`. A profile sets your theme, keymap and defaults, and never your identity. `--profile`, including `--profile none`, still wins. |

A member that isn't in this table stops the IDE from starting. So does anything else wrong with the file: the IDE
reports the file and the problem, and doesn't fall back to being HexIDE, because that would open HexIDE's settings
and recovery files inside your product. `--help` still answers on a broken installation, and exits with code 2.

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

## What it doesn't do

- **HexIDE's credits stay.** You can add to them, and nothing removes, replaces or hides them. A translation
  override of them is ignored. Your About box also says what it's built on, with HexIDE's version and notice.
- **It grants no trust.** The add-in trust root and the first-party key don't change, and About keeps showing them.
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

`--input` is a published x64 build, such as an unpacked `hexide-windows-amd64-selfcontained.zip`. `--output` must not
exist. The result is built beside it and moved into place only when it's complete, and the input is never changed.

| In the output | Comes from |
|---|---|
| `app\<id>.exe` | Stock's `HexIDE.exe`, renamed from your `id`. |
| Its icon | Your `logo`, which is required here, at 16, 32, 48 and 256 pixels. Nothing of stock's icon stays. |
| Its product and company names and file description | Your `name`. |
| Its copyright | Your `copyright`, or stock's if you have none. |
| Its version numbers | The stock build's, because that's the code it holds. |
| `app\brand.json`, your logo and a `defaultProfile` folder | Copied from beside your `brand.json`. |
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
| `Contents/MacOS/<id>` | Stock's `HexIDE`, renamed from your `id`. Its bytes don't change. |
| `Contents/Info.plist` | Stock's, with the bundle's name, display name, identifier, executable and icon changed. The version numbers stay the build's. |
| `Contents/Resources/<id>.icns` | Your `logo`, which is required here, at every size from 16 to 1024 pixels. Nothing of stock's icon stays. |
| `Contents/MacOS/brand.json`, your logo and a `defaultProfile` folder | Copied from beside your `brand.json`. |

The bundle identifier is `io.hexide.distribution.<id>`, so it installs alongside HexIDE and can't be stock's. It comes
from `id`, so don't change that once you've shipped. To install the product, move the `.app` to `Applications`, and to
remove it, delete the `.app`. Each user's settings and logs stay where macOS keeps them for the `id`.

The run stops if your name can't be a bundle's name, or if the build is missing `LICENSE` or `THIRD-PARTY-NOTICES.md`.
Every other file is copied unchanged, including the helper programs such as `HexIDE.VbLspServer`, which keep their
names. It also stops if macOS doesn't accept the executable's signature or the `Info.plist` afterwards.

**The output has no signing identity.** Its programs keep the ad-hoc signatures stock shipped with, which Apple Silicon
needs in order to run them at all and which identify nobody. Signing with your own Developer ID and notarizing are
yours to do, and [the macOS signing notes](macos-signing.md) describe what that takes. The tool never touches a key
or a trust anchor, and there's no update service.

## Not done yet

Some diagnostics written in English. See the issues linked from #2042.
