## ADDED Requirements

### Requirement: The client SHALL declare the reply shapes it reads
Where the protocol lets a client's declaration decide the shape of a reply, the client SHALL declare every
shape it reads, and SHALL NOT declare a shape it cannot read.

A server that honours the declaration sends the most basic shape to a client that did not make it, and no error
says so. Servers that send the richer shape regardless hide the omission (hexide-io/HexIDE#463).

#### Scenario: Symbols from a server that sends the tree only when told
- **WHEN** a server that honours the declaration is asked for the symbols of a document that nests them
- **THEN** it answers with the tree, and the symbols arrive nested as the document declares them
