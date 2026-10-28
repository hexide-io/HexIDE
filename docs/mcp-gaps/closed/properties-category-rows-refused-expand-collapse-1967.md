# Properties category rows refused expand/collapse (#1967 — fixed)

**Symptom.** With Categorized Properties showing, `interact {"target":"…/List[CategorizedProperties]/Pane[PART_ScrollViewer]/ListItem[Appearance]","action":"collapse","window":"ide"}` refused with `element does not support 'collapse'`.

**Workaround.** `interact {"target":"…/ListItem[Appearance]/Button","action":"toggle","window":"ide"}` operated the unnamed toggle beneath the row.

**Fix.** Category rows now expose an expand/collapse provider tied to `IsExpanded`; their toggles use the localized category header as their accessible name. Inspection reports the provider's `expandCollapseState`.
