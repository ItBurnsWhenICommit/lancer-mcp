using System.Text.RegularExpressions;

namespace LancerMcp.Services;

/// <summary>
/// Tokenization helpers for symbol-aware retrieval.
/// </summary>
public static class SymbolTokenization
{
    private static readonly Regex SegmentSplit = new(@"[^A-Za-z0-9]+", RegexOptions.Compiled);
    private static readonly Regex TokenPattern = new(@"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+", RegexOptions.Compiled);
    private static readonly Regex IdentifierPattern = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this",
        "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
        "using", "virtual", "void", "volatile", "while",
        "add", "alias", "and", "ascending", "async", "await", "by", "descending", "dynamic",
        "equals", "file", "from", "get", "global", "group", "init", "into", "join", "let",
        "managed", "nameof", "not", "notnull", "on", "or", "orderby", "partial", "record",
        "remove", "required", "select", "set", "unmanaged", "value", "var", "when", "where",
        "with", "yield"
    };

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

    public static IReadOnlyList<string> ExtractIdentifierTokens(string text, int maxChars, int maxTokens)
    {
        if (string.IsNullOrWhiteSpace(text) || maxChars <= 0 || maxTokens <= 0)
        {
            return Array.Empty<string>();
        }

        var slice = text.Length > maxChars ? text[..maxChars] : text;
        var tokens = new List<string>();

        foreach (Match match in IdentifierPattern.Matches(slice))
        {
            var identifier = match.Value;
            if (CSharpKeywords.Contains(identifier))
            {
                continue;
            }

            foreach (var token in Tokenize(identifier))
            {
                if (token.Length < 3)
                {
                    continue;
                }

                if (token.All(char.IsDigit))
                {
                    continue;
                }

                if (CSharpKeywords.Contains(token))
                {
                    continue;
                }

                tokens.Add(token);
                if (tokens.Count >= maxTokens)
                {
                    return tokens;
                }
            }
        }

        return tokens;
    }
}
