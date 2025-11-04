using System.Text;
using LancerMcp.Models;

namespace LancerMcp.Services;

/// <summary>
/// Detects programming language from file extension and shebang.
/// </summary>
public sealed class LanguageDetectionService
{
    private static readonly Dictionary<string, Language> ExtensionToLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        // C# and .NET
        [".cs"] = Language.CSharp,
        [".csx"] = Language.CSharp,
        [".cake"] = Language.CSharp,

        // JavaScript/TypeScript
        [".js"] = Language.JavaScript,
        [".mjs"] = Language.JavaScript,
        [".cjs"] = Language.JavaScript,
        [".jsx"] = Language.JavaScriptReact,
        [".ts"] = Language.TypeScript,
        [".tsx"] = Language.TypeScriptReact,
        [".mts"] = Language.TypeScript,
        [".cts"] = Language.TypeScript,

        // Python
        [".py"] = Language.Python,
        [".pyw"] = Language.Python,
        [".pyi"] = Language.Python,

        // Java
        [".java"] = Language.Java,

        // C/C++
        [".c"] = Language.C,
        [".h"] = Language.C,
        [".cpp"] = Language.CPlusPlus,
        [".cc"] = Language.CPlusPlus,
        [".cxx"] = Language.CPlusPlus,
        [".hpp"] = Language.CPlusPlus,
        [".hh"] = Language.CPlusPlus,
        [".hxx"] = Language.CPlusPlus,

        // Go
        [".go"] = Language.Go,

        // Rust
        [".rs"] = Language.Rust,

        // Ruby
        [".rb"] = Language.Ruby,
        [".rake"] = Language.Ruby,

        // PHP
        [".php"] = Language.PHP,
        [".phtml"] = Language.PHP,

        // Shell
        [".sh"] = Language.Shell,
        [".bash"] = Language.Shell,
        [".zsh"] = Language.Shell,

        // Kotlin
        [".kt"] = Language.Kotlin,
        [".kts"] = Language.Kotlin,

        // Swift
        [".swift"] = Language.Swift,

        // Scala
        [".scala"] = Language.Scala,
        [".sc"] = Language.Scala,

        // R
        [".r"] = Language.R,
        [".R"] = Language.R,

        // Lua
        [".lua"] = Language.Lua,

        // Perl
        [".pl"] = Language.Perl,
        [".pm"] = Language.Perl,

        // Haskell
        [".hs"] = Language.Haskell,

        // Elixir
        [".ex"] = Language.Elixir,
        [".exs"] = Language.Elixir,

        // Clojure
        [".clj"] = Language.Clojure,
        [".cljs"] = Language.Clojure,
        [".cljc"] = Language.Clojure,

        // F#
        [".fs"] = Language.FSharp,
        [".fsx"] = Language.FSharp,
        [".fsi"] = Language.FSharp,

        // Dart
        [".dart"] = Language.Dart,

        // SQL
        [".sql"] = Language.SQL,

        // HTML/CSS
        [".html"] = Language.HTML,
        [".htm"] = Language.HTML,
        [".css"] = Language.CSS,
        [".scss"] = Language.SCSS,
        [".less"] = Language.LESS,

        // Markup
        [".xml"] = Language.XML,
        [".json"] = Language.JSON,
        [".yaml"] = Language.YAML,
        [".yml"] = Language.YAML,
        [".toml"] = Language.TOML,
        [".md"] = Language.Markdown,
        [".markdown"] = Language.Markdown,
    };

    private static readonly Dictionary<string, Language> ShebangToLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["python"] = Language.Python,
        ["python2"] = Language.Python,
        ["python3"] = Language.Python,
        ["node"] = Language.JavaScript,
        ["nodejs"] = Language.JavaScript,
        ["ruby"] = Language.Ruby,
        ["perl"] = Language.Perl,
        ["php"] = Language.PHP,
        ["bash"] = Language.Shell,
        ["sh"] = Language.Shell,
        ["zsh"] = Language.Shell,
        ["fish"] = Language.Shell,
        ["lua"] = Language.Lua,
    };

    /// <summary>
    /// Detects the programming language of a file.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="fileContent">Optional file content (for shebang detection).</param>
    /// <returns>Detected language or Unknown.</returns>
    public Language DetectLanguage(string filePath, string? fileContent = null)
    {
        // First try extension
        var extension = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(extension) && ExtensionToLanguage.TryGetValue(extension, out var language))
        {
            return language;
        }

        // Try shebang if content is provided
        if (!string.IsNullOrEmpty(fileContent))
        {
            var shebangLanguage = DetectFromShebang(fileContent);
            if (shebangLanguage != null)
            {
                return shebangLanguage.Value;
            }
        }

        // Special cases for files without extensions
        var fileName = Path.GetFileName(filePath);
        return fileName.ToLowerInvariant() switch
        {
            "rakefile" => Language.Ruby,
            "gemfile" => Language.Ruby,
            "vagrantfile" => Language.Ruby,
            _ => Language.Unknown
        };
    }

    /// <summary>
    /// Detects language from shebang line.
    /// </summary>
    private Language? DetectFromShebang(string content)
    {
        if (!content.StartsWith("#!"))
        {
            return null;
        }

        var firstLineEnd = content.IndexOf('\n');
        var shebang = firstLineEnd > 0 ? content[..firstLineEnd] : content;

        // Extract the interpreter from the shebang
        // Examples: #!/usr/bin/python3, #!/usr/bin/env node
        foreach (var (key, value) in ShebangToLanguage)
        {
            if (shebang.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a file should be indexed based on its language.
    /// </summary>
    public bool ShouldIndex(Language language)
    {
        return language switch
        {
            Language.Unknown => false,
            Language.Markdown => false,  // Skip documentation files for now
            Language.JSON => false,      // Skip data files
            Language.YAML => false,
            Language.TOML => false,
            Language.XML => false,
            Language.HTML => false,
            Language.CSS => false,
            Language.SCSS => false,
            Language.LESS => false,
            _ => true
        };
    }

    /// <summary>
    /// Gets all supported languages.
    /// </summary>
    public IEnumerable<Language> GetSupportedLanguages()
    {
        return ExtensionToLanguage.Values.Distinct().OrderBy(x => x);
    }
}

