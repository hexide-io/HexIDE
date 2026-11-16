using System.Collections;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Headless;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using SharpFuzz;

// Fuzzes a form as HexIDE reads it (#1222), in one of two modes:
//   - a form's companion .frx: the .frm named on the command line stays fixed, and the .frx bytes vary;
//   - with --frm, the .frm itself, with no companion, as a form whose .frx is missing is read.
// The varying bytes come from AFL on standard input, or from files given after --replay.
//
// Each input goes through what opening the form and showing or saving it does without a window:
//   1. FrxDeserializer.Read cuts the companion at the offsets the form cites;
//   2. FormDeserializer.Deserialize builds the form, holding each cited record and collecting an ImageList's pictures;
//   3. every record is put through FrxImageHelper (kind, payload, and Avalonia's Skia decode of a bitmap or icon);
//   4. a StatusBar's panel pictures go through PanelPicture.FromRecord, and a RichTextBox's TextRTF through
//      RichTextBoxPersistence.ReadDocument and RtfToWrite, as the designer and the running form do;
//   5. when HexIDE would let the form be saved, FormSerializer renders it and its companion again, as a save does;
//   6. what that save wrote is read back and saved again. A form HexIDE wrote must read
//      back, and the second save must write the same bytes: anything else is a save that damages the file.
//
// A refusal HexIDE already makes on purpose, NotImplementedException naming the issue that would implement the
// record, is not a finding. Any other exception escapes to SharpFuzz, which reports it to AFL as a crash.

var frmMode = args.Length > 0 && args[0] == "--frm";
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: FrxFuzz <form.frm>                      fuzz: the .frx comes from standard input");
    Console.Error.WriteLine("       FrxFuzz <form.frm> --replay <file>...   run each file as the .frx and report what happened");
    Console.Error.WriteLine("       FrxFuzz --frm                           fuzz: the .frm comes from standard input");
    Console.Error.WriteLine("       FrxFuzz --frm --replay <file>...        run each file as the .frm and report what happened");
    return 2;
}

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var ansi = Encoding.GetEncoding(1252);
var fixedFormText = frmMode ? "" : ansi.GetString(File.ReadAllBytes(args[0]));
var formName = frmMode ? "Form1.frm" : Path.GetFileName(args[0]);

AppBuilder.Configure<Application>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

if (args.Length > 1 && args[1] == "--replay")
{
    var failed = 0;
    foreach (var file in args.Skip(2))
    {
        try
        {
            Load(File.ReadAllBytes(file));
            Console.WriteLine($"ok       {file}");
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine($"FINDING  {file}");
            Console.WriteLine(ex.ToString());
        }
    }
    return failed == 0 ? 0 : 1;
}

Fuzzer.Run(stream =>
{
    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    Load(buffer.ToArray());
});
return 0;

void Load(byte[] input)
{
    var formText = frmMode ? ansi.GetString(input) : fixedFormText;
    var records = frmMode ? null : FrxDeserializer.Read(input, formText);
    var project = new ProjectDefinition(VBProjectType.EXE, "Fuzz");
    var form = new FormDeserializer().Deserialize(project, formText, new DiscardingSink(), records);
    if (form is null)
        return;

    foreach (var instance in form.Components)
    {
        foreach (var record in RecordsOf(instance))
            Refusable(() => Decode(record));

        var className = instance.BaseClass.GetType().Name;
        if (className == nameof(RichTextBoxComponentClass))
        {
            Refusable(() => CallInternal(typeof(RichTextBoxPersistence), "ReadDocument", instance));
            Refusable(() => CallInternal(typeof(RichTextBoxPersistence), "RtfToWrite", instance));
        }
        foreach (var value in instance.GetAllSetProperties().Values)
            if (value is IReadOnlyDictionary<int, byte[]> pictures)
                foreach (var picture in pictures.Values)
                    Refusable(() => CallInternal(PanelPicture, "FromRecord", picture));
    }

    // Only a form HexIDE would save is written, as the save path refuses the rest before rendering anything.
    if (form.CanSaveFaithfully)
        Refusable(() => SavesStably(form));
    // For triage: FRXFUZZ_VERBOSE=1 says whether the save check ran, as a refused save makes a pass vacuous.
    if (Environment.GetEnvironmentVariable("FRXFUZZ_VERBOSE") == "1")
        Console.WriteLine(form.CanSaveFaithfully ? "  saved and read back" : $"  not saveable: {form.UnfaithfulSaveReason}");
}

// A save HexIDE allows must write a form it can read back, and saving that again must change nothing.
void SavesStably(FormDefinition form)
{
    var (firstText, firstCompanion) = new FormSerializer().Serialize(form, formName);
    // For triage: FRXFUZZ_DUMP=<folder> keeps the first save, and the second when they differ.
    var dump = Environment.GetEnvironmentVariable("FRXFUZZ_DUMP");
    if (dump is { Length: > 0 })
    {
        File.WriteAllText(Path.Combine(dump, "first.frm"), firstText, ansi);
        if (firstCompanion is not null)
            File.WriteAllBytes(Path.Combine(dump, "first.frx"), firstCompanion);
    }
    var reread = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "Fuzz"), firstText,
        new DiscardingSink(), firstCompanion is null ? null : FrxDeserializer.Read(firstCompanion, firstText))
        ?? throw new InvalidOperationException("HexIDE saved a form it cannot read back");
    if (!reread.CanSaveFaithfully)
        throw new InvalidOperationException($"HexIDE saved a form it then refuses to save: {reread.UnfaithfulSaveReason}");
    var (secondText, secondCompanion) = new FormSerializer().Serialize(reread, formName);
    if (secondText != firstText)
    {
        if (dump is { Length: > 0 })
            File.WriteAllText(Path.Combine(dump, "second.frm"), secondText, ansi);
        var first = firstText.Split('\n');
        var second = secondText.Split('\n');
        var line = Enumerable.Range(0, Math.Min(first.Length, second.Length)).FirstOrDefault(i => first[i] != second[i],
            Math.Min(first.Length, second.Length));
        throw new InvalidOperationException($"Saving the form again changed its .frm, first at line {line + 1}: "
            + $"[{(line < first.Length ? first[line].TrimEnd() : "(end)")}] became "
            + $"[{(line < second.Length ? second[line].TrimEnd() : "(end)")}]");
    }
    if (!(firstCompanion ?? []).AsSpan().SequenceEqual(secondCompanion ?? []))
        throw new InvalidOperationException("Saving the form again changed its .frx");
}

static void Decode(byte[] record)
{
    FrxImageHelper.DetectKind(record);
    var payload = FrxImageHelper.PicturePayload(record);
    if (payload is not null)
        FrxImageHelper.DetectKind(payload);
    FrxImageHelper.TryDecodeToAvaloniaBitmap(record)?.Dispose();
}

// Every companion record a control holds: a byte[] property, a record inside a held collection (an ImageList's
// pictures), a dictionary of records (a StatusBar's panel pictures), and the records an unmodelled property cites.
static IEnumerable<byte[]> RecordsOf(ComponentInstance instance)
{
    foreach (var value in instance.GetAllSetProperties().Values)
    {
        switch (value)
        {
            case byte[] bytes:
                yield return bytes;
                break;
            case IReadOnlyDictionary<int, byte[]> dictionary:
                foreach (var bytes in dictionary.Values) yield return bytes;
                break;
            case IEnumerable items and not string:
                foreach (var item in items)
                    if (item?.GetType().GetProperty("Record")?.GetValue(item) is byte[] held)
                        yield return held;
                break;
        }
    }
    foreach (var raw in instance.UnknownRawProperties)
        if (raw.Records is { } cited)
            foreach (var bytes in cited.Values)
                yield return bytes;
}

static object? CallInternal(Type type, string method, object argument)
{
    var target = type.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(type.FullName, method);
    try
    {
        return target.Invoke(null, [argument]);
    }
    catch (TargetInvocationException ex) when (ex.InnerException is not null)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw;
    }
}

// HexIDE's deliberate refusal of a record it doesn't implement yet. Everything else is a finding.
static void Refusable(Action action)
{
    try
    {
        action();
    }
    catch (NotImplementedException)
    {
    }
}

partial class Program
{
    private static readonly Type PanelPicture =
        typeof(FrxDeserializer).Assembly.GetType("HexIDE.Runtime.Interpreter.PanelPicture", throwOnError: true)!;
}

sealed class DiscardingSink : IDeserializeErrorSink
{
    public void LogError(string error)
    {
    }
}
