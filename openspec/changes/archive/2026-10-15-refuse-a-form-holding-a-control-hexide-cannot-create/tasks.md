# Tasks

## 1. The refusal

- [x] 1.1 Refuse in `VBLoader.SpawnComponents`, before any control is placed, which every run path reaches: the startup
  form, a form the program loads, and a made program's forms.
- [x] 1.2 Name every control in each preserved block from its `Begin` lines, nested ones included, and a control
  array's name once. Skip `BeginProperty`.
- [x] 1.3 Leave the designer's path alone.

## 2. Tests

- [x] 2.1 A startup form holding one doesn't start: the failure names the control, and no code runs.
- [x] 2.2 Every control a block holds is named, including a modelled one nested inside it, and a font block isn't.
- [x] 2.3 A control array is named once.
- [x] 2.4 Loading another form that holds one ends the run, and `On Error Resume Next` doesn't trap it.
- [x] 2.5 The designer still draws the form.

## 3. Records

- [x] 3.1 MISSING_FEATURES, CHANGELOG, and the two narrowed requirements.
