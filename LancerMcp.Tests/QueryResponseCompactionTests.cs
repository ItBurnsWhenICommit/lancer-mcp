using System.Text;
using System.Text.Json;
using LancerMcp.Models;
using Xunit;

namespace LancerMcp.Tests;

public sealed class QueryResponseCompactionTests
{
    [Fact]
    public void ToOptimizedFormat_EnforcesResultAndPayloadCaps()
    {
        var results = new List<SearchResult>();
        for (var i = 0; i < 25; i++)
        {
            results.Add(new SearchResult
            {
                Id = i.ToString(),
                Type = "code_chunk",
                Repository = "repo",
                Branch = "main",
                FilePath = "src/File.cs",
                Language = Language.CSharp,
                SymbolName = $"Symbol{i}",
                Content = new string('x', 1000),
                StartLine = 1,
                EndLine = 2,
                Score = 0.5f
            });
        }

        var response = new QueryResponse
        {
            Query = "big query",
            Intent = QueryIntent.Search,
            Results = results,
            TotalResults = results.Count,
            ExecutionTimeMs = 1
        };

        var payload = response.ToOptimizedFormat(new QueryResponseCompactionOptions
        {
            MaxResults = 10,
            MaxSnippetChars = 8000,
            MaxJsonBytes = 16384
        });

        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);

        var jsonBytes = Encoding.UTF8.GetByteCount(json);
        Assert.True(jsonBytes <= 16384, $"JSON bytes too large: {jsonBytes}");

        var resultsArray = doc.RootElement.GetProperty("results");
        Assert.True(resultsArray.GetArrayLength() <= 10);

        var snippetTotal = 0;
        foreach (var result in resultsArray.EnumerateArray())
        {
            if (result.TryGetProperty("content", out var content))
            {
                snippetTotal += content.GetString()?.Length ?? 0;
            }
        }

        Assert.True(snippetTotal <= 8000, $"Snippet chars too large: {snippetTotal}");
    }

    [Fact]
    public void ToOptimizedFormat_IncludesErrorMetadata()
    {
        var response = new QueryResponse
        {
            Query = "similar:missing-id",
            Intent = QueryIntent.Similar,
            Results = new List<SearchResult>(),
            TotalResults = 0,
            ExecutionTimeMs = 1,
            Metadata = new Dictionary<string, object>
            {
                ["repository"] = "repo",
                ["branch"] = "main",
                ["errorCode"] = "seed_not_found",
                ["error"] = "Seed symbol not found."
            }
        };

        var payload = response.ToOptimizedFormat();
        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("seed_not_found", doc.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal("Seed symbol not found.", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void ToOptimizedFormat_IncludesQualifiedName()
    {
        var result = new SearchResult
        {
            Id = "1",
            Type = "symbol",
            Repository = "repo",
            Branch = "main",
            FilePath = "src/File.cs",
            Language = Language.CSharp,
            SymbolName = "File",
            QualifiedName = "Repo.File",
            Content = "public class File {}",
            StartLine = 1,
            EndLine = 2,
            Score = 1.0f
        };

        var response = new QueryResponse
        {
            Query = "qualified query",
            Intent = QueryIntent.Search,
            Results = new List<SearchResult> { result },
            TotalResults = 1,
            ExecutionTimeMs = 1,
            Metadata = new Dictionary<string, object>
            {
                ["repository"] = "repo",
                ["branch"] = "main"
            }
        };

        var payload = response.ToOptimizedFormat();
        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);

        var firstResult = doc.RootElement.GetProperty("results")[0];
        Assert.Equal("Repo.File", firstResult.GetProperty("qualified").GetString());
    }
}
