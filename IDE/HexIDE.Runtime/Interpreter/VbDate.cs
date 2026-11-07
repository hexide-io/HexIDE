using System;
using System.Globalization;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// A VB6 Date: its serial, a Double counting days from 30 December 1899 with the time of day as the fraction, kept exactly
/// as the program made it (#783). A day before the epoch is negative and its time of day is still added, so -1.25 is
/// 29 December 1899 06:00.
/// <para>
/// VB6 keeps a fraction of a second and never shows one. Arithmetic, comparison, <c>CDbl</c>, <c>Put #</c> and the
/// day-and-time intervals of <c>DateAdd</c> see the serial; display, the date functions and the calendar intervals of
/// <c>DateAdd</c> see <see cref="Instant"/>, the serial rounded to the second. Measured on vb6.exe:
/// docs/vb6-fidelity-oracle/a-dates-fraction-of-a-second.md.
/// </para>
/// </summary>
public readonly record struct VbDate(double Serial) : IComparable<VbDate>
{
    private static readonly DateTime Epoch = new(1899, 12, 30);

    // Half a second, which VB6 adds before it cuts the time to whole seconds.
    private const double HalfSecond = 1.0 / 172800.0;

    // 9999-12-31 00:00:00. Once the serial with its half second added is past it, VB6 does not round at all (measured).
    private const double LastRoundedSerial = 2958465.0;

    /// <summary>Whether VB6 holds <paramref name="serial"/> as a Date: strictly between -657435 and 2958466, which are each
    /// Overflow, where a millisecond inside either is a date (measured).</summary>
    public static bool InRange(double serial) => serial > -657435.0 && serial < 2958466.0;

    /// <summary>The Date for a calendar instant, as VB6 builds one from its fields: whole seconds, so the serial is the
    /// nearest Double to the days and seconds it counts.</summary>
    public static VbDate FromDateTime(DateTime value) => new(value.ToOADate());

    /// <summary>
    /// The instant VB6 shows and its date functions read: the serial to the second. Fitted to 418 serials on vb6.exe, with
    /// no exception, each step forced by a row:
    /// <list type="number">
    /// <item>The day is the serial's whole part, toward zero.</item>
    /// <item>Half a second is added to the serial, away from zero, in Doubles. So a tie falls one way or the other with the
    /// serial's magnitude: 12:00:00.5 and 12:00:02.5 round up, 12:00:01.5 down.</item>
    /// <item>Unless that is past 9999-12-31 00:00, where nothing rounds: 2958464 + 86399.5 s, which lands on it exactly,
    /// rounds up, and 2958464 + 86399.6 s does not.</item>
    /// <item>The time is the half-added serial less the day, times 86400 in Doubles, cut to whole seconds.</item>
    /// <item>A time of 24:00 is the next calendar day: 1899-12-30 23:59:59.6, the serial -0.99999537, is 31 December.</item>
    /// </list>
    /// </summary>
    public DateTime Instant
    {
        get
        {
            var day = Math.Truncate(Serial);
            var rounded = Serial + (Serial >= 0 ? HalfSecond : -HalfSecond);
            if (rounded > LastRoundedSerial)
                rounded = Serial;
            var seconds = Math.Floor(Math.Abs(rounded - day) * 86400.0);
            if (seconds >= 86400)
            {
                seconds -= 86400;
                day += 1;
            }
            return Epoch.AddDays(day).AddSeconds(seconds);
        }
    }

    /// <summary>
    /// The serial as days on one continuous line, where it is a date after the epoch, and its time of day added after the
    /// day where it is before it: -1.25 is -0.75. <c>DateAdd</c> adds its days, hours, minutes and seconds there (measured:
    /// one day back from 12:00:00.6 on the epoch is -1.5000069, 29 December 12:00:00.6).
    /// </summary>
    public double Continuous
    {
        get
        {
            if (Serial >= 0)
                return Serial;
            var day = Math.Truncate(Serial);
            return day - (Serial - day);
        }
    }

    /// <summary>The Date at <paramref name="continuous"/> days on the line <see cref="Continuous"/> measures.</summary>
    public static double SerialOfContinuous(double continuous)
    {
        var day = Math.Floor(continuous);
        var time = continuous - day;
        return day >= 0 ? day + time : day - time;
    }

    public int CompareTo(VbDate other) => Serial.CompareTo(other.Serial);

    // Never shown to a program, which goes through VbDisplay; this is for a debugger's eye and an exception message.
    public override string ToString() => Instant.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
