### Fixed

- **Runtime Label AutoSize fits its caption** (part of #922). Changing Caption or Font, or enabling AutoSize,
  recomputes its size and keeps the edge named by Alignment. WordWrap keeps its width unless a word is wider.
  Windows measures through GDI in the loaded VB font, including its family, size and weight;
  other platforms approximate in the drawn font. Centered, word-wrapped labels retain their width when text
  fits, avoiding VB6's repeated odd-width growth. Saved dimensions and explicit Width/Height assignments hold
  until a trigger; designer AutoSize remains pending.
