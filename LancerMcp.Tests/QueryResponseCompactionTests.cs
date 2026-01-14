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
}
