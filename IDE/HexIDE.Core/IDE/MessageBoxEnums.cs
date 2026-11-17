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
    RtlReading = 2,

    /// <summary>
    /// Not a layout: the second button is the default, which Enter presses and which has the focus, as VB6's
    /// <c>vbDefaultButton2</c> makes it. For a question whose safe answer is the second, such as No to running code
    /// from outside this computer (#1121). The IDE's own boxes read it; a program's box ignores it.
    /// </summary>
    SecondButtonDefault = 4,

    /// <summary>
    /// Not a layout: the box asks the person a security question, such as whether to run code from outside this
    /// computer (#1121). Automation describes such a box as the person's decision and never says how to answer it.
    /// </summary>
    SecurityPrompt = 8
}
