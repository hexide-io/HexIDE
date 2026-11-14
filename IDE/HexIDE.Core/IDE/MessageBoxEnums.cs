namespace HexIDE.IDE;

public enum MessageBoxResult
{
    None,
    Ok,
    Cancel,
    Abort,
    Retry,
    Ignore,
    Yes,
    No,
    TryAgain,
    Continue
}

public enum MessageBoxButtons
{
    Ok,
    OkCancel,
    AbortRetryIgnore,
    YesNoCancel,
    YesNo,
    RetryCancel
}

public enum MessageBoxIcon
{
    None,
    Error,
    Warning,
    Question,
    Information
}

[System.Flags]
public enum MessageBoxLayout
{
    None = 0,
    Right = 1,
    RtlReading = 2
}
