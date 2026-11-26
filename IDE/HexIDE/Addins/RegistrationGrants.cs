namespace HexIDE.Addins;

/// <summary>
/// What one owner may register through the host's add-in registrations that change what the IDE shows: its resources
/// (#5396), its language packs (#5394), what draws the designer's controls (#5591), components beyond a plain control
/// (#5594), the type-name namespaces and control libraries it keeps (#5599), and the project types it makes programs of
/// (#5621). Kept in HexIDE's own source, so a package can't grant itself anything.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deny by default.</b> An owner with no grant registers none of these, and a grant covers only the families it names.
/// Nothing a grant names is what the IDE or another owner already holds: that's still refused at registration.
/// </para>
/// <para>
/// <b>Never what the trust and consent dialogs read.</b> <c>RegistrationGrantsTests</c> reads those dialogs' resource keys
/// from their markup and fails if any grant's family matches one.
/// </para>
/// </remarks>
/// <param name="OwnerId">The owner's id.</param>
/// <param name="Composed">
/// Whether the owner is one HexIDE.Desktop composes at startup (<see cref="AddinOrigin.IsComposed"/>), rather than a
/// first-party signed package. Phase 3 of #3411 switches the provider's grant to a first-party signed package with its id,
/// and the composed origin goes.
/// </param>
internal sealed record RegistrationGrant(string OwnerId, bool Composed)
{
    /// <summary>The assemblies whose types it may key a resource on, such as its own controls' themes.</summary>
    public IReadOnlyList<string> ResourceTypeAssemblies { get; init; } = [];

    /// <summary>The string keys it may add as resources: whole keys, or prefixes that end in <c>*</c>.</summary>
    public IReadOnlyList<string> ResourceKeys { get; init; } = [];

    /// <summary>Whether <paramref name="key"/> is a string key this grant covers.</summary>
    public bool CoversResourceKey(string key) => ResourceKeys.Any(family => Matches(family, key));

    /// <summary>
    /// The text keys its language packs may supply (#5394): whole keys, or prefixes that end in <c>*</c>. Each is a
    /// <c>Str.</c> key under an area of the IDE's text it alone holds.
    /// </summary>
    public IReadOnlyList<string> LanguagePackKeys { get; init; } = [];

    /// <summary>Whether <paramref name="key"/> is a text key this grant's language packs may supply.</summary>
    public bool CoversLanguagePackKey(string key) => LanguagePackKeys.Any(family => Matches(family, key));

    /// <summary>
    /// Whether it may register what draws the designer's controls (#5591), which is given a place in the designer's tree.
    /// </summary>
    public bool DesignerPictures { get; init; }

    /// <summary>
    /// Whether it may register VB6's scale rules at design time (#5758), whose answers decide the positions the Properties
    /// window and automation write into a form, and the units the designer moves a Line in.
    /// </summary>
    public bool DesignPositions { get; init; }

    /// <summary>
    /// Whether it may register a language's editing helpers (#5403), which the code window calls on every edit: which lines an
    /// <c>#If</c> leaves out, where the parentheses are, and what a module declares.
    /// </summary>
    public bool LanguageEditing { get; init; }

    /// <summary>
    /// Whether it may register the engine that runs projects in the IDE (#5616), which runs the developer's code in the IDE's
    /// process and reaches the debugger.
    /// </summary>
    public bool RunEngine { get; init; }

    /// <summary>
    /// The project types it reserves (#5625): an identity, the project files only it may register, and the member formats
    /// its type carries. Whoever else registers those files is refused, whether it registers before this owner or after.
    /// </summary>
    public IReadOnlyList<ReservedProjectType> ProjectTypes { get; init; } = [];

    /// <summary>
    /// Whether it may register a component beyond a plain control (#5594): one whose role isn't
    /// <see cref="HexIDE.Runtime.Components.ComponentRole.Control"/>, such as a form, or that's a container. The designer
    /// gives such a class a place no plain control has, as a form's root or as other controls' parent.
    /// </summary>
    public bool Components { get; init; }

    /// <summary>
    /// The type-name namespaces it keeps (#5599): <c>VB</c> in <c>VB.CommandButton</c>, a control library's name in
    /// <c>MSComctlLib.ProgressBar</c>. A namespace a grant names is its owner's whether or not that owner has registered
    /// anything yet, so any other owner is refused a component in it, and only this owner may register a control library
    /// under it. A library's controls never pass the component checks, so this is what lets a library offer a container
    /// such as the SSTab.
    /// </summary>
    public IReadOnlyList<string> TypeNamespaces { get; init; } = [];

    /// <summary>Whether <paramref name="ns"/> is a type-name namespace this grant keeps, compared without regard to case.</summary>
    public bool KeepsTypeNamespace(string ns) => TypeNamespaces.Contains(ns, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The project types it may register a maker for (#5621): what File > Make, Make Project Group and the type's own
    /// toolchain run for a project of that type. Each is a type id, as a project's <c>ContributedBy</c> names it.
    /// </summary>
    public IReadOnlyList<string> MakesProjectTypes { get; init; } = [];

    /// <summary>Whether it may register a maker for projects of <paramref name="type"/>.</summary>
    public bool MakesProjectType(string type) => MakesProjectTypes.Contains(type, StringComparer.Ordinal);

    /// <summary>Whether a type declared in <paramref name="assembly"/> may key one of its resources.</summary>
    public bool CoversResourceType(string? assembly) =>
        assembly is not null && ResourceTypeAssemblies.Contains(assembly, StringComparer.Ordinal);

    /// <summary>Whether <paramref name="family"/>, a whole key or a prefix ending in <c>*</c>, covers <paramref name="key"/>.</summary>
    internal static bool Matches(string family, string key) =>
        family.EndsWith('*') ? key.StartsWith(family[..^1], StringComparison.Ordinal) : string.Equals(family, key, StringComparison.Ordinal);
}

/// <summary>The grants HexIDE gives (#5396). See <see cref="RegistrationGrant"/>.</summary>
internal static class RegistrationGrants
{
    /// <summary>The Basic provider's id, as HexIDE.Desktop composes it.</summary>
    internal const string BasicProvider = "hexide.basic";

    internal static IReadOnlyList<RegistrationGrant> All { get; } =
    [
        // Its controls' themes (BuiltinControls/VBTimer.axaml, VBLabel.axaml and Resources.axaml), keyed on its own types,
        // and the themes a control with Appearance Flat swaps to, FlatVBCheckBox and FlatVBOptionButton today (#918 adds
        // FlatVBTextBox).
        new(BasicProvider, Composed: true)
        {
            ResourceTypeAssemblies = ["HexIDE.Runtime"],
            ResourceKeys = ["FlatVB*"],
            // Its text (#5394, #5395): VB6's property descriptions, project type names and load messages, Make and Make
            // with VB6, and the one runner message only programs it runs raise. The IDE's own keys among these stay refused.
            LanguagePackKeys =
            [
                "Str.PropDesc.*", "Str.ProjectType.*", "Str.ProjectLoad.*", "Str.MakeProject.*", "Str.Vb6Toolchain.*",
                "Str.ProjectRunner.NotImplementedYet", "Str.Standalone.WaylandUnavailableEnvironment",
            ],
            // Its controls draw themselves in the designer (#5591).
            DesignerPictures = true,
            // VB6's scale rules for its forms' positions and its Lines' units (#5758).
            DesignPositions = true,
            // The VB family's editing helpers: its interpreter's #If reader and lexer (#5403).
            LanguageEditing = true,
            // Its interpreter runs projects in the IDE (#5616).
            RunEngine = true,
            // VB6's own project type, under VB6's identity, its files reserved to it (#5625).
            ProjectTypes = [HexIDE.Projects.Vb6ProjectType.Reserved],
            // Its Form, and its Frame and PictureBox, which hold other controls (#5594).
            Components = true,
            // VB6's own type names, and its control libraries', the in-box OCXs it implements (#5599).
            TypeNamespaces =
                ["VB", "MSComctlLib", "ComctlLib", "MSFlexGridLib", "TabDlg", "MSComDlg", "MSComCtl2", "MSMask", "RichTextLib"],
            // File > Make and VB6's own toolchain, for VB6's projects (#5621).
            MakesProjectTypes = [HexIDE.Projects.Vb6ProjectType.Id],
        },
    ];

    /// <summary>The grant <paramref name="origin"/> holds, or null when it holds none.</summary>
    /// <remarks>
    /// A grant that isn't for a composed owner is held only by a package signed by the first-party key, so no other package
    /// can name itself into one. None exists until phase 3.
    /// </remarks>
    internal static RegistrationGrant? For(AddinOrigin origin) => For(origin, All);

    /// <summary>The grant <paramref name="origin"/> holds among <paramref name="grants"/>, or null.</summary>
    internal static RegistrationGrant? For(AddinOrigin origin, IReadOnlyList<RegistrationGrant> grants) =>
        grants.FirstOrDefault(grant => (grant.Composed ? origin.IsComposed : origin.IsFirstParty && !origin.IsComposed)
            && !origin.IsDeveloperUnsigned
            && string.Equals(grant.OwnerId, origin.Id, StringComparison.Ordinal));
}

/// <summary>
/// A project type a grant reserves to its owner (#5625). The owner's type takes this identity, <see cref="Id"/> and
/// <see cref="Title"/>, only when it registers exactly these files; the alias is identity only, and who is trusted stays the
/// owner. It carries the formats of the members its projects hold, which an add-in's own project type may borrow.
/// </summary>
/// <param name="Id">What its projects carry as their contributor, whoever registers it.</param>
/// <param name="Title">What its projects and refusals call it.</param>
/// <param name="FileNames">Its project files.</param>
/// <param name="GroupFileNames">Its group files.</param>
/// <param name="MemberKinds">The kinds of module its format reads for another type's projects.</param>
/// <param name="Forms">Whether its format reads their forms too.</param>
/// <param name="FormFormat">The id another type names to read its forms with this format.</param>
internal sealed record ReservedProjectType(string Id, string Title, IReadOnlyList<string> FileNames,
    IReadOnlyList<string> GroupFileNames, IReadOnlySet<HexIDE.Runtime.ProjectElements.ModuleKind> MemberKinds, bool Forms,
    string? FormFormat)
{
    /// <summary>Every project and group file it reserves.</summary>
    internal IEnumerable<string> Patterns => FileNames.Concat(GroupFileNames);
}
