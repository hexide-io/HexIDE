# The foreign debug adapters the tests drive

HexIDE's real `DapSession`, `DapProcess` and `DapConnection` drive debug adapters written by other
projects. A client agreeing with its usual adapter is insufficient evidence of protocol interoperability;
these tests apply the same principle as the [foreign language servers](foreign-language-servers.md).
These sessions are the first part of [#3302](https://github.com/hexide-io/HexIDE/issues/3302); the generated
[DAP coverage table](dap-client.md#protocol-coverage) is also available.

## Adapters and licences

| Adapter | Pin | Protocol implementation | Licence at the pin |
|---|---|---|---|
| debugpy | 1.8.22 | Python, debugpy's own `debugpy.common.messaging` layer, with PyDev.Debugger for execution | [MIT](https://github.com/microsoft/debugpy/blob/v1.8.22/LICENSE); its [third-party notices](https://github.com/microsoft/debugpy/blob/v1.8.22/src/debugpy/ThirdPartyNotices.txt) include EPL-1.0 PyDev.Debugger, Python licences, Apache-2.0, MIT and BSD notices. |
| netcoredbg | 3.2.0-1092 | C++, Samsung's own VS Code protocol implementation | [MIT](https://github.com/Samsung/netcoredbg/blob/3.2.0-1092/LICENSE). Its release also carries the managed debugging support libraries. |

The linked licence files were read at these exact tags for this change, including debugpy's bundled
notices. netcoredbg's pinned third-party licence files were also read:
[libelfin (MIT)](https://github.com/Samsung/netcoredbg/blob/3.2.0-1092/third_party/libelfin/LICENSE),
[nlohmann/json (MIT)](https://github.com/Samsung/netcoredbg/blob/3.2.0-1092/third_party/json/LICENSE.MIT),
[linenoise-ng (BSD and permissive Unicode/wcwidth notices)](https://github.com/Samsung/netcoredbg/blob/3.2.0-1092/third_party/linenoise-ng/LICENSE),
and [Catch2 (Boost Software License)](https://github.com/Samsung/netcoredbg/blob/3.2.0-1092/third_party/catch2/LICENSE.txt). They are independent protocol implementations, not two wrappers around one framework. Both
have Linux support. Neither is linked into HexIDE, copied into its source, or included in releases.
They run unmodified as separate processes across stdio and are downloaded only into the gitignored
`artifacts/foreign-dap/` cache. The same non-distribution and separate-process reasoning recorded for
texlab applies to debugpy's EPL component. `scripts/check-licences.sh` refuses tracked files anywhere
under this cache, including wheels and extracted Python source.

## Acquisition and runtime dependencies

`ForeignAdapterAcquisition.cs` pins the URLs, entry points and SHA-256 digests. Both sets of digests
are **registry-attested at upload**, not computed-here provenance:

- debugpy's portable `py2.py3-none-any.whl`: PyPI computes the SHA-256 when the publisher uploads the wheel and records it in [PyPI's version metadata](https://pypi.org/pypi/debugpy/1.8.22/json).
- netcoredbg's release archives: GitHub computes the SHA-256 when the publisher uploads each asset and records it in the `digest` fields in the [official release API](https://api.github.com/repos/Samsung/netcoredbg/releases/tags/3.2.0-1092).

Adapter and DAP schema downloads make up to three attempts for HTTP 5xx responses, timeouts or dropped
connections, with 250 ms and 500 ms backoffs (#3489). Other HTTP errors and checksum mismatches do not
retry; exhausting the attempts still leaves the adapter unavailable.

Downloads use a partial file and are verified before promotion to the cache. Cached archives are
verified again before each test process extracts them, so an interrupted extraction is never trusted.
A mismatch refuses execution and fails an ordinary test even when adapters are optional. Only verified
archives are extracted; fetched code never lives beside tracked source.

The portable debugpy wheel needs Python 3 on PATH. CI explicitly installs Python **3.14** in both IDE
jobs; Python is a harness dependency, not a HexIDE dependency. No pip install, global package change or
new NuGet package is needed. netcoredbg debugs a small C# program built with the already required .NET
10 SDK and portable PDBs. Linux x64/Arm64, Windows x64
and macOS Arm64 archives are pinned. netcoredbg publishes no Windows Arm64 build, and its x64 build crashes with an
access violation launching an Arm program, so that test skips there and says why (#3548). Windows and macOS execution must be checked by their CI legs;
inspecting an archive's layout on Linux is not execution coverage there.

| Variable | Effect |
|---|---|
| `HEXIDE_FOREIGN_LSP_DOWNLOAD=0` (also `false` or `off`) | Use only cached archives; missing adapters skip visibly with a reason. |
| `HEXIDE_REQUIRE_FOREIGN_LSP=1` (also `true` or `yes`) | Ordinary availability tests fail when either adapter or its runtime is unavailable. CI already sets this; discovery cannot swallow the enforcement. |

These fixtures always use the pinned adapters, never an unverified PATH copy of an adapter. Python is
located on PATH and checked for a Python 3 version. Successful resolutions and failures are memoized per
test process. Bump a pin by reading the new official licence and notices, copying the registries' new
digests, checking the archive layouts and rerunning both complete sessions.

## What the sessions assert

`ForeignAdapterTests` launches a tiny real program in each adapter's language. The program initializes
`answer` to 41, stops before incrementing it, then prints 42 and exits. Tests check the verified breakpoint,
its stopped event and thread, stack source and line, and the local's name and value. They continue to
termination, check stdout and the debuggee's exit code, disconnect, and check the adapter's exit code 0,
no forced termination and no cleanup failure.

The armed conversation log also checks request order, initialize's coordinate/path conventions,
breakpoint source/line and disconnect's `terminateDebuggee:true`. Every outgoing request must have a
successful incoming response with matching `request_seq` and command. Incoming initialized, stopped,
output, exited and terminated frames are inspected; a returned call alone is never the assertion.
The tests consume the session update queue and put a deadline around the whole exchange.

## Linux findings (2026-11-07)

Both complete sessions passed against the existing client; no product-code accommodation was required.
The fixture uses debugpy's `console:"internalConsole"` to keep execution on the owned stdio session
rather than asking the client to open a terminal. netcoredbg uses `--interpreter=vscode` and a portable
PDB. These are explicit adapter launch settings, not protocol repairs.

The first test assumption did fail: a breakpoint need not be verified in the initial response.
At the pinned versions, the relevant incoming frames were (other fields omitted):

```json
{"command":"setBreakpoints","success":true,"body":{"breakpoints":[{"verified":true,"id":0,"line":2}]}}
```

That is debugpy. netcoredbg instead returns a pending breakpoint and verifies the same ID after loading
the program, before the breakpoint stop:

```json
{"command":"setBreakpoints","success":true,"body":{"breakpoints":[{"id":1,"line":7,"message":"The breakpoint is pending and will be resolved when debugging starts.","verified":false}]}}
{"event":"breakpoint","body":{"reason":"changed","breakpoint":{"id":1,"line":7,"verified":true}}}
```

The tests accept initial verification or a later verification event for the same ID, and still require
the real breakpoint stop, exact source line and local value. They do not convert a pending breakpoint
into a successful result themselves.

For a negative control, an isolated copy of `DapSession.VariablesAsync` was changed to send
`variablesReference:0` instead of the reference returned by scopes. **Both session tests failed**:

```json
{"command":"variables","success":false,"message":"Unable to find thread to evaluate variable reference."}
{"command":"variables","success":false,"message":"Failed command 'variables' : 0x80004005"}
```

The first refusal is debugpy, the second netcoredbg. The normal client was unchanged and both normal
sessions passed. This is mutation evidence, not a claim that either test failed on the original client.
Separately, all three new licence-guard self-tests failed against the original guard (tracked wheel,
Python source and extensionless binary accepted) and passed with the cache guard extended.

With both caches temporarily absent, `HEXIDE_FOREIGN_LSP_DOWNLOAD=0` produced two visible session skips
and two passing optional availability checks. Adding `HEXIDE_REQUIRE_FOREIGN_LSP=1` made those two
availability checks fail; the session tests still skipped. That negative control proves discovery's
skips cannot hide missing adapters in CI. The skips themselves are not interoperability coverage.

A fresh-cache run with adapters required also passed both sessions and all six adapter tests. Replacing
the cached debugpy wheel with corrupt bytes made its ordinary availability check fail even without the
CI-required flag (one failure, one passing netcoredbg availability check); the original archive was
restored afterwards.
