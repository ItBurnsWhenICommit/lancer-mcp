namespace LancerMcp.Services;

public sealed class SimHashService : IFingerprintService
{
    public const string FingerprintKind = "simhash_v1";

    public FingerprintResult Compute(IEnumerable<string> tokens)
    {
        if (tokens == null)
        {
            return FingerprintResult.FromHash(0UL);
        }

        var weights = new int[64];

        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            var hash = Fnv1a64(token);
            for (var bit = 0; bit < 64; bit++)
            {
                var mask = 1UL << bit;
                weights[bit] += (hash & mask) == 0 ? -1 : 1;
            }
        }

        ulong fingerprint = 0;
        for (var bit = 0; bit < 64; bit++)
        {
            if (weights[bit] > 0)
            {
                fingerprint |= 1UL << bit;
            }
        }

        return FingerprintResult.FromHash(fingerprint);
    }

    private static ulong Fnv1a64(string value)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;

        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= prime;
        }

        return hash;
    }
}

public readonly record struct FingerprintResult(ulong Hash, int Band0, int Band1, int Band2, int Band3)
{
    public static FingerprintResult FromHash(ulong hash)
        => new(
            hash,
            (int)(hash & 0xFFFF),
            (int)((hash >> 16) & 0xFFFF),
            (int)((hash >> 32) & 0xFFFF),
            (int)((hash >> 48) & 0xFFFF));
}
