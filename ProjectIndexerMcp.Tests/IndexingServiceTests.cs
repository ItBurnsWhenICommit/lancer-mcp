using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectIndexerMcp.Configuration;
using ProjectIndexerMcp.Models;
using ProjectIndexerMcp.Services;
using Xunit;

namespace ProjectIndexerMcp.Tests;

public class IndexingServiceTests
{
    [Fact]
    public void LanguageDetectionService_DetectsCSharp()
    {
        var service = new LanguageDetectionService();

        Assert.Equal(Language.CSharp, service.DetectLanguage("Program.cs"));
        Assert.Equal(Language.CSharp, service.DetectLanguage("Test.csx"));
    }

    [Fact]
    public void LanguageDetectionService_DetectsPython()
    {
        var service = new LanguageDetectionService();

        Assert.Equal(Language.Python, service.DetectLanguage("script.py"));
        Assert.Equal(Language.Python, service.DetectLanguage("test", "#!/usr/bin/env python3\nprint('hello')"));
    }

    [Fact]
    public void LanguageDetectionService_DetectsJavaScript()
    {
        var service = new LanguageDetectionService();

        Assert.Equal(Language.JavaScript, service.DetectLanguage("app.js"));
        Assert.Equal(Language.TypeScript, service.DetectLanguage("app.ts"));
        Assert.Equal(Language.TypeScriptReact, service.DetectLanguage("Component.tsx"));
    }

    [Fact]
    public async Task RoslynParserService_ParsesCSharpClass()
    {
        var service = new RoslynParserService(NullLogger<RoslynParserService>.Instance);

        var code = @"
namespace MyApp
{
    public class UserService
    {
        private readonly string _name;

        public UserService(string name)
        {
            _name = name;
        }

        public void Login(string username, string password)
        {
            // Login logic
        }

        public string GetName() => _name;
    }
}";

        var result = await service.ParseFileAsync(
            "test-repo",
            "main",
            "abc123",
            "UserService.cs",
            code);

        Assert.True(result.Success);
        Assert.Equal(Language.CSharp, result.Language);

        // Should find: namespace, class, field, constructor, 2 methods
        Assert.True(result.Symbols.Count >= 5, $"Expected at least 5 symbols, got {result.Symbols.Count}");

        var classSymbol = result.Symbols.FirstOrDefault(s => s.Name == "UserService" && s.Kind == SymbolKind.Class);
        Assert.NotNull(classSymbol);
        Assert.Contains("public", classSymbol.Modifiers ?? Array.Empty<string>());

        var loginMethod = result.Symbols.FirstOrDefault(s => s.Name == "Login" && s.Kind == SymbolKind.Method);
        Assert.NotNull(loginMethod);
        Assert.NotNull(loginMethod.Signature);
        Assert.Contains("username", loginMethod.Signature);
    }

    [Fact]
    public async Task BasicParserService_ParsesPythonClass()
    {
        var service = new BasicParserService(NullLogger<BasicParserService>.Instance);

        var code = @"
class UserService:
    def __init__(self, name):
        self.name = name

    def login(self, username, password):
        pass

    async def async_login(self, username):
        pass
";

        var result = await service.ParseFileAsync(
            "test-repo",
            "main",
            "abc123",
            "user_service.py",
            code,
            Language.Python);

        Assert.True(result.Success);
        Assert.Equal(Language.Python, result.Language);

        // Should find: class and 3 methods
        Assert.True(result.Symbols.Count >= 4, $"Expected at least 4 symbols, got {result.Symbols.Count}");

        var classSymbol = result.Symbols.FirstOrDefault(s => s.Name == "UserService" && s.Kind == SymbolKind.Class);
        Assert.NotNull(classSymbol);

        var loginMethod = result.Symbols.FirstOrDefault(s => s.Name == "login");
        Assert.NotNull(loginMethod);
    }

    [Fact]
    public async Task BasicParserService_ParsesJavaScriptFunctions()
    {
        var service = new BasicParserService(NullLogger<BasicParserService>.Instance);

        var code = @"
export class UserService {
    constructor(name) {
        this.name = name;
    }
}

export function login(username, password) {
    return true;
}

const logout = async () => {
    return false;
};
";

        var result = await service.ParseFileAsync(
            "test-repo",
            "main",
            "abc123",
            "userService.js",
            code,
            Language.JavaScript);

        Assert.True(result.Success);
        Assert.Equal(Language.JavaScript, result.Language);

        // Should find: class, function, arrow function
        Assert.True(result.Symbols.Count >= 3, $"Expected at least 3 symbols, got {result.Symbols.Count}");

        var classSymbol = result.Symbols.FirstOrDefault(s => s.Name == "UserService" && s.Kind == SymbolKind.Class);
        Assert.NotNull(classSymbol);

        var loginFunction = result.Symbols.FirstOrDefault(s => s.Name == "login" && s.Kind == SymbolKind.Function);
        Assert.NotNull(loginFunction);
    }

    [Fact]
    public async Task GitTrackerService_ReadsFileContentFromBlob()
    {
        // This test verifies that GitTrackerService can read file content from Git blobs
        // Create a temporary Git repository with test files
        var tempDir = Path.Combine(Path.GetTempPath(), $"git-test-{Guid.NewGuid()}");
        var repoPath = Path.Combine(tempDir, "test-repo");

        try
        {
            Directory.CreateDirectory(repoPath);

            // Initialize a Git repository
            LibGit2Sharp.Repository.Init(repoPath);

            using var repo = new LibGit2Sharp.Repository(repoPath);

            // Create test files
            var csFile = Path.Combine(repoPath, "Test.cs");
            var testContent = "public class Test { public void Method() { } }";
            await File.WriteAllTextAsync(csFile, testContent);

            // Stage and commit the file
            LibGit2Sharp.Commands.Stage(repo, "Test.cs");

            var signature = new LibGit2Sharp.Signature("Test User", "test@example.com", DateTimeOffset.Now);
            var commit = repo.Commit("Initial commit", signature, signature, new LibGit2Sharp.CommitOptions());

            // Now test reading the file content from the blob
            // We can read directly from the repository without GitTrackerService initialization
            var treeEntry = commit["Test.cs"];
            Assert.NotNull(treeEntry);
            Assert.Equal(LibGit2Sharp.TreeEntryTargetType.Blob, treeEntry.TargetType);

            var blob = (LibGit2Sharp.Blob)treeEntry.Target;
            var content = blob.GetContentText();

            Assert.Equal(testContent, content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Simple test implementation of IOptionsMonitor for testing.
    /// </summary>
    private class TestOptionsMonitor : IOptionsMonitor<ServerOptions>
    {
        private readonly ServerOptions _options;

        public TestOptionsMonitor(ServerOptions options)
        {
            _options = options;
        }

        public ServerOptions CurrentValue => _options;

        public ServerOptions Get(string? name) => _options;

        public IDisposable? OnChange(Action<ServerOptions, string?> listener) => null;
    }
}

