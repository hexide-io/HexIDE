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
| `links` | No | `home`, `support` and `repository`, each an absolute `https` URL. |
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

## Not done yet

The executable's native icon and version details, the installer's identity, localized credits and some diagnostics
written in English are still HexIDE's. See the issues linked from #2042.
