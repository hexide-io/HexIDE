## 1. Resources through the add-in host

- [x] 1.1 `IHexIdeHost.RegisterResources`, its public API line, and the check-and-merge behind it, deny by default under `RegistrationGrants`.
- [x] 1.2 The IDE's own host refuses it; an add-in's owned host passes its origin and calling assembly.
- [x] 1.3 `BasicProvider` registers its three dictionaries; `HexIDE.Desktop` composes it at startup; `App.axaml` no longer merges them.
- [x] 1.4 Tests: each refusal with the application's resources read afterwards, the grants against the trust and consent views' keys, and
  the headless harness registering the provider the same way.
