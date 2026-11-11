using System;
using System.IO;
using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

public class ImageComboLoadingTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = [];
        public void LogError(string error) => Errors.Add(error);
    }

    private static string Corpus()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "corpus")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "corpus/inbox-controls/pending/imagecombo");
    }

    [AvaloniaFact]
    public void Carried_ImageCombo_is_not_runnable_until_it_draws()
    {
        FormDeserializer.RunsWhenCarried("MSComctlLib.ImageCombo", false, false).Should().BeFalse();
        FormDeserializer.RunsWhenCarried("mscomctllib.imagecombo", false, false).Should().BeFalse();
    }

    [AvaloniaFact]
    public void Every_saved_form_warns_and_refuses_at_F5_naming_1092()
    {
        var forms = Directory.GetFiles(Corpus(), "*.frm");
        forms.Should().NotBeEmpty();
        foreach (var path in forms)
        {
            var project = new ProjectDefinition(VBProjectType.EXE, "P");
            var sink = new Sink();
            var form = new FormDeserializer().Deserialize(project, File.ReadAllText(path), sink)!;
            form.UnknownChildSubtreeTexts.Count.Should().Be(8);
            sink.Errors.Should().HaveCount(8);
            sink.Errors.Should().OnlyContain(error => error.Contains("MSComctlLib.ImageCombo"));
            Action load = () => VBLoader.SpawnComponents(form, new ModuleExecutionContext(), new ExecutionEnvironment());
            load.Should().Throw<NotImplementedException>().WithMessage("*ImageCombo*#1092*");
        }
    }
}
