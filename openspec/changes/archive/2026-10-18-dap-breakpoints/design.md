# External breakpoint ownership

The launch file seeds source paths and requested one-based lines. Duplicate source entries use the last set,
matching sequential DAP replacement semantics. The external host owns those sets independently of interpreter
marks. Ending the session drops them; another launch reads the file again. Unsaved/non-file sources are refused.

Edits are enabled after launch configuration completes. One request per source can be in flight; edits advance
a source version and coalesce into the newest full replacement set. An older reply cannot publish verification
for newer intent. Requests can run while the program is running or stopped and do not expire with a stop.
Session identity prevents old replies changing a later session. Empty replacements clear a source.

Requested and resolved lines remain distinct. Markers stay at requested lines: solid circles are verified,
diamonds are relocated, hollow circles are pending, and crosses are unverified. Hover text reports both line
numbers, verification, and the adapter message. This keeps F9/remove semantics tied to the requested line.
Breakpoint events update matching adapter IDs; unknown IDs and pending replacements are not adopted.

Removed markers stay pending until acknowledged. On failure, the uncertain union of previous and desired lines
stays visible as unverified and can be removed again. A refusal or timeout cannot establish the backend's set;
the failure is also reported in Immediate. There is no automatic retry after a failed latest edit.
