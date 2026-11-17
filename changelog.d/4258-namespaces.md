### Security

- **An add-in's language-server prefixes are bound to its publisher's signed namespaces** (#4258 part D). When the
  verified `publisher.json` lists `namespaces` (for example `rdcore.`), a contributed server's message prefix must be
  `<namespace>/` and its command prefix `<namespace>.`, or registration is refused naming both. Records without the
  field keep the earlier rule, with one notice per add-in. `hexide/` and `hexide.` stay reserved, and the packer's
  `sign-publisher --namespaces` refuses them.
