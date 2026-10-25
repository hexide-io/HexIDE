using System.Reflection;
using System.Runtime.CompilerServices;
using Dock.Model.Core;
using HexIDE;

namespace HexIDE.Tests.ViewModels;

public class DockFactoryWiringTests
{
    [Fact]
    public void ApplicationComposition_RegistersEveryLayoutToolByIdentity()
    {
        // Generated ordering can mask the symptom while the back edge remains (#895).
        typeof(DocumentDockService).GetConstructors().Single().GetParameters()
            .Should().NotContain(p => MentionsFactory(p.ParameterType));

        var composition = new DISetup();
        var root = composition.Root;
        var factory = (MainViewViewModel.DockFactory)typeof(MainViewViewModel)
            .GetField("dockFactory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(root)!;

        (IDockable tool, string key)[] tools =
        [
            (root.ToolBox, "toolbox"), (root.ProjectExplorer, "project"),
            (root.Properties, "properties"), (root.FormLayout, "formLayout"),
            (root.Immediate, "immediate"), (root.Locals, "locals"),
            (root.Watches, "watches"), (root.CallStack, "callStack"),
            (root.ColorPalette, "colorPalette"), (root.ObjectBrowser, "objectBrowser"),
            (root.TranslationEditor, "translationEditor"), (root.LanguageServers, "languageServers"),
        ];
        foreach (var (tool, key) in tools)
            factory.KeyOf(tool).Should().Be(key);

        // Populate the dock directly so this wiring check cannot schedule a per-user layout save.
        if (!factory.DocumentDock!.VisibleDockables!.Contains(root.LanguageServers))
            factory.DocumentDock.VisibleDockables.Add(root.LanguageServers);
        factory.InitDockable(root.LanguageServers, factory.DocumentDock);
        composition.DocumentDockService.AllTabs.Should().Contain(root.LanguageServers);
        composition.DocumentDockService.TryActivateAny(t => ReferenceEquals(t, root.LanguageServers))
            .Should().BeTrue();
    }

    private static bool MentionsFactory(Type type) =>
        type == typeof(MainViewViewModel.DockFactory) || type.GetGenericArguments().Any(MentionsFactory);

    public static IEnumerable<object[]> ConstructorArguments() =>
        typeof(MainViewViewModel.DockFactory).GetConstructors().Single().GetParameters()
            .Select(p => new object[] { p.Name! });

    [Theory]
    [MemberData(nameof(ConstructorArguments))]
    public void Constructor_RejectsNullArgument(string name)
    {
        var constructor = typeof(MainViewViewModel.DockFactory).GetConstructors().Single();
        var arguments = constructor.GetParameters().Select(p => p.Name == name ? null :
            p.ParameterType.IsInterface ? Substitute.For([p.ParameterType], []) :
            RuntimeHelpers.GetUninitializedObject(p.ParameterType)).ToArray();

        var construct = () => constructor.Invoke(arguments);

        construct.Should().Throw<TargetInvocationException>().WithInnerException<ArgumentNullException>()
            .Which.ParamName.Should().Be(name);
    }
}
