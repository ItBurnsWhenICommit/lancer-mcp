using System.Text.RegularExpressions;
using LancerMcp.Models;

namespace LancerMcp.Services;

/// <summary>
/// Basic regex-based parser for extracting symbols from various languages.
/// This is a pragmatic starting point - can be replaced with Tree-sitter later.
/// </summary>
public sealed class BasicParserService
{
    private readonly ILogger<BasicParserService> _logger;

    public BasicParserService(ILogger<BasicParserService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses a file using basic regex patterns.
    /// </summary>
    public Task<ParsedFile> ParseFileAsync(
        string repositoryName,
        string branchName,
        string commitSha,
        string filePath,
        string content,
        Language language,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parsedFile = new ParsedFile
            {
                RepositoryName = repositoryName,
                BranchName = branchName,
                CommitSha = commitSha,
                FilePath = filePath,
                Language = language,
                Success = true
            };

            var parser = GetParserForLanguage(language);
            if (parser != null)
            {
                parser(content, parsedFile);
            }

            _logger.LogInformation(
                "Parsed {Language} file {FilePath}: {SymbolCount} symbols",
                language,
                filePath,
                parsedFile.Symbols.Count);

            return Task.FromResult(parsedFile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse {Language} file {FilePath}", language, filePath);

            return Task.FromResult(new ParsedFile
            {
                RepositoryName = repositoryName,
                BranchName = branchName,
                CommitSha = commitSha,
                FilePath = filePath,
                Language = language,
                Success = false,
                ErrorMessage = ex.Message
            });
        }
    }

    private Action<string, ParsedFile>? GetParserForLanguage(Language language)
    {
        return language switch
        {
            Language.Python => ParsePython,
            Language.JavaScript or Language.TypeScript or Language.JavaScriptReact or Language.TypeScriptReact => ParseJavaScript,
            Language.Java => ParseJava,
            Language.Go => ParseGo,
            Language.Rust => ParseRust,
            _ => null
        };
    }

    private void ParsePython(string content, ParsedFile parsedFile)
    {
        var lines = content.Split('\n');

        // Match: class ClassName or class ClassName(BaseClass) (with optional indentation)
        var classRegex = new Regex(@"^\s*class\s+(\w+)", RegexOptions.Compiled);

        // Match: def function_name or async def function_name (with optional indentation)
        var functionRegex = new Regex(@"^\s*(?:async\s+)?def\s+(\w+)\s*\(([^)]*)\)", RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNumber = i + 1;

            // Classes
            var classMatch = classRegex.Match(line);
            if (classMatch.Success)
            {
                parsedFile.Symbols.Add(new Symbol
                {
                    RepositoryName = parsedFile.RepositoryName,
                    BranchName = parsedFile.BranchName,
                    CommitSha = parsedFile.CommitSha,
                    FilePath = parsedFile.FilePath,
                    Name = classMatch.Groups[1].Value,
                    Kind = SymbolKind.Class,
                    Language = parsedFile.Language,
                    StartLine = lineNumber,
                    StartColumn = 1,
                    EndLine = lineNumber,
                    EndColumn = line.Length
                });
            }

            // Functions/Methods
            var functionMatch = functionRegex.Match(line);
            if (functionMatch.Success)
            {
                var trimmedLine = line.TrimStart();
                var isMethod = line.Length > trimmedLine.Length; // Has leading whitespace = indented = method
                parsedFile.Symbols.Add(new Symbol
                {
                    RepositoryName = parsedFile.RepositoryName,
                    BranchName = parsedFile.BranchName,
                    CommitSha = parsedFile.CommitSha,
                    FilePath = parsedFile.FilePath,
                    Name = functionMatch.Groups[1].Value,
                    Kind = isMethod ? SymbolKind.Method : SymbolKind.Function,
                    Language = parsedFile.Language,
                    StartLine = lineNumber,
                    StartColumn = 1,
                    EndLine = lineNumber,
                    EndColumn = line.Length,
                    Signature = $"{functionMatch.Groups[1].Value}({functionMatch.Groups[2].Value})"
                });
            }
        }
    }

    private void ParseJavaScript(string content, ParsedFile parsedFile)
    {
        var lines = content.Split('\n');

        // Match: class ClassName, export class ClassName, export default class ClassName
        var classRegex = new Regex(@"(?:export\s+(?:default\s+)?)?class\s+(\w+)", RegexOptions.Compiled);

        // Match: function name, async function name, export function name
        var functionRegex = new Regex(@"(?:export\s+)?(?:async\s+)?function\s+(\w+)\s*\(([^)]*)\)", RegexOptions.Compiled);

        // Match: const name = function, const name = async function, const name = () =>
        var arrowFunctionRegex = new Regex(@"(?:const|let|var)\s+(\w+)\s*=\s*(?:async\s+)?(?:function|\([^)]*\)\s*=>)", RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNumber = i + 1;

            // Classes
            var classMatch = classRegex.Match(line);
            if (classMatch.Success)
            {
                parsedFile.Symbols.Add(new Symbol
                {
                    RepositoryName = parsedFile.RepositoryName,
                    BranchName = parsedFile.BranchName,
                    CommitSha = parsedFile.CommitSha,
                    FilePath = parsedFile.FilePath,
                    Name = classMatch.Groups[1].Value,
                    Kind = SymbolKind.Class,
                    Language = parsedFile.Language,
                    StartLine = lineNumber,
                    StartColumn = 1,
                    EndLine = lineNumber,
                    EndColumn = line.Length
                });
            }

            // Functions
            var functionMatch = functionRegex.Match(line);
            if (functionMatch.Success)
            {
                parsedFile.Symbols.Add(new Symbol
                {
                    RepositoryName = parsedFile.RepositoryName,
                    BranchName = parsedFile.BranchName,
                    CommitSha = parsedFile.CommitSha,
                    FilePath = parsedFile.FilePath,
                    Name = functionMatch.Groups[1].Value,
                    Kind = SymbolKind.Function,
                    Language = parsedFile.Language,
                    StartLine = lineNumber,
                    StartColumn = 1,
                    EndLine = lineNumber,
                    EndColumn = line.Length,
                    Signature = $"{functionMatch.Groups[1].Value}({functionMatch.Groups[2].Value})"
                });
            }

            // Arrow functions
            var arrowMatch = arrowFunctionRegex.Match(line);
            if (arrowMatch.Success)
            {
                parsedFile.Symbols.Add(new Symbol
                {
                    RepositoryName = parsedFile.RepositoryName,
                    BranchName = parsedFile.BranchName,
                    CommitSha = parsedFile.CommitSha,
                    FilePath = parsedFile.FilePath,
                    Name = arrowMatch.Groups[1].Value,
                    Kind = SymbolKind.Function,
                    Language = parsedFile.Language,
                    StartLine = lineNumber,
                    StartColumn = 1,
                    EndLine = lineNumber,
                    EndColumn = line.Length
                });
            }
        }
    }

    private void ParseJava(string content, ParsedFile parsedFile)
    {
        var lines = content.Split('\n');

        // Match: public class ClassName, class ClassName, etc.
        var classRegex = new Regex(@"(?:public\s+|private\s+|protected\s+)?(?:abstract\s+)?(?:final\s+)?class\s+(\w+)", RegexOptions.Compiled);

        // Match: public void methodName, private String methodName, etc.
        var methodRegex = new Regex(@"(?:public\s+|private\s+|protected\s+)?(?:static\s+)?(?:final\s+)?(?:\w+)\s+(\w+)\s*\(([^)]*)\)", RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var lineNumber = i + 1;

            var classMatch = classRegex.Match(line);
            if (classMatch.Success)
            {
                parsedFile.Symbols.Add(new Symbol
                {
                    RepositoryName = parsedFile.RepositoryName,
                    BranchName = parsedFile.BranchName,
                    CommitSha = parsedFile.CommitSha,
                    FilePath = parsedFile.FilePath,
                    Name = classMatch.Groups[1].Value,
                    Kind = SymbolKind.Class,
                    Language = parsedFile.Language,
                    StartLine = lineNumber,
                    StartColumn = 1,
                    EndLine = lineNumber,
                    EndColumn = line.Length
                });
            }

            var methodMatch = methodRegex.Match(line);
            if (methodMatch.Success && !line.Contains("class "))
            {
                parsedFile.Symbols.Add(new Symbol
                {
                    RepositoryName = parsedFile.RepositoryName,
                    BranchName = parsedFile.BranchName,
                    CommitSha = parsedFile.CommitSha,
                    FilePath = parsedFile.FilePath,
                    Name = methodMatch.Groups[1].Value,
                    Kind = SymbolKind.Method,
                    Language = parsedFile.Language,
                    StartLine = lineNumber,
                    StartColumn = 1,
                    EndLine = lineNumber,
                    EndColumn = line.Length,
                    Signature = $"{methodMatch.Groups[1].Value}({methodMatch.Groups[2].Value})"
                });
            }
        }
    }

    private void ParseGo(string content, ParsedFile parsedFile)
    {
        var lines = content.Split('\n');

        // Match: type StructName struct
        var structRegex = new Regex(@"type\s+(\w+)\s+struct", RegexOptions.Compiled);

        // Match: func functionName, func (receiver) methodName
        var functionRegex = new Regex(@"func\s+(?:\([^)]+\)\s+)?(\w+)\s*\(([^)]*)\)", RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNumber = i + 1;

            var structMatch = structRegex.Match(line);
            if (structMatch.Success)
            {
                parsedFile.Symbols.Add(new Symbol
                {
                    RepositoryName = parsedFile.RepositoryName,
                    BranchName = parsedFile.BranchName,
                    CommitSha = parsedFile.CommitSha,
                    FilePath = parsedFile.FilePath,
                    Name = structMatch.Groups[1].Value,
                    Kind = SymbolKind.Struct,
                    Language = parsedFile.Language,
                    StartLine = lineNumber,
                    StartColumn = 1,
                    EndLine = lineNumber,
                    EndColumn = line.Length
                });
            }

            var functionMatch = functionRegex.Match(line);
            if (functionMatch.Success)
            {
                parsedFile.Symbols.Add(new Symbol
                {
                    RepositoryName = parsedFile.RepositoryName,
                    BranchName = parsedFile.BranchName,
                    CommitSha = parsedFile.CommitSha,
                    FilePath = parsedFile.FilePath,
                    Name = functionMatch.Groups[1].Value,
                    Kind = SymbolKind.Function,
                    Language = parsedFile.Language,
                    StartLine = lineNumber,
                    StartColumn = 1,
                    EndLine = lineNumber,
                    EndColumn = line.Length,
                    Signature = $"{functionMatch.Groups[1].Value}({functionMatch.Groups[2].Value})"
                });
            }
        }
    }

    private void ParseRust(string content, ParsedFile parsedFile)
    {
        var lines = content.Split('\n');

        // Match: struct StructName, pub struct StructName
        var structRegex = new Regex(@"(?:pub\s+)?struct\s+(\w+)", RegexOptions.Compiled);

        // Match: fn function_name, pub fn function_name, async fn function_name
        var functionRegex = new Regex(@"(?:pub\s+)?(?:async\s+)?fn\s+(\w+)\s*\(([^)]*)\)", RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNumber = i + 1;

            var structMatch = structRegex.Match(line);
            if (structMatch.Success)
            {
                parsedFile.Symbols.Add(new Symbol
                {
                    RepositoryName = parsedFile.RepositoryName,
                    BranchName = parsedFile.BranchName,
                    CommitSha = parsedFile.CommitSha,
                    FilePath = parsedFile.FilePath,
                    Name = structMatch.Groups[1].Value,
                    Kind = SymbolKind.Struct,
                    Language = parsedFile.Language,
                    StartLine = lineNumber,
                    StartColumn = 1,
                    EndLine = lineNumber,
                    EndColumn = line.Length
                });
            }

            var functionMatch = functionRegex.Match(line);
            if (functionMatch.Success)
            {
                parsedFile.Symbols.Add(new Symbol
                {
                    RepositoryName = parsedFile.RepositoryName,
                    BranchName = parsedFile.BranchName,
                    CommitSha = parsedFile.CommitSha,
                    FilePath = parsedFile.FilePath,
                    Name = functionMatch.Groups[1].Value,
                    Kind = SymbolKind.Function,
                    Language = parsedFile.Language,
                    StartLine = lineNumber,
                    StartColumn = 1,
                    EndLine = lineNumber,
                    EndColumn = line.Length,
                    Signature = $"{functionMatch.Groups[1].Value}({functionMatch.Groups[2].Value})"
                });
            }
        }
    }
}

