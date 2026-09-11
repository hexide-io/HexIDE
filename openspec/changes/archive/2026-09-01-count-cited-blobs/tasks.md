# Tasks

## 1. Make the count companion-independent
- [x] 1.1 Derive the expected count from `FrxDeserializer.CitedOffsets(source)`, de-duplicated, so two
      properties citing one offset count once
- [x] 1.2 Repoint the dead subtree check at the component's own raw properties, recursing into subcomponents
- [x] 1.3 Record at the silent-skip site that the citation is counted by the gate, so the skip is safe

## 2. Cover it
- [x] 2.1 A modelled `Picture` whose companion is missing holds the form read-only
- [x] 2.2 An unmodelled owner whose companion is missing holds the form read-only
- [x] 2.3 An unmodelled property whose companion is missing holds the form read-only
- [x] 2.4 A truncated companion holds the form read-only
- [x] 2.5 A form whose citations are all honoured stays savable (the over-reach guard)
- [x] 2.6 Two properties citing one offset count as one blob
- [x] 2.7 Mutation-test both arms: restore the old count and confirm exactly the cases only a count can
      catch fail; remove the count and confirm the revived flag still carries its own case

## 3. Verify
- [x] 3.1 Runtime, IDE and Integration suites green
- [x] 3.2 Round-trip corpus unchanged at 21/22 VB6-authored forms
