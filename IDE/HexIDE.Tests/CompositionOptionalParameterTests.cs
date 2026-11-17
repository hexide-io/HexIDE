using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace HexIDE.Tests;

/// <summary>
/// Pure.DI tries each optional parameter of a constructor it composes both ways, so every one doubles its search. The IDE's
/// graph reached the search limit (DIE031) with no single change at fault: two branches, each fine alone, failed together
/// with no conflict in the text (#4204). So nothing Pure.DI composes itself has one. A type that wants optional parameters,
/// for its tests, is built in a factory binding instead, where the C# compiler, not Pure.DI, fills the defaults.
/// </summary>
public class CompositionOptionalParameterTests
{
    [Fact]
    public void No_constructor_Pure_DI_composes_itself_has_an_optional_parameter()
    {
        var setup = File.ReadAllText(Path.Join(SourceDirectory(), "..", "HexIDE", "DISetup.cs"));
        var composition = typeof(DISetup).Assembly;
        var types = composition.GetReferencedAssemblies()
            .Where(name => name.Name is { } simple && simple.StartsWith("HexIDE", StringComparison.Ordinal))
            .Select(Assembly.Load)
            .Append(composition)
            .SelectMany(assembly => assembly.GetTypes())
            .ToList();

        // Built in a factory binding, or bound to one: Pure.DI reads the factory's injections, never these constructors.
        var built = Names(setup, @"\bnew\s+([\w.]+)\s*\(").Concat(Names(setup, @"\.Bind<([\w.,\s]+)>\(\)\s*(?:\.As\(\w+\)\s*)?\.To\(\s*\w+\s*=>"))
            .SelectMany(name => Resolve(types, name)).ToHashSet();
        var bound = Names(setup, @"\.Bind<([\w.,\s]+)>\(").SelectMany(name => Resolve(types, name)).ToHashSet();
        // What it binds and roots, and what a factory binding injects: Pure.DI composes an injected class nothing binds itself.
        var composed = Names(setup, @"\.To<([\w.]+)>\(\)").Concat(Names(setup, @"\.Root<([\w.]+)>"))
            .Concat(Names(setup, @"ctx\.Inject<(?:Func<)?([\w.]+)>"))
            .SelectMany(name => Resolve(types, name))
            .Where(type => !built.Contains(type))
            .ToList();
        composed.Should().NotBeEmpty("the premise: DISetup's bindings are read");

        // And what their constructors take that nothing binds: Pure.DI composes a concrete class like that itself.
        var seen = new HashSet<Type>();
        var offenders = new List<string>();
        var queue = new Queue<Type>(composed.Where(type => type is { IsClass: true, IsAbstract: false }));
        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            if (!seen.Add(type))
                continue;
            foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                         .Where(constructor => !constructor.IsPrivate))
            {
                offenders.AddRange(constructor.GetParameters().Where(parameter => parameter.IsOptional)
                    .Select(parameter => $"{type.FullName}({parameter.ParameterType.Name} {parameter.Name})"));
                foreach (var parameter in constructor.GetParameters())
                {
                    var needed = Unwrapped(parameter.ParameterType);
                    if (needed is { IsClass: true, IsAbstract: false } && types.Contains(needed) && !built.Contains(needed)
                        && !bound.Contains(needed))
                        queue.Enqueue(needed);
                }
            }
        }

        seen.Should().Contain(typeof(MainViewViewModel), "the premise: the root view model is read as one Pure.DI composes");
        string.Join(", ", offenders).Should().BeEmpty("each optional parameter doubles Pure.DI's search, which is at its limit (#4204); make it "
            + "required, or build the type in a factory binding in DISetup");
    }

    private static IEnumerable<string> Names(string source, string pattern) =>
        Regex.Matches(source, pattern).SelectMany(match => match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries));

    private static IEnumerable<Type> Resolve(List<Type> types, string name) =>
        types.Where(type => type.FullName is { } full && !type.IsGenericTypeDefinition
            && (full.Replace('+', '.') == name || full.Replace('+', '.').EndsWith("." + name, StringComparison.Ordinal)));

    private static Type Unwrapped(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() is var definition
            && (definition == typeof(Func<>) || definition == typeof(Lazy<>)) ? type.GetGenericArguments()[0] : type;

    private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
