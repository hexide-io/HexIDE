using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using HexIDE.Addins;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;

namespace HexIDE.Integration.Tests.Views;

public class DesignerModuleReadOnlyTests
{
    // #4459: drive actual text input after opening the misdeclared member through either loader.
    [AvaloniaTheory]
    [InlineData("VB.Form", false, false)]
    [InlineData("VB.UserControl", false, false)]
    [InlineData("VB.Form", true, false)]
    [InlineData("VB.Form", false, true)]
    [InlineData("VB.UserControl", false, true)]
    [InlineData("VB.Form", true, true)]
    public async Task Typing_into_a_designer_file_declared_as_a_module_is_refused(string root, bool ocx, bool contributed)
    {
        var owned = new RealComposition();
        var composition = owned.Composition;
        _ = composition.Root;
        var directory = Path.Join(Path.GetTempPath(), "hexide-designer-module-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var window = new Window { Width = 800, Height = 600 };
        IDisposable? type = null;
        try
        {
            var text = "VERSION 5.00\r\n" + (ocx ? "Object = \"X\"; \"X.OCX\"\r\n" : "")
                + $"Begin {root} Form1\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\nOption Explicit\r\n";
            File.WriteAllText(Path.Join(directory, "Form1.frm"), text);
            type = contributed ? composition.ContributedProjects.Register(new AddinProjectType("Stand-in projects")
            {
                FileNames = ["*.rdproj"],
                OpenAsync = (_, _, _) => Task.FromResult(new AddinProjectContent("Test")
                {
                    Members = [new AddinProjectMember(Path.Join(directory, "Form1.frm"), AddinMemberKind.StandardModule)],
                }),
            }, new AddinOrigin("provider", "A provider")) : null;
            var path = Path.Join(directory, contributed ? "Test.rdproj" : "Test.vbp");
            File.WriteAllText(path, "Type=Exe\r\nModule=Form1; Form1.frm\r\nName=\"Test\"\r\n");
            await composition.ProjectService.OpenProject(path);
            var module = composition.ProjectManager.LoadedProjects.Single().Modules.Single();
            composition.EditorService.EditCode(module);
            var model = composition.DocumentDockService.OpenDocuments.OfType<CodeEditorViewModel>().Single();
            var view = new CodeEditorView { DataContext = model };
            window.Content = view;
            window.Show();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var editor = view.FindControl<TextEditor>("TextEditor")!;
            editor.CaretOffset = editor.Document.TextLength;
            editor.TextArea.Focus().Should().BeTrue();
            var before = editor.Document.Text;

            window.KeyTextInput("changed");
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            editor.Document.Text.Should().Be(before);
            model.IsReadOnly.Should().BeTrue();
            var banner = view.FindControl<Border>("DesignerModuleReadOnlyBanner")!;
            banner.IsVisible.Should().BeTrue();
            ((TextBlock)banner.Child!).Text.Should().Be(model.ReadOnlyReason);
            model.ReadOnlyReason.Should().Contain("Form1.frm");
        }
        finally
        {
            composition.DocumentDockService.CloseAll();
            window.Close();
            foreach (var project in composition.ProjectManager.LoadedProjects.ToArray())
                composition.ProjectManager.UnloadProject(project);
            type?.Dispose();
            // The composition watches the project's folder until it is disposed, and Windows won't delete a watched folder.
            await owned.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
