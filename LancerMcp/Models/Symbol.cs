namespace LancerMcp.Models;

/// <summary>
/// Programming language identifier.
/// </summary>
public enum Language
{
    Unknown,
    CSharp,
    Python,
    JavaScript,
    TypeScript,
    TypeScriptReact,
    JavaScriptReact,
    Java,
    Go,
    Rust,
    C,
    CPlusPlus,
    Ruby,
    PHP,
    Swift,
    Kotlin,
    Scala,
    Haskell,
    Clojure,
    Elixir,
    Erlang,
    FSharp,
    OCaml,
    Dart,
    Lua,
    Perl,
    R,
    Shell,
    PowerShell,
    SQL,
    HTML,
    CSS,
    SCSS,
    LESS,
    JSON,
    YAML,
    TOML,
    XML,
    Markdown,
    Protobuf,
    GraphQL
}

/// <summary>
/// Represents a code symbol (class, function, variable, etc.) extracted from source code.
/// </summary>
public sealed class Symbol
{
    /// <summary>
    /// Unique identifier for this symbol (generated).
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Repository name.
    /// </summary>
    public required string RepositoryName { get; init; }

    /// <summary>
    /// Branch name.
    /// </summary>
    public required string BranchName { get; init; }

    /// <summary>
    /// Commit SHA where this symbol was indexed.
    /// </summary>
    public required string CommitSha { get; init; }

    /// <summary>
    /// File path relative to repository root.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Symbol name (e.g., "UserService", "Login", "userId").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Fully qualified name if available (e.g., "MyApp.Services.UserService").
    /// </summary>
    public string? QualifiedName { get; init; }

    /// <summary>
    /// Type of symbol.
    /// </summary>
    public required SymbolKind Kind { get; init; }

    /// <summary>
    /// Programming language.
    /// </summary>
    public required Language Language { get; init; }

    /// <summary>
    /// Start line (1-based).
    /// </summary>
    public required int StartLine { get; init; }

    /// <summary>
    /// Start column (1-based).
    /// </summary>
    public required int StartColumn { get; init; }

    /// <summary>
    /// End line (1-based).
    /// </summary>
    public required int EndLine { get; init; }

    /// <summary>
    /// End column (1-based).
    /// </summary>
    public required int EndColumn { get; init; }

    /// <summary>
    /// Optional signature for methods/functions (e.g., "Login(string username, string password)").
    /// </summary>
    public string? Signature { get; init; }

    /// <summary>
    /// Optional documentation/comments.
    /// </summary>
    public string? Documentation { get; init; }

    /// <summary>
    /// Optional modifiers (public, private, static, async, etc.).
    /// </summary>
    public string[]? Modifiers { get; init; }

    /// <summary>
    /// Optional parent symbol ID (for nested symbols).
    /// </summary>
    public string? ParentSymbolId { get; init; }

    /// <summary>
    /// When this symbol was indexed.
    /// </summary>
    public DateTimeOffset IndexedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Type of code symbol.
/// </summary>
public enum SymbolKind
{
    Unknown,
    Namespace,
    Class,
    Interface,
    Struct,
    Enum,
    Method,
    Function,
    Property,
    Field,
    Variable,
    Parameter,
    Constant,
    Event,
    Delegate,
    Constructor,
    Destructor,
    Module,
    Package,
    TypeParameter
}

/// <summary>
/// Represents a relationship/edge between symbols (imports, inheritance, calls, references).
/// </summary>
public sealed class SymbolEdge
{
    /// <summary>
    /// Unique identifier for this edge.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Source symbol ID.
    /// </summary>
    public required string SourceSymbolId { get; init; }

    /// <summary>
    /// Target symbol ID or external reference.
    /// </summary>
    public required string TargetSymbolId { get; init; }

    /// <summary>
    /// Type of relationship.
    /// </summary>
    public required EdgeKind Kind { get; init; }

    /// <summary>
    /// Repository name.
    /// </summary>
    public required string RepositoryName { get; init; }

    /// <summary>
    /// Branch name.
    /// </summary>
    public required string BranchName { get; init; }

    /// <summary>
    /// Commit SHA where this edge was indexed.
    /// </summary>
    public required string CommitSha { get; init; }

    /// <summary>
    /// When this edge was indexed.
    /// </summary>
    public DateTimeOffset IndexedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Type of relationship between symbols.
/// </summary>
public enum EdgeKind
{
    Unknown,
    Import,           // A imports B
    Inherits,         // A inherits from B
    Implements,       // A implements B
    Calls,            // A calls B
    References,       // A references B
    Defines,          // A defines B
    Contains,         // A contains B (parent-child)
    Overrides,        // A overrides B
    TypeOf,           // A is of type B
    Returns           // A returns type B
}

/// <summary>
/// Represents a parsed file with its symbols and edges.
/// </summary>
public sealed class ParsedFile
{
    /// <summary>
    /// Repository name.
    /// </summary>
    public required string RepositoryName { get; init; }

    /// <summary>
    /// Branch name.
    /// </summary>
    public required string BranchName { get; init; }

    /// <summary>
    /// Commit SHA.
    /// </summary>
    public required string CommitSha { get; init; }

    /// <summary>
    /// File path relative to repository root.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Detected language.
    /// </summary>
    public required Language Language { get; init; }

    /// <summary>
    /// Symbols extracted from this file.
    /// </summary>
    public List<Symbol> Symbols { get; init; } = new();

    /// <summary>
    /// Edges/relationships extracted from this file.
    /// </summary>
    public List<SymbolEdge> Edges { get; init; } = new();

    /// <summary>
    /// Whether parsing was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if parsing failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// When this file was parsed.
    /// </summary>
    public DateTimeOffset ParsedAt { get; init; } = DateTimeOffset.UtcNow;
}

