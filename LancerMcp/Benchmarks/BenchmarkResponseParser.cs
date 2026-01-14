using System.Text;
using System.Text.Json;

namespace LancerMcp.Benchmarks;

public static class BenchmarkResponseParser
{
    public static BenchmarkQueryExecution Parse(string json, string? query = null, long elapsedMs = 0)
    {
        using var document = JsonDocument.Parse(json);
        var symbols = new List<string>();
        var snippetChars = 0;

        if (document.RootElement.TryGetProperty("results", out var results))
        {
            foreach (var result in results.EnumerateArray())
            {
                if (result.TryGetProperty("symbol", out var symbol))
                {
                    var value = symbol.GetString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        symbols.Add(value);
                    }
                }

                if (result.TryGetProperty("content", out var content))
                {
                    snippetChars += content.GetString()?.Length ?? 0;
                }
            }
        }

        var jsonBytes = Encoding.UTF8.GetByteCount(json);
        return new BenchmarkQueryExecution(query ?? string.Empty, elapsedMs, jsonBytes, snippetChars, symbols);
    }
}
