# HexIDE.Imaging

Decodes picture bytes that HexIDE didn't write and can't trust: an add-in's publisher logo, and the pictures in a
form's `.frx` companion. Those arrive with any project or package someone downloads.

The IDE and the Basic provider both decode through this assembly, so the checks exist once (#3411). It stays in
HexIDE when the provider moves out, and the provider consumes it as it consumes `HexIDE.Core`.

- **`SafeImageDecoder`** decodes a small PNG with a byte cap and a dimension cap, read from the header before anything
  is allocated. It never throws.
- **`FrxImageHelper`** reads a `.frx` picture record's framing and decodes its payload.

## A fix here reaches a made program only when it's made again

File > Make writes a program as HexIDE's standalone runner, which is a single-file executable carrying this assembly.
So a program made before a fix keeps the decoder it was made with. When you fix a decoding defect here, especially a
security one:
- say so in the changelog entry, and tell people to make their programs again;
- don't describe the fix as reaching programs already made.
