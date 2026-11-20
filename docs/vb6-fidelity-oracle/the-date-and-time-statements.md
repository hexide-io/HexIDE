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

### The other value types, an object, and the date range (2026-11-18, #4733)

**The rig:**
- **Build and run:** vb6.exe 6.00.9782 with MSVBVM60 6.00.9848, `/make` native and p-code in session 0 of the oracle guest.
- **No clock privilege:** the exe ran as Basic User through `runas /trustlevel:0x20000`, so it held no right to set the clock.
- **The guard:** the program first ran `Date = Date`, and stopped unless that raised 70. It raised 70, and the guest's date was the same after the run.
- **The rows:** each statement ran under `On Error Resume Next`, logging the error. Native code and p-code agreed in every row.
- **`c`:** a `New Class1`, whose default member (`VB_UserMemId = 0`) is a Function returning `Date`.

| Statement | VB6 |
|---|---|
| `Date = CInt(5)`, `CLng(5)`, `CByte(5)`, `CSng(5)` | **13** |
| `Date = n`, `n` a Long holding today's serial; `CCur(n)`; `CDec(n)` | **13** |
| `Date = -1`, an Integer | **13** |
| `Date = True`, `Date = Empty`, `Date = CVErr(5)` | **13** each |
| `Date = c` | **70**: the default member is read, and gives a Date |
| `Date = o`, `o As Object` holding `c` | **70** |
| `Date = "12/31/9999"`, `"1/1/100"`, `"1/1/99"` | 70: these convert |
| `Date = CDate(-1)`, 1899-12-29 | 70 |
| `Date = "1/1/10000"`, out of range | **13** |
| `Time = CInt(0)`, `CSng(0.5)`, `CCur(0.5)`, `True`, `Empty`, `CVErr(5)` | **13** each |
| `Time = c` | 70 |
| `Time = "25:00"` | **13** |

- **Only a Date or a String converts,** and an object only through a default member that gives one. Every numeric type raises 13, as the Double above does, and so do Boolean, Empty and an Error value. They raise it before the privilege is checked.
- **A String must name a date in VB6's range,** 1/1/100 to 12/31/9999. Outside it, 13.
- **The obvious guess was that `Date = 5` converts as `CDate(5)` does,** to 1900-01-04. It doesn't: the statement takes no number.

**Not measured:** an object with no default member, and one whose default member gives something other than a Date or a String.
