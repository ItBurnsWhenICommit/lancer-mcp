using System.Text.RegularExpressions;

namespace LancerMcp.Services;

/// <summary>
/// Tokenization helpers for symbol-aware retrieval.
/// </summary>
public static class SymbolTokenization
{
    private static readonly Regex SegmentSplit = new(@"[^A-Za-z0-9]+", RegexOptions.Compiled);
    private static readonly Regex TokenPattern = new(@"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+", RegexOptions.Compiled);

    public static IReadOnlyList<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var tokens = new HashSet<string>(StringComparer.Ordinal);

        foreach (var segment in SegmentSplit.Split(text))
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            foreach (Match match in TokenPattern.Matches(segment))
            {
                var token = match.Value.ToLowerInvariant();
                if (token.Length < 2)
                {
                    continue;
                }

                tokens.Add(token);
            }
        }

        return tokens.ToList();
    }
}
