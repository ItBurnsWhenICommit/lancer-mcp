using System.Text.Json;

namespace LancerMcp.Benchmarks;

public sealed record BenchmarkQuerySpec(string Query, List<string> ExpectedSymbols);

public sealed class BenchmarkQuerySet
{
    public required string Name { get; init; }
    public int TopK { get; init; } = 5;
    public required List<BenchmarkQuerySpec> Queries { get; init; }

    public static BenchmarkQuerySet FromJson(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var set = JsonSerializer.Deserialize<BenchmarkQuerySet>(json, options);
        if (set == null)
        {
            throw new InvalidOperationException("Invalid benchmark query set JSON");
        }
        return set;
    }

    public static BenchmarkQuerySet FromFile(string path)
    {
        var json = File.ReadAllText(path);
        return FromJson(json);
    }
}
