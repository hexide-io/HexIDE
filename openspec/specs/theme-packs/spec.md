# theme-packs Specification

## Purpose
Define how the IDE's colours are themed: declared as data rather than compiled in, swappable while the IDE
runs, and extensible without rebuilding.

Dark mode is the obvious motivation and the least interesting part of it. The reason to build a pack system
rather than a second colour file is that a theme is data, and data does not need a compiler — which means a
user can have a theme the project never shipped, and adding one is a contribution anybody can make.

This is one of three independent pack systems that follow the same shape and compose freely: this one swaps
colours, `keymap-packs` swaps keyboard gestures, and `language-packs` swaps text.

Disk discovery closes [#413](https://github.com/hexide-io/HexIDE/issues/413), which tracked the former
embedded-only loader.

## Requirements

### Requirement: Selectable high-contrast palettes
The IDE SHALL ship HighContrastLight and HighContrastDark packs in every profile, including Classic.
Each SHALL declare every top-level brush and colour key from Classic.axaml. The palette's window,
control, menu, caption, selection, information, warning, validation, read-only, inactive-code, link and bracket
text/background pairs SHALL have contrast of at least 7:1. Options SHALL display localized names while
retaining stable pack IDs as setting values.

These packs are part of [#3300](https://github.com/hexide-io/HexIDE/issues/3300). Avalonia does not
report the Linux portal contrast preference yet; HexIDE adds no workaround.

#### Scenario: Selecting a high-contrast pack
- **WHEN** a user selects either high-contrast pack in Options
- **THEN** its complete colour dictionary is applied using its declared Light or Dark variant
- **AND** the saved setting remains the pack ID rather than its translated name

### Requirement: Follow platform high contrast unless the user chose a theme
Before creating views, the IDE SHALL read Application.PlatformSettings and SHALL observe its
ColorValuesChanged event. When ContrastPreference is High, it SHALL apply HighContrastLight or
HighContrastDark according to the platform theme variant, in every profile. Clearing high contrast
SHALL restore the user's prior pack or profile theme. Automatic changes SHALL NOT write settings.
An explicit theme choice saved in Options SHALL take precedence, including after restarting.
While following automatically, the selector SHALL show the active high-contrast pack so Classic can
be explicitly selected; saving untouched Options SHALL NOT pin that automatic pack.
Ordinary OS dark mode SHALL leave Classic light. Avalonia does not yet read the Linux freedesktop
portal contrast preference; Linux automatic following is a known gap, with no HexIDE workaround.

#### Scenario: Starting with high contrast enabled
- **WHEN** the platform already requests high contrast at startup and no explicit theme is saved
- **THEN** the matching pack is applied before the first frame

#### Scenario: Changing platform contrast
- **WHEN** platform contrast turns on, changes variant, then clears
- **THEN** the matching high-contrast packs are applied and the prior theme is restored
- **AND** the user's saved theme is never overwritten

#### Scenario: Saving a theme in Options
- **WHEN** the user saves an explicit theme choice
- **THEN** subsequent platform contrast changes and restarts preserve that choice
- **AND** saving Options without changing the theme does not disable automatic following

### Requirement: High-contrast syntax defaults
Under HighContrastLight and HighContrastDark, default named syntax foregrounds SHALL meet 7:1 against
the pack's Window background, for VB6 editors, Formatting previews and adopted language definitions.
Adaptation SHALL preserve hue and use opaque colours, adjusting lightness to reach the threshold.
Leaving the pack SHALL restore the ordinary
palette. Explicit per-theme editor colour overrides SHALL continue to take precedence.

#### Scenario: Switching to a high-contrast pack with code open
- **WHEN** either high-contrast pack is selected
- **THEN** default syntax colours and Formatting previews meet 7:1 against the editor background
- **AND** switching back to Classic restores the original light syntax colours

### Requirement: High-contrast chrome beyond the Classic palette
Line-number text, input placeholders and selected Properties tab text SHALL also reach 7:1; scrollbar
thumbs SHALL reach 3:1 against their tracks. Dock panel and tab-strip backgrounds SHALL follow the pack.
Active document tabs and selected Properties tabs SHALL have a heavy border. Both selection indicators
SHALL contrast at least 3:1 with the unselected state, without relying on hue alone. Packs MAY set `tabSelectionBorderThickness` (a non-negative number, default zero).
Packs MAY override the SimpleTheme and Dock brush resources exposed by the colour mapping and set
`placeholderOpacity` (zero through one); omitted overrides SHALL retain the base theme's defaults.

#### Scenario: Chrome beyond the Classic palette
- **WHEN** either high-contrast pack is applied
- **THEN** line numbers, placeholders and selected tabs use the pack's high-contrast text
- **AND** scrollbar thumbs contrast with their tracks and Dock backgrounds follow the pack
- **AND** document and Properties tabs show selection with a contrasting heavy border

### Requirement: High-contrast focus and selection borders
HighContrastLight and HighContrastDark SHALL set `chromeBorderThickness` to 2. Focused controls and
selected list, tree, tab and menu items, including document and tool Dock tab headers, SHALL show a
border with at least 3:1 contrast against the adjacent chrome. The shared styles SHALL use theme
resources rather than pack names or literal colours. Other packs SHALL retain their existing borders
when this non-negative thickness is omitted or zero. The application SHALL load these shared styles
only while the active pack requests a positive thickness, removing them when it no longer does.
While active, selection border space SHALL be reserved so selection does not change layout.
Inputs and buttons SHALL retain their unfocused outlines and show keyboard focus with a 2px
rectangle in `ThemeForegroundBrush` outside the control, without changing its border or layout.
The focus rectangle SHALL remain visible outside the adorned control’s clip.
Removing the styles SHALL restore existing controls’ base geometry, border thickness and padding,
including template parts and popup content when focus or selection is activated again.

#### Scenario: Moving focus under a high-contrast pack
- **WHEN** focus moves between buttons and inputs under either high-contrast pack
- **THEN** the newly focused control shows a contrasting 2px border
- **AND** selected items retain their selection border independently of focus
- **AND** focus and selection changes leave control and content bounds unchanged
- **AND** switching to Classic restores existing menu and tool-header geometry, including when a menu is reopened
- **AND** repeated switches load exactly one shared border style include for a requesting pack and none for other packs

### Requirement: Dialog layouts leave room for outside focus rings
Every control that draws the high-contrast packs' outside focus ring, on an Options page or in one of the IDE's standard
dialogs, SHALL leave at least 4 DIP between itself and each neighbour, and at least 2 DIP inside its window, in every
theme. Items, tabs and tree rows draw focus inside their own border and need no gap. The Properties pane SHALL inset its content at least 4 DIP from its edges, with the object selector and
the filter below it aligned and 4 DIP apart, in normal and maximized windows.

#### Scenario: Focusing controls in tight layouts
- **WHEN** either high-contrast pack is active and focus moves through these controls
- **THEN** each outside 2 DIP ring fits without covering its neighbour or being clipped at the window edge
- **AND** the layout gaps remain when switching to another theme

### Requirement: Editor links SHALL follow the theme
Editors SHALL use the `LinkText` theme colour (`LinkTextBrush`) for web and mail links.
Every shipped pack SHALL declare it with at least 4.5:1 contrast against its own `Window` background.
An open editor SHALL update links immediately when that resource changes, including between dark packs.

#### Scenario: Switching between dark packs with a link visible
- **WHEN** Dark is changed to Abyss while an editable comment contains a URL
- **THEN** the link uses Abyss's link colour without reopening the editor


### Requirement: IDE dialog buttons expose keyboard focus and fixed defaults
IDE message boxes, server prompts and all dialogs hosted by the IDE window manager SHALL show a dotted inset
focus rectangle for keyboard focus, using the active theme's control-text brush. Mouse focus alone
SHALL NOT show it. Buttons with a fixed default action SHALL show a heavier border. These styles
SHALL NOT change VB6 controls on running forms or program message boxes.

#### Scenario: Opening a server prompt
- **WHEN** a server prompt opens with actions
- **THEN** its first action receives keyboard focus and shows the focus cue

#### Scenario: Navigating a server prompt
- **WHEN** keyboard navigation focuses an action button
- **THEN** its rendering differs from an unfocused button in Classic, Dark and Abyss

#### Scenario: Showing an IDE message box
- **WHEN** an IDE message box opens
- **THEN** its first action is the default and carries a heavier border

#### Scenario: Recorded dialog defaults
- **WHEN** the IDE opens Save Changes
- **THEN** Yes is the declared default with the heavier border
- **AND** Find/Replace retains Find Next, Add Watch/Components/Options retain OK, Recovery retains Restore,
  and Runtime Error retains Debug
- **AND** other unmeasured VB6 default choices retain their existing assignments

#### Scenario: Opening a shared desktop dialog
- **WHEN** the IDE opens a dialog through its shared window manager
- **THEN** its buttons use the same theme cues without a view-type allowlist
- **AND** if no control already has focus, its enabled visible default button receives keyboard focus,
  or its first text input / available tab stop if it has no default
- **AND** an explicit input target (Find What in Find/Replace, Expression in Add Watch) takes priority
- **AND** an existing explicit focus choice is preserved with keyboard cues
- **AND** program message boxes and input boxes retain their existing chrome and focus behaviour

### Requirement: IDE dialogs cannot be minimized or add taskbar buttons
IDE dialogs hosted by `DialogWindow`, IDE message/input boxes and language-server prompts SHALL set
`CanMinimize` and `ShowInTaskbar` to false, as recorded in [#3341](https://github.com/hexide-io/HexIDE/issues/3341).
Program MsgBox and InputBox windows SHALL retain their existing behaviour.

#### Scenario: Opening an IDE dialog
- **WHEN** any of these IDE dialog windows opens
- **THEN** it cannot be minimized and does not add its own taskbar button

### Requirement: A theme SHALL be declared as data, not compiled in
A theme SHALL be a data file declaring colour values by key, and adding one SHALL require no code change and
no rebuild.

A theme expressed as compiled markup can only come from whoever builds the product, which rules out the
people most likely to want one. Keeping the compiled layer to the *definition* of the keys and their default
values, and expressing every theme as overrides on top, means the extensible part has no build step at all.

#### Scenario: Adding a theme
- **WHEN** a new theme is contributed
- **THEN** it is a data file listing colour overrides, with no code change

#### Scenario: Installing a user theme
- **WHEN** a JSON pack is placed in the active user-data directory's `themes` subdirectory before launch
- **THEN** its filename without `.json` appears in the theme selector alongside the shipped themes
- **AND** selecting it loads that file without a rebuild

#### Scenario: A theme that overrides some colours
- **WHEN** a theme declares only some of the colour keys
- **THEN** the remainder keep their default values

### Requirement: Theme discovery SHALL preserve shipped defaults
The shipped theme IDs SHALL be declared in `Themes/manifest.json`. At service creation, the IDE SHALL
append top-level `*.json` files from `UserDataPath.For("themes")`, sorted by filename using ordinal order.
Filename stems SHALL identify disk themes; shipped IDs SHALL take precedence over case-insensitive
collisions. An absent or unreadable directory SHALL leave the shipped themes available.
Recovery mode (`--profile none`) SHALL skip disk discovery and retain only shipped themes.
Each selection SHALL reread the selected file, so edits can be previewed by switching away and back. A failed load
SHALL remove the previous override and revert to Classic. Unknown colour keys SHALL be logged and skipped.

#### Scenario: Starting in recovery mode
- **WHEN** the IDE starts with `--profile none` and user theme files exist
- **THEN** those files are neither listed nor loaded, and shipped themes remain available

#### Scenario: A disk pack collides with a shipped theme
- **WHEN** a disk filename has the same ID as a shipped theme, ignoring case
- **THEN** the selector lists that ID once and applies the shipped theme

#### Scenario: Editing a selected pack
- **WHEN** the user edits a discovered pack and reselects it
- **THEN** the new overrides apply, or Classic is restored if the file cannot be loaded

### Requirement: Changing theme SHALL take effect immediately
Selecting a theme SHALL restyle the IDE without a restart, including windows already open.

Choosing a theme is an aesthetic judgement made by looking at it, so a restart between choosing and seeing
turns a two-second decision into a chore and effectively stops people trying the alternatives. Applying live
also means a theme in development can be iterated on without relaunching.

#### Scenario: Switching theme
- **WHEN** the user selects a different theme
- **THEN** the IDE restyles immediately, including windows already open

### Requirement: A theme SHALL declare whether it is light or dark
A theme SHALL state which variant it is, and the IDE SHALL adopt that variant rather than following the
operating system.

The controls the IDE is built from pick their own defaults from the variant, so it has to be set
deliberately and it has to agree with the theme's own colours. Following the operating system instead
produces the specific failure this rule prevents: an explicitly light theme rendering with dark control
defaults on a machine set to dark, giving unreadable chrome that no single colour key can fix.

#### Scenario: Applying a light theme on a dark system
- **WHEN** a theme declaring itself light is applied on a system set to dark
- **THEN** the IDE renders light throughout, ignoring the system setting

### Requirement: The form design surface SHALL NOT follow the IDE theme
The design canvas SHALL remain light regardless of the active theme.

The canvas shows the form as it will look when it runs, and a VB6 form's colours are its own — they do not
change because the developer prefers a dark IDE. Theming the canvas would mean the developer designs against
one appearance and ships another, which makes the designer misleading in exactly the way a designer must
not be.

#### Scenario: Designing a form under a dark theme
- **WHEN** a dark theme is active and a form is open on the design surface
- **THEN** the canvas and the controls on it render with the form's own colours

### Requirement: Chrome colours SHALL come from the theme, never from literals
Interface colour SHALL be taken from a named theme key, and SHALL NOT be written as a literal value at the
point of use.

A literal colour is one a theme cannot reach. It survives every theme switch unchanged, so it looks correct
in whichever theme it was written against and wrong in all the others — and it fails quietly, appearing as
one panel that did not change rather than as an error.

#### Scenario: Adding interface chrome
- **WHEN** new chrome is added
- **THEN** its colours reference theme keys

#### Scenario: Form Layout thumbnail follows the theme
- **WHEN** the theme changes while Form Layout is open
- **THEN** the thumbnail face uses `WindowBrushKey`, its label uses `WindowTextBrushKey`, and its title strip uses `ControlDarkBrushKey`

#### Scenario: A colour that has no key
- **WHEN** chrome needs a colour the theme does not define
- **THEN** a key is added to the definition layer and every theme inherits a default for it

#### Scenario: A form designer without an MDI ancestor
- **WHEN** a form designer is opened as a Dock document
- **THEN** its caption bar uses the `ActiveTitleBarBrush` resource resolved at the bar, without an MDI ancestor binding

### Requirement: Shipped dark packs SHALL declare all mapped colours
Dark and Abyss SHALL carry every colour key supported by the theme-pack mapping. Their warning banners
SHALL use dark backgrounds, with foreground contrast of at least 4.5:1 and border contrast of at least 3:1
against both the banner and Window backgrounds. Validation error text SHALL have at least 4.5:1 contrast
against the pack's Window background.

#### Scenario: Warning and validation surfaces in a dark pack
- **WHEN** Dark or Abyss is selected
- **THEN** warning banners and validation text use that pack's explicit colours rather than Classic's defaults

### Requirement: Hidden controls SHALL be dimmed in the designer
The designer SHALL dim a modelled control whose own Visible property or a containing control's Visible
property is False using the theme's `DesignerHiddenControlMask` colour (the `DesignerHiddenControlMaskBrush` resource). Only the preview SHALL
be dimmed; the control SHALL remain selectable and editable. This is a HexIDE addition.

#### Scenario: Changing visibility while designing
- **WHEN** a control's Visible property becomes False
- **THEN** its preview uses the theme's dimming mask immediately
- **AND** its selection and editing remain available
- **WHEN** Visible becomes True and all containing controls are visible
- **THEN** the preview has no dimming mask

#### Scenario: A hidden container
- **WHEN** a Frame containing a CommandButton has Visible=False, on load or after editing
- **THEN** both previews use the theme's dimming mask
- **AND** the button remains selectable, movable and resizable
- **WHEN** the Frame becomes visible
- **THEN** the button's preview is dimmed only if its own Visible property is False

#### Scenario: Changing the dimming colour
- **WHEN** the theme's DesignerHiddenControlMask colour changes
- **THEN** hidden controls already on the designer use the new mask
