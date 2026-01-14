using Microsoft.Extensions.Logging.Abstractions;
using LancerMcp.Models;
using LancerMcp.Services;

namespace LancerMcp.Tests;

public sealed class RoslynQualifiedNameTests
{
    [Fact]
    public async Task RoslynParserService_UsesCanonicalQualifiedNames_ForMethodsAndContainment()
    {
        var service = new RoslynParserService(NullLogger<RoslynParserService>.Instance);

        var code = @"
namespace Acme.Tools
{
    public class Outer
    {
        public class Inner
        {
            public void M(int value) { }
            public void M(string value) { }
            public T Echo<T>(T value) { return value; }
        }
    }
}";

        var result = await service.ParseFileAsync(
            "test-repo",
            "main",
            "abc123",
            "Outer.cs",
            code);

        Assert.True(result.Success);

        var outer = result.Symbols.Single(s => s.Kind == SymbolKind.Class && s.Name == "Outer");
        var inner = result.Symbols.Single(s => s.Kind == SymbolKind.Class && s.Name == "Inner");
        Assert.Equal(outer.Id, inner.ParentSymbolId);

        var methodInt = result.Symbols.Single(s => s.Kind == SymbolKind.Method && s.Name == "M" && s.Signature != null && s.Signature.Contains("int"));
        var methodString = result.Symbols.Single(s => s.Kind == SymbolKind.Method && s.Name == "M" && s.Signature != null && s.Signature.Contains("string"));
        var generic = result.Symbols.Single(s => s.Kind == SymbolKind.Method && s.Name == "Echo");

        Assert.Equal("Acme.Tools.Outer.Inner.M(System.Int32)", methodInt.QualifiedName);
        Assert.Equal("Acme.Tools.Outer.Inner.M(System.String)", methodString.QualifiedName);
        Assert.Equal("Acme.Tools.Outer.Inner.Echo<T>(T)", generic.QualifiedName);
        Assert.Equal(inner.Id, methodInt.ParentSymbolId);
        Assert.Equal(inner.Id, generic.ParentSymbolId);
    }
}
