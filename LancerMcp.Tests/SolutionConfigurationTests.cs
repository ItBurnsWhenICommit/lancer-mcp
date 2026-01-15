using Xunit;

namespace LancerMcp.Tests;

public sealed class SolutionConfigurationTests
{
    [Fact]
    public void Solution_IncludesIntegrationTestsProject()
    {
        var root = FindRepoRoot();
        var slnPath = Path.Combine(root, "lancer-mcp.sln");
        var sln = File.ReadAllText(slnPath);

        Assert.Contains(
            "tests\\LancerMcp.IntegrationTests\\LancerMcp.IntegrationTests.csproj",
            sln,
            StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "lancer-mcp.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("lancer-mcp.sln not found");
    }
}
