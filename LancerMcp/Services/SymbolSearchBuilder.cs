using LancerMcp.Models;

namespace LancerMcp.Services;

/// <summary>
/// Builds symbol search entries for sparse retrieval.
/// </summary>
public static class SymbolSearchBuilder
{
    public static IReadOnlyList<SymbolSearchEntry> BuildEntries(ParsedFile parsedFile)
    {
        var entries = new List<SymbolSearchEntry>();

        foreach (var symbol in parsedFile.Symbols)
        {
            entries.Add(new SymbolSearchEntry
            {
                SymbolId = symbol.Id,
                RepositoryName = symbol.RepositoryName,
                BranchName = symbol.BranchName,
                CommitSha = symbol.CommitSha,
                FilePath = symbol.FilePath,
                Language = symbol.Language,
                Kind = symbol.Kind,
                NameTokens = SymbolTokenization.Tokenize(symbol.Name),
                QualifiedTokens = SymbolTokenization.Tokenize(symbol.QualifiedName ?? string.Empty),
                SignatureTokens = SymbolTokenization.Tokenize(symbol.Signature ?? string.Empty),
                DocumentationTokens = SymbolTokenization.Tokenize(symbol.Documentation ?? string.Empty),
                LiteralTokens = symbol.LiteralTokens ?? Array.Empty<string>(),
                Snippet = ExtractSnippet(parsedFile.SourceText, symbol.StartLine, symbol.EndLine)
            });
        }

        return entries;
    }

    private static string? ExtractSnippet(string? sourceText, int startLine, int endLine)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return null;
        }

        var lines = sourceText.Split('\n');
        if (lines.Length == 0)
        {
            return null;
        }

        var clampedStart = Math.Max(1, startLine);
        var clampedEnd = Math.Max(clampedStart, endLine);

        var startIndex = Math.Min(lines.Length - 1, clampedStart - 1);
        var endIndex = Math.Min(lines.Length - 1, clampedEnd - 1);

        if (endIndex < startIndex)
        {
            return null;
        }

        return string.Join('\n', lines[startIndex..(endIndex + 1)]);
    }
}
