namespace HexIDE.Tests.Infrastructure;

public class WindowsInstallerTests
{
    private static string Script => File.ReadAllText(
        Path.Combine(RepoTree.Root(), "installer", "HexIDE.iss")).ReplaceLineEndings("\n");

    [Fact]
    public void InstallerUsesProtectedPerMachineDirectory()
    {
        Script.Should().Contain("PrivilegesRequired=admin\n");
        Script.Should().Contain("DefaultDirName={autopf}\\HexIDE\n");
        Script.Should().Contain("ArchitecturesInstallIn64BitMode=x64compatible\n");
        Script.Should().NotContain("PrivilegesRequiredOverridesAllowed");
        Script.Should().NotContain("Permissions:");
    }

    [Fact]
    public void InstallerKeepsAddinsAndPublishedPayloadUnderApp()
    {
        Script.Should().Contain("Source: \"{#PublishDir}\\*\"; DestDir: \"{app}\"; Flags: ignoreversion recursesubdirs createallsubdirs");
        Script.Should().Contain("Name: \"{app}\\addins\"");
        Script.Should().Contain("Name: \"{group}\\HexIDE\"; Filename: \"{app}\\HexIDE.exe\"");
        Script.Should().Contain("Uninstallable=yes\n");
        Script.Should().NotContain("[Registry]");
    }

    [Fact]
    public void WindowsX64BuildPublishesSelfContainedInstallerUnderAFixedName()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoTree.Root(), ".github", "workflows", "publish.yml"))
            .ReplaceLineEndings("\n");
        workflow.Should().Contain("- name: Build Windows installer\n        if: matrix.rid == 'win-x64'");
        workflow.Should().Contain("/DAppVersion=$version");
        workflow.Should().Contain("/DPublishDir=$((Resolve-Path 'bin/sc').Path)");
        workflow.Should().Contain("./IDE/bin/hexide-windows-amd64-setup.exe");
        Script.Should().Contain("AppVersion={#AppVersion}\n");
        // Fixed, like every other asset: hexide.io links downloads through releases/latest/download/<name>,
        // which cannot reach a name with the version in it.
        Script.Should().Contain("OutputBaseFilename=hexide-windows-amd64-setup\n");
    }

    [Fact]
    public void CompilerDownloadIsPinnedAndVerifiedBeforeExecution()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoTree.Root(), ".github", "workflows", "publish.yml"))
            .ReplaceLineEndings("\n");
        var step = workflow.Split("- name: Build Windows installer")[1].Split("- uses: actions/upload-artifact")[0];
        step.Should().Contain("is-6_7_3/innosetup-6.7.3.exe");
        step.Should().NotContain("Test-Path $compiler");
        step.Should().Contain("9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732");
        step.Should().Contain("(Get-Item $installer).Length -ne 10592232");
        step.Should().Contain("Get-FileHash $installer -Algorithm SHA256");
        step.IndexOf("SHA-256 mismatch", StringComparison.Ordinal).Should()
            .BeLessThan(step.IndexOf("Start-Process $installer", StringComparison.Ordinal));
    }

    [Fact]
    public void CompilerInstallerRequiresValidPublisherSignatureBeforeExecution()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoTree.Root(), ".github", "workflows", "publish.yml"))
            .ReplaceLineEndings("\n");
        var step = workflow.Split("- name: Build Windows installer")[1].Split("- uses: actions/upload-artifact")[0];
        step.Should().Contain("Get-AuthenticodeSignature $installer");
        step.Should().Contain("$signature.Status -ne 'Valid'");
        step.Should().Contain("$signature.SignerCertificate.Subject -notmatch '(?:^|,\\s*)O=Pyrsys B\\.V\\.(?:,|$)'");
        step.IndexOf("Authenticode verification failed", StringComparison.Ordinal).Should()
            .BeLessThan(step.IndexOf("Start-Process $installer", StringComparison.Ordinal));
    }

    [Fact]
    public void InstalledCompilerVersionIsCheckedBeforeCompilation()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoTree.Root(), ".github", "workflows", "publish.yml"))
            .ReplaceLineEndings("\n");
        var step = workflow.Split("- name: Build Windows installer")[1].Split("- uses: actions/upload-artifact")[0];
        // ISCC.exe's version resource reads 0.0.0.0 (measured on 6.7.3), so a check of it would fail every build.
        step.Should().NotContain("VersionInfo");
        step.Should().Contain("Uninstall");
        step.Should().Contain("Inno Setup 6_is1').DisplayVersion");
        step.Should().Contain("$installed -ne '6.7.3'");
        step.IndexOf("Unexpected Inno Setup version installed", StringComparison.Ordinal).Should()
            .BeLessThan(step.IndexOf("& $compiler \"/DAppVersion", StringComparison.Ordinal));
    }
}
