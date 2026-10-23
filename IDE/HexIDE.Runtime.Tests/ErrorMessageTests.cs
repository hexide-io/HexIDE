using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The text vb6.exe gives an error number (#1560): Err.Raise with no Description takes it from the same table Error(n)
/// answers from, so a number with no text of its own there is "Application-defined or object-defined error", even one
/// such as 340 that a forms runtime raises with words of its own. Each expected line is what vb6.exe logged, native and
/// p-code alike.
/// </summary>
public class ErrorMessageTests : BaseVBTestFixture
{
    [Fact]
    public async Task Err_Raise_with_no_Description_takes_vb6s_text_for_the_number()
    {
        await Run("""
            On Error Resume Next
            Err.Clear: Err.Raise 340: Debug.Print "raise 340: " & Err.Description
            Err.Clear: Err.Raise 360: Debug.Print "raise 360: " & Err.Description
            Err.Clear: Err.Raise 400: Debug.Print "raise 400: " & Err.Description
            Err.Clear: Err.Raise 31004: Debug.Print "raise 31004: " & Err.Description
            Err.Clear: Err.Raise 35: Debug.Print "raise 35: " & Err.Description
            Err.Clear: Err.Raise 449: Debug.Print "raise 449: " & Err.Description
            Err.Clear: Err.Raise 451: Debug.Print "raise 451: " & Err.Description
            Err.Clear: Err.Raise 429: Debug.Print "raise 429: " & Err.Description
            Err.Clear: Err.Raise 96: Debug.Print "raise 96: " & Err.Description
            Err.Clear: Err.Raise 97: Debug.Print "raise 97: " & Err.Description
            Err.Clear: Err.Raise 1000: Debug.Print "raise 1000: " & Err.Description
            Err.Clear: Err.Raise 65535: Debug.Print "raise 65535: " & Err.Description
            Err.Clear: Err.Raise 13: Debug.Print "raise 13: " & Err.Description
            Dim a(1) As Integer
            Err.Clear: a(5) = 1: Debug.Print "runtime 9: " & Err.Description
            """);

        AssertDebugLog(Lines("""
            raise 340: Application-defined or object-defined error
            raise 360: Application-defined or object-defined error
            raise 400: Application-defined or object-defined error
            raise 31004: Application-defined or object-defined error
            raise 35: Sub or Function not defined
            raise 449: Argument not optional
            raise 451: Property let procedure not defined and property get procedure did not return an object
            raise 429: ActiveX component can't create object
            raise 96: Unable to sink events of object because the object is already firing events to the maximum number of event receivers that it supports
            raise 97: Can not call friend function on object which is not an instance of defining class
            raise 1000: Application-defined or object-defined error
            raise 65535: Application-defined or object-defined error
            raise 13: Type mismatch
            runtime 9: Subscript out of range
            """));
    }

    private static List<Vb6Value> Lines(string text) =>
        text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => new Vb6Value(l)).ToList();
}
