using System.Collections;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Headless;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using SharpFuzz;

// Fuzzes a form's companion .frx as HexIDE reads it (#1222). The .frm named on the command line stays fixed; the
// .frx bytes come from AFL on standard input, or from files given after --replay.
//
// Each input goes through what opening the form and showing or saving it does without a window:
//   1. FrxDeserializer.Read cuts the companion at the offsets the form cites;
//   2. FormDeserializer.Deserialize builds the form, holding each cited record and collecting an ImageList's pictures;
//   3. every record is put through FrxImageHelper (kind, payload, and Avalonia's Skia decode of a bitmap or icon);
//   4. a StatusBar's panel pictures go through PanelPicture.FromRecord, and a RichTextBox's TextRTF through
//      RichTextBoxPersistence.ReadDocument and RtfToWrite, as the designer and the running form do;
//   5. FormSerializer renders the form and its companion again, as a save or a recovery capture does.
//
// A refusal HexIDE already makes on purpose, NotImplementedException naming the issue that would implement the
// record, is not a finding. Any other exception escapes to SharpFuzz, which reports it to AFL as a crash.

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: FrxFuzz <form.frm>                      fuzz: the .frx comes from standard input");
    Console.Error.WriteLine("       FrxFuzz <form.frm> --replay <file>...   run each file as the .frx and report what happened");
    return 2;
}

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var formText = Encoding.GetEncoding(1252).GetString(File.ReadAllBytes(args[0]));
var formName = Path.GetFileName(args[0]);

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

void Load(byte[] companion)
{
    var records = FrxDeserializer.Read(companion, formText);
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

    Refusable(() => new FormSerializer().Serialize(form, formName));
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
