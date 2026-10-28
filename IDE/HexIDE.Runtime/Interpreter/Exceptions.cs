using System;
using Antlr4.Runtime;

// ReSharper disable UnusedMember.Global

namespace HexIDE.Runtime.Interpreter;

public class VBRunTimeException : Exception
{
    // VB6 error provenance, separate from the CLR assembly name in Exception.Source.
    internal string? VbSource { get; set; }
    internal string? VbHelpFile { get; set; }
    internal long? VbHelpContext { get; set; }

    public ParserRuleContext? Context { get; }
    private readonly VBStandardError stdError;

    public VBStandardError Error => stdError;

    public VBRunTimeException(ParserRuleContext? context, VBStandardError stdError, string? extraMessage = null) : base(
        $"Run-time error '{stdError.ErrNo}':\n\n{stdError.Description}" + (extraMessage == null ? "" : "\n\n" + extraMessage))
    {
        Context = context;
        this.stdError = stdError;
    }

    // Context-less form: raised by built-in functions (CInt overflow, a Type Mismatch on a bad arg) that don't
    // have a parse context handy. Handlers must tolerate a null Context.
    public VBRunTimeException(VBStandardError stdError, string? extraMessage = null) : this((ParserRuleContext?)null, stdError, extraMessage) { }

    public VBRunTimeException(ParserRuleContext? context, string custom) : base($"Run-time error:\n\n{custom}")
    {
        Context = context;
    }

    private VBRunTimeException(string message, ParserRuleContext context, VBStandardError stdError, Exception inner)
        : base(message, inner)
    {
        Context = context;
        this.stdError = stdError;
    }

    /// <summary>
    /// This error as raised by <paramref name="statement"/>, for one a built-in raised with no statement to hand, so the
    /// run-time error dialog and <c>get_last_runtime_error</c> still name the statement. One that has a context keeps it.
    /// </summary>
    public VBRunTimeException At(ParserRuleContext statement) =>
        Context != null ? this : new VBRunTimeException(Message, statement, stdError, this)
            { VbSource = VbSource, VbHelpFile = VbHelpFile, VbHelpContext = VbHelpContext };
}

public class VBCompileErrorException : Exception
{
    public int? Line { get; init; }

    public VBCompileErrorException(string custom) : base($"Compile error:\n\n{custom}")
    {

    }
}

public class VBSubOrFunctionNotDefinedException : VBCompileErrorException
{
    public VBSubOrFunctionNotDefinedException(string ident) : base("Sub or Function not defined (" + ident + ")") {}
}
public class VBVariableNotDefinedException : VBCompileErrorException
{
    public VBVariableNotDefinedException(string ident) : base("Variable not defined (" + ident + ")") {}
}

public class VBMethodOrDataMemberNotFoundException : VBCompileErrorException
{
    public VBMethodOrDataMemberNotFoundException(string ident, HexIDE.Runtime.Interpreter.Vb6Value.ValueType type) : base($"Method or data member not found ({ident})") {}
}

public struct VBStandardError
{
    public VBStandardError(int errNo, string description)
    {
        ErrNo = errNo;
        Description = description;
    }

    public int ErrNo { get; }
    public string Description { get; }

    // The error NUMBERS are the canonical, load-bearing part of VB6 compatibility and are reproduced
    // exactly. The descriptions are reproduced for behavioural parity, not as creative content: VB6 code
    // traps on Err.Number and routinely surfaces Err.Description to the user (message boxes, logs, custom
    // error handlers), so a program run under HexIDE must report the same text its VB6 build would — a
    // program that string-matches or displays Err.Description would otherwise diverge. These are short,
    // functional condition labels ("Type mismatch", "Object doesn't support this property or method", …)
    // with essentially one practical wording each, kept verbatim for that interoperability. (A handful of
    // the longest, most descriptive entries were independently reworded to functionally-equivalent wording;
    // the number a program traps on is unchanged either way.)
    public static readonly VBStandardError ReturnWithoutGoSub = new(3, "Return without GoSub");
    public static readonly VBStandardError InvalidProcedureCall = new(5, "Invalid procedure call or argument");
    public static readonly VBStandardError Overflow = new(6, "Overflow");
    public static readonly VBStandardError OutOfMemory = new(7, "Out of memory");
    public static readonly VBStandardError SubscriptOutOfRange = new(9, "Subscript out of range");
    public static readonly VBStandardError ThisArrayIsFixedOrTemporarilyLocked = new(10, "This array is fixed or temporarily locked");
    public static readonly VBStandardError DivisionByZero = new(11, "Division by zero");
    public static readonly VBStandardError TypeMismatch = new(13, "Type mismatch");
    public static readonly VBStandardError OutOfStringSpace = new(14, "Out of string space");
    public static readonly VBStandardError ExpressionTooComplex = new(16, "Expression too complex");
    public static readonly VBStandardError CantPerformRequestedOperation = new(17, "Can't perform requested operation");
    public static readonly VBStandardError UserInterruptOccurred = new(18, "User interrupt occurred");
    public static readonly VBStandardError ResumeWithoutError = new(20, "Resume without error");
    public static readonly VBStandardError OutOfStackSpace = new(28, "Out of stack space");
    public static readonly VBStandardError SubFunctionOrPropertyNotDefined = new(35, "Sub or Function not defined");
    public static readonly VBStandardError TooManyDLLApplicationClients = new(47, "Too many DLL application clients");
    public static readonly VBStandardError ErrorInLoadingDLL = new(48, "Error in loading DLL");
    public static readonly VBStandardError BadDLLCallingConvention = new(49, "Bad DLL calling convention");
    public static readonly VBStandardError InternalError = new(51, "Internal error");
    public static readonly VBStandardError BadFileNameOrNumber = new(52, "Bad file name or number");
    public static readonly VBStandardError FileNotFound = new(53, "File not found");
    public static readonly VBStandardError BadFileMode = new(54, "Bad file mode");
    public static readonly VBStandardError FileAlreadyOpen = new(55, "File already open");
    public static readonly VBStandardError DeviceIOError = new(57, "Device I/O error");
    public static readonly VBStandardError FileAlreadyExists = new(58, "File already exists");
    public static readonly VBStandardError BadRecordLength = new(59, "Bad record length");
    public static readonly VBStandardError DiskFull = new(61, "Disk full");
    public static readonly VBStandardError InputPastEndOfFile = new(62, "Input past end of file");
    public static readonly VBStandardError BadRecordNumber = new(63, "Bad record number");
    public static readonly VBStandardError TooManyFiles = new(67, "Too many files");
    public static readonly VBStandardError DeviceUnavailable = new(68, "Device unavailable");
    public static readonly VBStandardError PermissionDenied = new(70, "Permission denied");
    public static readonly VBStandardError DiskNotReady = new(71, "Disk not ready");
    public static readonly VBStandardError CantRenameWithDifferentDrive = new(74, "Can't rename with different drive");
    public static readonly VBStandardError PathFileAccessError = new(75, "Path/File access error");
    public static readonly VBStandardError PathNotFound = new(76, "Path not found");
    public static readonly VBStandardError ObjectVariableOrWithBlockVariableNotSet = new(91, "Object variable or With block variable not set");
    public static readonly VBStandardError ForLoopNotInitialized = new(92, "For loop not initialized");
    public static readonly VBStandardError InvalidPatternString = new(93, "Invalid pattern string");
    public static readonly VBStandardError InvalidUseOfNull = new(94, "Invalid use of Null");
    public static readonly VBStandardError CantCallFriendProcedureOnAnObjectThatIsNotAnInstanceOfTheDefiningClass = new(97, "Can not call friend function on object which is not an instance of defining class");
    public static readonly VBStandardError APropertyOrMethodCallCannotIncludeAReferenceToAPrivateObjectEitherAsAnArgumentOrAsAReturnValue = new(98, "A property or method call cannot include a reference to a private object, either as an argument or as a return value");
    public static readonly VBStandardError SystemDLLCouldNotBeLoaded = new(298, "System DLL could not be loaded");
    public static readonly VBStandardError CantUseCharacterDeviceNamesInSpecifiedFileNames = new(320, "Can't use character device names in specified file names");
    public static readonly VBStandardError InvalidFileFormat = new(321, "Invalid file format");
    public static readonly VBStandardError CantCreateNecessaryTemporaryFile = new(322, "Can't create necessary temporary file");
    public static readonly VBStandardError InvalidFormatInResourceFile = new(325, "Invalid format in resource file");
    public static readonly VBStandardError DataValueNamedNotFound = new(327, "Data value named not found");
    public static readonly VBStandardError IllegalParameterCantWriteArrays = new(328, "Illegal parameter; can't write arrays");
    public static readonly VBStandardError CouldNotAccessSystemRegistry = new(335, "Could not access system registry");
    public static readonly VBStandardError ComponentNotCorrectlyRegistered = new(336, "Component not correctly registered");
    public static readonly VBStandardError ComponentNotFound = new(337, "Component not found");
    public static readonly VBStandardError ComponentDidNotRunCorrectly = new(338, "Component did not run correctly");
    public static readonly VBStandardError ControlArrayElementDoesntExist = new(340, "Control array element doesn't exist");
    /// <summary>VB6's 340 names the index it was given, read, written or unloaded (measured, #1386).</summary>
    public static VBStandardError ControlArrayElementIsMissing(int index) =>
        new(ControlArrayElementDoesntExist.ErrNo, $"Control array element '{index}' doesn't exist");
    public static readonly VBStandardError InvalidControlArrayIndex = new(341, "Invalid control array index");
    public static readonly VBStandardError ObjectNotAnArray = new(343, "Object not an array");
    public static readonly VBStandardError ObjectAlreadyLoaded = new(360, "Object already loaded");
    public static readonly VBStandardError CantLoadOrUnloadThisObject = new(361, "Can't load or unload this object");
    public static readonly VBStandardError CantUnloadControlsCreatedAtDesignTime = new(362, "Can't unload controls created at design time");
    public static readonly VBStandardError ControlSpecifiedNotFound = new(363, "Control specified not found");
    public static readonly VBStandardError ObjectWasUnloaded = new(364, "Object was unloaded");
    public static readonly VBStandardError UnableToUnloadWithinThisContext = new(365, "Unable to unload within this context");
    public static readonly VBStandardError TheSpecifiedFileIsOutOfDateThisProgramRequiresALaterVersion = new(368, "This file is an older version than the program requires");
    public static readonly VBStandardError TheSpecifiedObjectCantBeUsedAsAnOwnerFormForShow = new(371, "That object can't act as the owner form for Show");
    public static readonly VBStandardError InvalidPropertyValue = new(380, "Invalid property value");
    public static readonly VBStandardError InvalidPropertyArrayIndex = new(381, "Invalid property array index");
    public static readonly VBStandardError PropertySetCantBeExecutedAtRunTime = new(382, "Set not supported at runtime");
    public static readonly VBStandardError PropertySetCantBeUsedWithAReadOnlyProperty = new(383, "Set not supported (read-only property)");
    public static readonly VBStandardError NeedPropertyArrayIndex = new(385, "Need property array index");
    public static readonly VBStandardError PropertySetNotPermitted = new(387, "Set not permitted");
    public static readonly VBStandardError PropertyGetCantBeExecutedAtRunTime = new(393, "Get not supported at runtime");
    public static readonly VBStandardError PropertyGetCantBeExecutedOnWriteOnlyProperty = new(394, "Get not supported (write-only property)");
    public static readonly VBStandardError FormAlreadyDisplayedCantShowModally = new(400, "Form already displayed; can't show modally");
    public static readonly VBStandardError CantShowNonModalFormWhenModalFormIsDisplayed = new(401, "Can't show non-modal form when modal form is displayed");
    public static readonly VBStandardError CodeMustCloseTopmostModalFormFirst = new(402, "Must close or hide topmost modal form first");
    public static readonly VBStandardError UnableToShowModalFormWithinThisContext = new(405, "Unable to show modal form within this context");
    public static readonly VBStandardError PermissionToUseObjectDenied = new(419, "Permission to use object denied");
    public static readonly VBStandardError PropertyNotFound = new(422, "Property not found");
    public static readonly VBStandardError PropertyOrMethodNotFound = new(423, "Property or method not found");
    public static readonly VBStandardError ObjectRequired = new(424, "Object required");
    public static readonly VBStandardError InvalidObjectUse = new(425, "Invalid object use");
    public static readonly VBStandardError ComponentCantCreateObjectOrReturnReferenceToThisObject = new(429, "ActiveX component can't create object");
    public static readonly VBStandardError ClassDoesntSupportAutomation = new(430, "Class does not support Automation or does not support expected interface");
    public static readonly VBStandardError FileNameOrClassNameNotFoundDuringAutomationOperation = new(432, "File name or class name not found during Automation operation");
    public static readonly VBStandardError ObjectDoesntSupportThisPropertyOrMethod = new(438, "Object doesn't support this property or method");
    public static readonly VBStandardError AutomationError = new(440, "Automation error");
    public static readonly VBStandardError ConnectionToTypeLibraryOrObjectLibraryForRemoteProcessHasBeenLost = new(442, "Connection to type library or object library for remote process has been lost. Press OK for dialog to remove reference.");
    public static readonly VBStandardError AutomationObjectDoesntHaveADefaultValue = new(443, "Automation object does not have a default value");
    public static readonly VBStandardError ObjectDoesntSupportThisAction = new(445, "Object doesn't support this action");
    public static readonly VBStandardError ObjectDoesntSupportNamedArguments = new(446, "Object doesn't support named arguments");
    public static readonly VBStandardError ObjectDoesntSupportCurrentLocaleSetting = new(447, "Object doesn't support current locale setting");
    public static readonly VBStandardError NamedArgumentNotFound = new(448, "Named argument not found");
    public static readonly VBStandardError ArgumentNotOptionalOrInvalidPropertyAssignment = new(449, "Argument not optional");
    public static readonly VBStandardError WrongNumberOfArgumentsOrInvalidPropertyAssignment = new(450, "Wrong number of arguments or invalid property assignment");
    public static readonly VBStandardError ObjectNotACollection = new(451, "Property let procedure not defined and property get procedure did not return an object");
    public static readonly VBStandardError InvalidOrdinal = new(452, "Invalid ordinal");
    public static readonly VBStandardError SpecifiedNotFound = new(453, "Specified DLL function not found");
    public static readonly VBStandardError CodeResourceNotFound = new(454, "Code resource not found");
    public static readonly VBStandardError CodeResourceLockError = new(455, "Code resource lock error");
    public static readonly VBStandardError ThisKeyIsAlreadyAssociatedWithAnElementOfThisCollection = new(457, "This key is already associated with an element of this collection");
    public static readonly VBStandardError VariableUsesATypeNotSupportedInVisualBasic = new(458, "Variable uses an Automation type not supported in Visual Basic");
    public static readonly VBStandardError ThisComponentDoesntSupportTheSetOfEvents = new(459, "Object or class does not support the set of events");
    public static readonly VBStandardError InvalidClipboardFormat = new(460, "Invalid clipboard format");
    public static readonly VBStandardError MethodOrDataMemberNotFound = new(461, "Method or data member not found");
    public static readonly VBStandardError TheRemoteServerMachineDoesNotExistOrIsUnavailable = new(462, "The remote server machine does not exist or is unavailable");
    public static readonly VBStandardError ClassNotRegisteredOnLocalMachine = new(463, "Class not registered on local machine");
    public static readonly VBStandardError CantCreateAutoRedrawImage = new(480, "Can't create AutoRedraw image");
    public static readonly VBStandardError InvalidPicture = new(481, "Invalid picture");
    public static readonly VBStandardError PrinterError = new(482, "Printer error");
    public static readonly VBStandardError PrinterDriverDoesNotSupportSpecifiedProperty = new(483, "Printer driver does not support specified property");
    public static readonly VBStandardError ProblemGettingPrinterInformationFromTheSystemMakeSureThePrinterIsSetUpCorrectly = new(484, "Couldn't read printer information from the system; check that the printer is installed correctly");
    public static readonly VBStandardError InvalidPictureType = new(485, "Invalid picture type");
    public static readonly VBStandardError CantPrintFormImageToThisTypeOfPrinter = new(486, "Can't print form image to this type of printer");
    public static readonly VBStandardError CantEmptyClipboard = new(520, "Can't empty Clipboard");
    public static readonly VBStandardError CantOpenClipboard = new(521, "Can't open Clipboard");
    /// <summary>A form's <c>Controls("")</c> (measured, #1060).</summary>
    public static VBStandardError NotALegalControlName(string name) => new(728, $"'{name}' is not a legal control name");
    /// <summary>A form's <c>Controls("Name")</c> where it has no control of that name, the name as it was given (measured, #1060).</summary>
    public static VBStandardError ControlNotFound(string name) => new(730, $"Control '{name}' not found");
    public static readonly VBStandardError CantSaveFileToTEMPDirectory = new(735, "Can't save file to TEMP");
    /// <summary>A ProgID <c>Controls.Add</c> finds nothing registered for; the description's break is a line feed alone (measured, #1556).</summary>
    public static VBStandardError InvalidClassString(string progId) => new(711, $"Invalid class string.\nLooking for object with ProgID: {progId}");
    /// <summary><c>Controls.Add</c> of a class that is no control: <c>'Form'</c> for the VB library's own, the whole ProgID otherwise (measured, #1556).</summary>
    public static VBStandardError NotAValidControlType(string name) => new(720, $"'{name}' is not a valid control type");
    /// <summary><c>Controls.Add</c> into a container that can't hold controls, by its class or <c>&lt;Nothing&gt;</c> (measured, #1556).</summary>
    public static VBStandardError NotAValidControlContainer(string name) => new(721, $"'{name}' is not a valid control container");
    /// <summary><c>Controls.Add</c> of a name a control on the form already has, in any case, the name as it was given (measured, #1556).</summary>
    public static VBStandardError ControlNameTaken(string name) => new(727, $"There is already a control with the name '{name}'");
    public static readonly VBStandardError OnlyAddedControlsCanBeRemoved = new(729, "Controls.Remove can only remove controls added with Controls.Add");
    /// <summary>Any use of a control Controls.Remove has taken out (measured, #1556): 735 has this second meaning there.</summary>
    public static readonly VBStandardError ControlRemoved = new(735, "The control is no longer usable because it has been removed from the controls collection");
    // What a late-bound call answers as the raw HRESULT rather than as VB6's own number: Controls.Add and Remove with too few
    // or too many arguments, Null or a String where an object is wanted, and Remove of a control array's name (measured, #1556).
    public static readonly VBStandardError DispatchWrongNumberOfArguments = new(-2147352562, "Wrong number of arguments or invalid property assignment");
    public static readonly VBStandardError DispatchTypeMismatch = new(-2147352571, "Type mismatch");
    public static readonly VBStandardError NoSuchInterfaceSupported = new(-2147467262, "System Error &H80004002 (-2147467262).   No such interface supported ");
    public static readonly VBStandardError SearchTextNotFound = new(744, "Search text not found");
    public static readonly VBStandardError ReplacementsTooLong = new(746, "Replacements too long");
    public static readonly VBStandardError OutOfMemory2 = new(31001, "Out of memory");
    public static readonly VBStandardError NoObject = new(31004, "No object");
    public static readonly VBStandardError ClassIsNotSet = new(31018, "Class is not set");
    public static readonly VBStandardError UnableToActivateObject = new(31027, "Unable to activate object");
    public static readonly VBStandardError UnableToCreateEmbeddedObject = new(31032, "Unable to create embedded object");
    public static readonly VBStandardError ErrorSavingToFile = new(31036, "Error saving to file");
    public static readonly VBStandardError ErrorLoadingFromFile = new(31037, "Error loading from file");
}