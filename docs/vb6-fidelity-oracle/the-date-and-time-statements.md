## The `Date` and `Time` statements (2026-11-18, #4681, SP6)

**The rig:**
- **Build:** vb6.exe 6.00.9782 with MSVBVM60 6.00.9848, `/make` native and p-code in session 0 of the oracle guest. Native code and p-code gave identical answers in every row.
- **Two runs of each build:**
  - **Elevated:** started directly, as the session-0 process is, holding the right to set the clock.
  - **A basic-user token:** started with `runas /trustlevel:0x20000`, which removes administrator rights and, with them, the right to set the clock. That's the token an IDE run by an ordinary user has.
- **The guest was a shared machine,** so the program only ever assigned today's date or the time just read. A value that succeeded moved the clock by under a second at most.

| Statement | Elevated | Basic-user token |
|---|---|---|
| `Date = d`, `d` a Date holding today | ok | **70** Permission denied |
| `Date = CStr(d)` | ok | **70** |
| `Date = Now`, today with a time part | ok | **70** |
| `Date = v`, a Variant holding today | ok | **70** |
| `Date = CDbl(d)`, today's serial number | **13** Type mismatch | **13** |
| `Date = "garbage"` | **13** | **13** |
| `Date = ""` | **13** | **13** |
| `Date = Null` | **13** | **13** |
| `Date = o`, an Object holding Nothing | **91** | **91** |
| `Date = v`, a Variant holding `Array(1)` | **13** | **13** |
| `Time = t`, a Date read from `Time` | ok | **70** |
| `Time = CStr(Time)` | ok | **70** |
| `Time = Now`, today's date part | ok | **70** |
| `Time = CDbl(Time)` | **13** | **13** |
| `Time = "garbage"`, `Time = ""`, `Time = Null` | **13** each | **13** each |
| `Time = o`, Nothing | **91** | **91** |
| `Date$ = Format$(Date, "mm-dd-yyyy")` | compiles; ok | **70**, p-code (the native build wasn't run under this token) |
| `Time$ = Format$(Time, "hh:nn:ss")` | compiles; ok | **70**, p-code (the native build wasn't run under this token) |

- **The value is converted first, and the privilege checked after.** A value that won't convert raises 13 or 91 under
  either token. One that converts raises 70 when the process can't set the clock.
- **A number is not accepted,** not even today's serial number as a Double. Neither is Null: 13, not 94.
- **`Date$ =` and `Time$ =` are statements too,** and behave as `Date =` and `Time =` do.
- **Not measured, by design:**
  - an assignment that would change the clock, such as yesterday, a date out of the clock's range, or a time with a non-zero date part to a different day;
  - whether `Date = Now` ignores `Now`'s time part. The elevated run left the date unchanged, as it must have whichever way it works.
