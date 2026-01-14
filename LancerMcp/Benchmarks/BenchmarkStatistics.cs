namespace LancerMcp.Benchmarks;

public static class BenchmarkStatistics
{
    public static long Percentile(IReadOnlyList<long> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(v => v).ToArray();
        var rank = (int)Math.Ceiling(percentile * sorted.Length);
        var index = Math.Clamp(rank - 1, 0, sorted.Length - 1);
        return sorted[index];
    }
}
