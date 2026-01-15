using LancerMcp.Models;

namespace LancerMcp.Services;

/// <summary>
/// Builds symbol fingerprint entries for similarity search.
/// </summary>
public static class SymbolFingerprintBuilder
{
    public static IReadOnlyList<SymbolFingerprintEntry> BuildEntries(
        ParsedFile parsedFile,
        IFingerprintService fingerprintService)
    {
        var entries = new List<SymbolFingerprintEntry>();

        foreach (var symbol in parsedFile.Symbols)
        {
            var snippet = ExtractSnippet(parsedFile.SourceText, symbol.StartLine, symbol.EndLine);
            var tokens = new List<string>();

            tokens.AddRange(SymbolTokenization.Tokenize(symbol.Name));
            tokens.AddRange(SymbolTokenization.Tokenize(symbol.QualifiedName ?? string.Empty));
            tokens.AddRange(SymbolTokenization.Tokenize(symbol.Signature ?? string.Empty));
            tokens.AddRange(SymbolTokenization.Tokenize(symbol.Documentation ?? string.Empty));
            tokens.AddRange(symbol.LiteralTokens ?? Array.Empty<string>());
            if (!string.IsNullOrWhiteSpace(snippet))
            {
                tokens.AddRange(SymbolTokenization.ExtractIdentifierTokens(snippet, 4000, 256));
            }

            var fingerprint = fingerprintService.Compute(tokens);

            entries.Add(new SymbolFingerprintEntry
            {
                SymbolId = symbol.Id,
                RepositoryName = symbol.RepositoryName,
                BranchName = symbol.BranchName,
                CommitSha = symbol.CommitSha,
                FilePath = symbol.FilePath,
                Language = symbol.Language,
                Kind = symbol.Kind,
                FingerprintKind = SimHashService.FingerprintKind,
                Fingerprint = fingerprint.Hash,
                Band0 = fingerprint.Band0,
                Band1 = fingerprint.Band1,
                Band2 = fingerprint.Band2,
                Band3 = fingerprint.Band3
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
