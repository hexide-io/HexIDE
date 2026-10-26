namespace HexIDE.Tests.Infrastructure;

public class OracleMirrorTests
{
    [Theory]
    [InlineData("on:\n  push:\n    branches: [main]\n    paths:\n      - 'docs/vb6-fidelity-oracle/**'")]
    [InlineData("workflow_dispatch:")]
    [InlineData("secrets.VB6_BEHAVIOUR_DEPLOY_KEY")]
    [InlineData("::error::VB6_BEHAVIOUR_DEPLOY_KEY is required")]
    [InlineData("git clone git@github.com:hexide-io/vb6-behaviour.git")]
    [InlineData("rsync -a --delete --exclude=.git/")]
    [InlineData("git config user.name hexide-oracle-mirror")]
    [InlineData("Mirror docs/vb6-fidelity-oracle at hexide-io/HexIDE@")]
    [InlineData("git diff --cached --quiet")]
    public void WorkflowKeepsTheMirrorContract(string contract)
    {
        var path = Path.Combine(RepoTree.Root(), ".github", "workflows", "mirror-oracle.yml");
        File.Exists(path).Should().BeTrue();
        File.ReadAllText(path).ReplaceLineEndings("\n").Should().Contain(contract);
    }

    [Theory]
    [InlineData("LICENSE")]
    [InlineData("LICENSE-CODE")]
    [InlineData("README.md")]
    public void WorkflowCopiesTheMirrorsOwnFiles(string name)
    {
        var root = RepoTree.Root();
        File.Exists(Path.Combine(root, ".github", "oracle-mirror", name)).Should().BeTrue();
        File.ReadAllText(Path.Combine(root, ".github", "workflows", "mirror-oracle.yml"))
            .Should().Contain($"cp \".github/oracle-mirror/{name}\" \"$mirror/{name}\"");
    }
}
