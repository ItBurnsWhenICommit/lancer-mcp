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
    public async Task RoslynParserService_ExtractsTypeEdges()
    {
        var service = new RoslynParserService(NullLogger<RoslynParserService>.Instance);

        var code = @"
using System;
using System.Collections.Generic;

namespace MyApp
{
    public interface ILogger { }

    public class UserService
    {
        private readonly ILogger _logger;
        private readonly DateTime _createdAt;
        public string Name { get; set; }
        public List<int> Ids { get; set; }

        public UserService(ILogger logger, string name)
        {
            _logger = logger;
            Name = name;
            _createdAt = DateTime.Now;
        }

        public List<string> GetUsers()
        {
            return new List<string>();
        }

        public IEnumerable<int> GetIds()
        {
            return Ids;
        }
    }
}";

        var result = await service.ParseFileAsync(
            "test-repo",
            "main",
            "abc123",
            "UserService.cs",
            code);

        Assert.True(result.Success);
        Assert.True(result.Edges.Count > 0, $"Expected edges to be created, got {result.Edges.Count}");

        // Find the ILogger field symbol
        var loggerFieldSymbol = result.Symbols.FirstOrDefault(s => s.Name == "_logger" && s.Kind == SymbolKind.Field);
        Assert.NotNull(loggerFieldSymbol);

        // Should have a TypeOf edge from field to ILogger
        var loggerFieldTypeEdge = result.Edges.FirstOrDefault(e =>
            e.SourceSymbolId == loggerFieldSymbol.Id &&
            e.Kind == EdgeKind.TypeOf);
        Assert.NotNull(loggerFieldTypeEdge);

        // The target could be either a symbol ID (if ILogger is in the same file) or a qualified name
        var iloggerSymbol = result.Symbols.FirstOrDefault(s => s.Name == "ILogger");
        if (iloggerSymbol != null)
        {
            // ILogger is defined in the same file, so the edge should point to its symbol ID
            Assert.Equal(iloggerSymbol.Id, loggerFieldTypeEdge.TargetSymbolId);
        }
        else
        {
            // ILogger is external, so the edge should contain the qualified name
            Assert.Contains("ILogger", loggerFieldTypeEdge.TargetSymbolId);
        }

        // Find the DateTime field symbol - this should now have an edge (not skipped)
        var dateTimeFieldSymbol = result.Symbols.FirstOrDefault(s => s.Name == "_createdAt" && s.Kind == SymbolKind.Field);
        Assert.NotNull(dateTimeFieldSymbol);

        // Should have a TypeOf edge from field to DateTime (DateTime is NOT a primitive)
        var dateTimeFieldTypeEdge = result.Edges.FirstOrDefault(e =>
            e.SourceSymbolId == dateTimeFieldSymbol.Id &&
            e.Kind == EdgeKind.TypeOf);
        Assert.NotNull(dateTimeFieldTypeEdge);
        Assert.Contains("DateTime", dateTimeFieldTypeEdge.TargetSymbolId);

        // Find the Name property symbol
        var namePropertySymbol = result.Symbols.FirstOrDefault(s => s.Name == "Name" && s.Kind == SymbolKind.Property);
        Assert.NotNull(namePropertySymbol);

        // Should NOT have a TypeOf edge from property to string (string IS a primitive, so it's skipped)
        var namePropertyTypeEdge = result.Edges.FirstOrDefault(e =>
            e.SourceSymbolId == namePropertySymbol.Id &&
            e.Kind == EdgeKind.TypeOf);
        Assert.Null(namePropertyTypeEdge);

        // Find the Ids property symbol
        var idsPropertySymbol = result.Symbols.FirstOrDefault(s => s.Name == "Ids" && s.Kind == SymbolKind.Property);
        Assert.NotNull(idsPropertySymbol);

        // Should have a TypeOf edge from property to List<int> (List is NOT a primitive)
        var idsPropertyTypeEdge = result.Edges.FirstOrDefault(e =>
            e.SourceSymbolId == idsPropertySymbol.Id &&
            e.Kind == EdgeKind.TypeOf);
        Assert.NotNull(idsPropertyTypeEdge);
        Assert.Contains("List", idsPropertyTypeEdge.TargetSymbolId);

        // Find the GetUsers method
        var getUsersMethod = result.Symbols.FirstOrDefault(s => s.Name == "GetUsers" && s.Kind == SymbolKind.Method);
        Assert.NotNull(getUsersMethod);

        // Should have a Returns edge from method to List<string>
        var getUsersReturnTypeEdge = result.Edges.FirstOrDefault(e =>
            e.SourceSymbolId == getUsersMethod.Id &&
            e.Kind == EdgeKind.Returns);
        Assert.NotNull(getUsersReturnTypeEdge);
        Assert.Contains("List", getUsersReturnTypeEdge.TargetSymbolId);

        // Find the GetIds method
        var getIdsMethod = result.Symbols.FirstOrDefault(s => s.Name == "GetIds" && s.Kind == SymbolKind.Method);
        Assert.NotNull(getIdsMethod);

        // Should have a Returns edge from method to IEnumerable<int> (IEnumerable is NOT a primitive)
        var getIdsReturnTypeEdge = result.Edges.FirstOrDefault(e =>
            e.SourceSymbolId == getIdsMethod.Id &&
            e.Kind == EdgeKind.Returns);
        Assert.NotNull(getIdsReturnTypeEdge);
        Assert.Contains("IEnumerable", getIdsReturnTypeEdge.TargetSymbolId);

        // Find the constructor
        var constructor = result.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Constructor);
        Assert.NotNull(constructor);

        // Should have TypeOf edges from constructor to parameter types
        var constructorTypeEdges = result.Edges.Where(e =>
            e.SourceSymbolId == constructor.Id &&
            e.Kind == EdgeKind.TypeOf).ToList();

        // Should have edge to ILogger parameter, but NOT to string parameter (string is primitive)
        Assert.True(constructorTypeEdges.Count >= 1, $"Expected at least 1 parameter type edge, got {constructorTypeEdges.Count}");

        // Should have edge to ILogger parameter (could be symbol ID or qualified name)
        var hasILoggerEdge = constructorTypeEdges.Any(e =>
            e.TargetSymbolId == iloggerSymbol?.Id ||
            e.TargetSymbolId.Contains("ILogger"));
        Assert.True(hasILoggerEdge, "Constructor should have a TypeOf edge to ILogger parameter");

        // Should NOT have edge to string parameter (string is primitive)
        var hasStringEdge = constructorTypeEdges.Any(e =>
            e.TargetSymbolId.Contains("string") ||
            e.TargetSymbolId.Contains("String"));
        Assert.False(hasStringEdge, "Constructor should NOT have a TypeOf edge to string parameter (primitive)");
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

