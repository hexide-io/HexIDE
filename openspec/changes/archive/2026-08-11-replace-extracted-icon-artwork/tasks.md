# Tasks

## 1. Draw the replacement set
- [x] 1.1 Draw an original vector geometry for every icon in use across the menus, the four toolbars, the tool windows, the Project Explorer tree, and the project-type lists.
- [x] 1.2 Declare each as a keyed geometry resource under a `Geo.` prefix, defined once and referenced by key.
- [x] 1.3 Keep the visual language consistent across the set — one stroke weight, one corner treatment, one optical size.

## 2. Theme the set
- [x] 2.1 Define a single themed ink brush with light and dark variants.
- [x] 2.2 Fill icons from that brush rather than giving each icon its own colour.
- [x] 2.3 Give run, break and stop their own semantic brushes, since their colour carries meaning.

## 3. Provide a C# entry point
- [x] 3.1 Add a factory exposing exactly two operations: a theme-tinted icon by key, and an icon by key in a caller-supplied brush.
- [x] 3.2 Route the C# call sites that previously loaded bitmaps through it.

## 4. Remove the extracted artwork
- [x] 4.1 Delete every extracted raster icon from the tree.
- [x] 4.2 Confirm no extracted artwork remains; the only rasters left are the application logo and a sample add-in logo, both original.
- [x] 4.3 Verify the result on the running IDE across theme switches, including that icons re-tint live.

## 5. Retire the superseded design
- [x] 5.1 Retire the `runtime-icon-loading` capability — an icon service and per-theme raster icon sets, neither of which was built and neither of which this approach needs.
