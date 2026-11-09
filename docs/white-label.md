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

Don't install the program itself in `~/.local/share/<id>`, because that's where your product keeps each user's
settings and logs, and removing the program shouldn't remove them. Removing the product means deleting those four
things by name: the program's folder, the link, the `.desktop` file and the icons.

A branded build sets its X11 window class to your `id`, and the menu entry's `StartupWMClass` names the same value, so
a desktop groups the running window with its menu entry. It comes from `id`, which is one more reason not to change it.

The run stops if the build is missing `LICENSE` or `THIRD-PARTY-NOTICES.md`, or if unpacking lost its execute
permissions. Every other file is copied unchanged, including the helper programs such as `HexIDE.VbLspServer`, which
keep their names.

**The output is not signed.** A Linux executable has no signature for the tool to remove. Sign your archive or package
yourself. As on Windows, the tool never touches a key or a trust anchor, and there's no update service.

## Not done yet

Repacking for macOS, localized credits and some diagnostics written in English. See the issues linked from
#2042.
