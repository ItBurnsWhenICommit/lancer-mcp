using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using LancerMcp.Models;

namespace LancerMcp.Services;

/// <summary>
/// Parses C# code using Roslyn to extract symbols and relationships.
/// </summary>
public sealed class RoslynParserService
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> _defaultMetadataReferences =
        new(LoadMetadataReferences, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly SymbolDisplayFormat CanonicalDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType | SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);
    private static readonly IReadOnlyDictionary<string, string> SpecialTypeKeywordMap = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["bool"] = "System.Boolean",
        ["byte"] = "System.Byte",
        ["sbyte"] = "System.SByte",
        ["short"] = "System.Int16",
        ["ushort"] = "System.UInt16",
        ["int"] = "System.Int32",
        ["uint"] = "System.UInt32",
        ["long"] = "System.Int64",
        ["ulong"] = "System.UInt64",
        ["float"] = "System.Single",
        ["double"] = "System.Double",
        ["decimal"] = "System.Decimal",
        ["char"] = "System.Char",
        ["string"] = "System.String",
        ["object"] = "System.Object",
        ["nint"] = "System.IntPtr",
        ["nuint"] = "System.UIntPtr",
        ["void"] = "System.Void"
    };
    private static readonly Regex SpecialTypeKeywordRegex = new(
        @"\b(bool|byte|sbyte|short|ushort|int|uint|long|ulong|float|double|decimal|char|string|object|nint|nuint|void)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SpecialTypeQualifiedKeywordRegex = new(
        @"\bSystem\.(bool|byte|sbyte|short|ushort|int|uint|long|ulong|float|double|decimal|char|string|object|nint|nuint|void)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly ILogger<RoslynParserService> _logger;

    public RoslynParserService(ILogger<RoslynParserService> logger)
    {
        _logger = logger;
    }

    private static string? GetCanonicalQualifiedName(ISymbol? symbol)
    {
        if (symbol == null)
        {
            return null;
        }

        var parts = symbol.ToDisplayParts(CanonicalDisplayFormat);
        string display;
        if (parts.Length == 0)
        {
            display = symbol.ToDisplayString(CanonicalDisplayFormat);
        }
        else
        {
            var formattedParts = new string[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                formattedParts[i] = FormatDisplayPart(parts[i]);
            }

            display = string.Concat(formattedParts);
        }

        return ReplaceSpecialTypeKeywords(display);
    }

    private static string FormatDisplayPart(SymbolDisplayPart part)
    {
        if (part.Symbol is ITypeSymbol typeSymbol && typeSymbol.SpecialType != SpecialType.None)
        {
            var fullyQualified = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return StripGlobalQualifier(fullyQualified);
        }

        if (part.Kind == SymbolDisplayPartKind.Keyword &&
            SpecialTypeKeywordMap.TryGetValue(part.ToString(), out var replacement))
        {
            return replacement;
        }

        return part.ToString();
    }

    private static string StripGlobalQualifier(string name)
    {
        return name.Replace("global::", string.Empty, StringComparison.Ordinal);
    }

    private static string ReplaceSpecialTypeKeywords(string display)
    {
        var normalized = SpecialTypeQualifiedKeywordRegex.Replace(display, match => SpecialTypeKeywordMap[match.Groups[1].Value]);
        return SpecialTypeKeywordRegex.Replace(normalized, match => SpecialTypeKeywordMap[match.Value]);
    }

    /// <summary>
    /// Parses a C# file and extracts symbols and edges.
    /// </summary>
    /// <param name="workspaceCompilation">Optional workspace compilation for full semantic analysis. If null, uses fallback compilation.</param>
    public async Task<ParsedFile> ParseFileAsync(
        string repositoryName,
        string branchName,
        string commitSha,
        string filePath,
        string content,
        Compilation? workspaceCompilation = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parseOptions = CSharpParseOptions.Default;
            var tree = CSharpSyntaxTree.ParseText(content, options: parseOptions, path: filePath, cancellationToken: cancellationToken);
            var root = await tree.GetRootAsync(cancellationToken);

            var parsedFile = new ParsedFile
            {
                RepositoryName = repositoryName,
                BranchName = branchName,
                CommitSha = commitSha,
                FilePath = filePath,
                Language = Language.CSharp,
                Success = true
            };

            SemanticModel semanticModel;

            // Use workspace compilation if available, otherwise create a minimal one
            if (workspaceCompilation != null)
            {
                // Add the current file's syntax tree to the workspace compilation
                var updatedCompilation = workspaceCompilation.AddSyntaxTrees(tree);
                semanticModel = updatedCompilation.GetSemanticModel(tree);

                _logger.LogDebug(
                    "Using workspace compilation for {FilePath} with {ReferenceCount} references",
                    filePath,
                    workspaceCompilation.References.Count());
            }
            else
            {
                // Fallback: Create a minimal compilation with framework references
                var compilation = CSharpCompilation.Create(
                        assemblyName: "TempCompilation",
                        syntaxTrees: new[] { tree },
                        references: _defaultMetadataReferences.Value,
                        options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

                semanticModel = compilation.GetSemanticModel(tree);

                _logger.LogDebug("Using fallback compilation for {FilePath}", filePath);
            }

            // Extract symbols
            var visitor = new SymbolExtractorVisitor(
                repositoryName,
                branchName,
                commitSha,
                filePath,
                semanticModel,
                parsedFile.Symbols,
                parsedFile.Edges,
                _logger);

            visitor.Visit(root);

            _logger.LogInformation(
                "Parsed C# file {FilePath}: {SymbolCount} symbols, {EdgeCount} edges",
                filePath,
                parsedFile.Symbols.Count,
                parsedFile.Edges.Count);

            return parsedFile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse C# file {FilePath}", filePath);

            return new ParsedFile
            {
                RepositoryName = repositoryName,
                BranchName = branchName,
                CommitSha = commitSha,
                FilePath = filePath,
                Language = Language.CSharp,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Visitor that walks the syntax tree and extracts symbols.
    /// </summary>
    private class SymbolExtractorVisitor : CSharpSyntaxWalker
    {
        private readonly string _repositoryName;
        private readonly string _branchName;
        private readonly string _commitSha;
        private readonly string _filePath;
        private readonly SemanticModel _semanticModel;
        private readonly List<Symbol> _symbols;
        private readonly List<SymbolEdge> _edges;
        private readonly Dictionary<string, string> _symbolLookup = new(); // QualifiedName -> Symbol ID
        private readonly ILogger _logger;
        private readonly Stack<string> _parentSymbolIds = new();
        private readonly Dictionary<string, string> _fieldTypes = new(); // field name -> type name

        public SymbolExtractorVisitor(
            string repositoryName,
            string branchName,
            string commitSha,
            string filePath,
            SemanticModel semanticModel,
            List<Symbol> symbols,
            List<SymbolEdge> edges,
            ILogger logger)
        {
            _repositoryName = repositoryName;
            _branchName = branchName;
            _commitSha = commitSha;
            _filePath = filePath;
            _semanticModel = semanticModel;
            _symbols = symbols;
            _edges = edges;
            _logger = logger;
        }

        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            var symbol = CreateSymbol(node.Name, Models.SymbolKind.Namespace, node);
            _parentSymbolIds.Push(symbol.Id);
            base.VisitNamespaceDeclaration(node);
            _parentSymbolIds.Pop();
        }

        public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        {
            var symbol = CreateSymbol(node.Name, Models.SymbolKind.Namespace, node);
            _parentSymbolIds.Push(symbol.Id);
            base.VisitFileScopedNamespaceDeclaration(node);
            _parentSymbolIds.Pop();
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var symbol = CreateSymbol(node.Identifier, Models.SymbolKind.Class, node, node.Modifiers);
            ExtractBaseTypes(node.BaseList, symbol.Id);
            _parentSymbolIds.Push(symbol.Id);
            base.VisitClassDeclaration(node);
            _parentSymbolIds.Pop();
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            var symbol = CreateSymbol(node.Identifier, Models.SymbolKind.Interface, node, node.Modifiers);
            ExtractBaseTypes(node.BaseList, symbol.Id);
            _parentSymbolIds.Push(symbol.Id);
            base.VisitInterfaceDeclaration(node);
            _parentSymbolIds.Pop();
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            var symbol = CreateSymbol(node.Identifier, Models.SymbolKind.Struct, node, node.Modifiers);
            ExtractBaseTypes(node.BaseList, symbol.Id);
            _parentSymbolIds.Push(symbol.Id);
            base.VisitStructDeclaration(node);
            _parentSymbolIds.Pop();
        }

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            var symbol = CreateSymbol(node.Identifier, Models.SymbolKind.Enum, node, node.Modifiers);
            _parentSymbolIds.Push(symbol.Id);
            base.VisitEnumDeclaration(node);
            _parentSymbolIds.Pop();
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var signature = $"{node.Identifier.Text}({string.Join(", ", node.ParameterList.Parameters.Select(p => $"{p.Type} {p.Identifier}"))})";
            var symbol = CreateSymbol(node.Identifier, Models.SymbolKind.Method, node, node.Modifiers, signature);

            // Extract return type edge
            ExtractTypeEdge(node.ReturnType, symbol.Id, EdgeKind.Returns);

            // Extract parameter type edges
            ExtractParameterTypeEdges(node.ParameterList, symbol.Id);

            // Extract method calls
            ExtractMethodCalls(node.Body, symbol.Id);

            base.VisitMethodDeclaration(node);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            var signature = $"{node.Identifier.Text}({string.Join(", ", node.ParameterList.Parameters.Select(p => $"{p.Type} {p.Identifier}"))})";
            var symbol = CreateSymbol(node.Identifier, Models.SymbolKind.Constructor, node, node.Modifiers, signature);

            // Extract parameter type edges
            ExtractParameterTypeEdges(node.ParameterList, symbol.Id);

            ExtractMethodCalls(node.Body, symbol.Id);

            base.VisitConstructorDeclaration(node);
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            var symbol = CreateSymbol(node.Identifier, Models.SymbolKind.Property, node, node.Modifiers);

            // Extract property type edge
            ExtractTypeEdge(node.Type, symbol.Id, EdgeKind.TypeOf);

            base.VisitPropertyDeclaration(node);
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            foreach (var variable in node.Declaration.Variables)
            {
                var symbol = CreateSymbol(variable.Identifier, Models.SymbolKind.Field, node, node.Modifiers);

                // Extract field type edge
                ExtractTypeEdge(node.Declaration.Type, symbol.Id, EdgeKind.TypeOf);

                // Store field type for later lookup
                var fieldName = variable.Identifier.Text;
                var typeName = node.Declaration.Type.ToString();
                _fieldTypes[fieldName] = typeName;
            }
            base.VisitFieldDeclaration(node);
        }

        private Symbol CreateSymbol(
            SyntaxToken identifier,
            Models.SymbolKind kind,
            SyntaxNode node,
            SyntaxTokenList? modifiers = null,
            string? signature = null)
        {
            var lineSpan = node.GetLocation().GetLineSpan();
            var symbolInfo = _semanticModel.GetDeclaredSymbol(node);

            var symbol = new Symbol
            {
                RepositoryName = _repositoryName,
                BranchName = _branchName,
                CommitSha = _commitSha,
                FilePath = _filePath,
                Name = identifier.Text,
                QualifiedName = GetCanonicalQualifiedName(symbolInfo),
                Kind = kind,
                Language = Language.CSharp,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                StartColumn = lineSpan.StartLinePosition.Character + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1,
                EndColumn = lineSpan.EndLinePosition.Character + 1,
                Signature = signature,
                Modifiers = modifiers?.Select(m => m.Text).ToArray(),
                ParentSymbolId = _parentSymbolIds.Count > 0 ? _parentSymbolIds.Peek() : null
            };

            _symbols.Add(symbol);

            // Add to lookup table for edge resolution
            if (!string.IsNullOrEmpty(symbol.QualifiedName))
            {
                _symbolLookup[symbol.QualifiedName] = symbol.Id;
            }

            return symbol;
        }

        private Symbol CreateSymbol(
            NameSyntax name,
            Models.SymbolKind kind,
            SyntaxNode node)
        {
            var lineSpan = node.GetLocation().GetLineSpan();
            var symbolInfo = _semanticModel.GetDeclaredSymbol(node);

            var symbol = new Symbol
            {
                RepositoryName = _repositoryName,
                BranchName = _branchName,
                CommitSha = _commitSha,
                FilePath = _filePath,
                Name = name.ToString(),
                QualifiedName = GetCanonicalQualifiedName(symbolInfo),
                Kind = kind,
                Language = Language.CSharp,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                StartColumn = lineSpan.StartLinePosition.Character + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1,
                EndColumn = lineSpan.EndLinePosition.Character + 1,
                ParentSymbolId = _parentSymbolIds.Count > 0 ? _parentSymbolIds.Peek() : null
            };

            _symbols.Add(symbol);

            // Add to lookup table for edge resolution
            if (!string.IsNullOrEmpty(symbol.QualifiedName))
            {
                _symbolLookup[symbol.QualifiedName] = symbol.Id;
            }

            return symbol;
        }

        private void ExtractBaseTypes(BaseListSyntax? baseList, string sourceSymbolId)
        {
            if (baseList == null)
                return;

            foreach (var baseType in baseList.Types)
            {
                var typeInfo = _semanticModel.GetTypeInfo(baseType.Type);
                if (typeInfo.Type != null)
                {
                    var qualifiedName = typeInfo.Type.ToDisplayString();

                    // Try to resolve to a symbol ID, otherwise use qualified name
                    var targetSymbolId = _symbolLookup.TryGetValue(qualifiedName, out var symbolId)
                        ? symbolId
                        : qualifiedName;

                    var edge = new SymbolEdge
                    {
                        SourceSymbolId = sourceSymbolId,
                        TargetSymbolId = targetSymbolId,
                        Kind = typeInfo.Type.TypeKind == TypeKind.Interface ? EdgeKind.Implements : EdgeKind.Inherits,
                        RepositoryName = _repositoryName,
                        BranchName = _branchName,
                        CommitSha = _commitSha
                    };
                    _edges.Add(edge);
                }
            }
        }

        private void ExtractMethodCalls(BlockSyntax? body, string sourceSymbolId)
        {
            if (body == null)
                return;

            var invocations = body.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                var symbolInfo = _semanticModel.GetSymbolInfo(invocation);
                string? qualifiedName = null;

                if (symbolInfo.Symbol != null)
                {
                    qualifiedName = GetCanonicalQualifiedName(symbolInfo.Symbol);
                }
                else
                {
                    // Fallback: Try to construct a qualified name from the invocation expression
                    // This handles cases where the semantic model can't resolve the symbol
                    // (e.g., due to incomplete compilation or missing references)
                    qualifiedName = TryGetQualifiedNameFromInvocation(invocation);
                }

                if (!string.IsNullOrEmpty(qualifiedName))
                {
                    // Try to resolve to a symbol ID, otherwise use qualified name
                    var targetSymbolId = _symbolLookup.TryGetValue(qualifiedName, out var symbolId)
                        ? symbolId
                        : qualifiedName;

                    var edge = new SymbolEdge
                    {
                        SourceSymbolId = sourceSymbolId,
                        TargetSymbolId = targetSymbolId,
                        Kind = EdgeKind.Calls,
                        RepositoryName = _repositoryName,
                        BranchName = _branchName,
                        CommitSha = _commitSha
                    };
                    _edges.Add(edge);
                }
            }
        }

        /// <summary>
        /// Attempts to extract a qualified method name from an invocation expression.
        /// This is a fallback for when the semantic model can't resolve the symbol.
        /// </summary>
        private string? TryGetQualifiedNameFromInvocation(InvocationExpressionSyntax invocation)
        {
            // Try to get the method name from the invocation expression
            // Examples:
            // - _db.QueryAsync<T>(...) -> DatabaseService.QueryAsync
            // - logger.LogInformation(...) -> ILogger.LogInformation
            // - Console.WriteLine(...) -> Console.WriteLine

            var expression = invocation.Expression;

            // Handle member access (e.g., _db.QueryAsync, logger.LogInformation)
            if (expression is MemberAccessExpressionSyntax memberAccess)
            {
                var memberName = memberAccess.Name.Identifier.Text;

                // Try to get the type of the object being called on
                var typeInfo = _semanticModel.GetTypeInfo(memberAccess.Expression);
                if (typeInfo.Type != null)
                {
                    var typeName = GetCanonicalQualifiedName(typeInfo.Type);
                    return string.IsNullOrWhiteSpace(typeName) ? null : $"{typeName}.{memberName}";
                }

                // Fallback: Check if it's a field we've seen in this file
                if (memberAccess.Expression is IdentifierNameSyntax identifierName)
                {
                    var fieldName = identifierName.Identifier.Text;
                    if (_fieldTypes.TryGetValue(fieldName, out var fieldType))
                    {
                        // Use the field type we stored earlier
                        return $"{fieldType}.{memberName}";
                    }
                }

                // Last resort: Use the expression text
                return $"{memberAccess.Expression}.{memberName}";
            }

            // Handle simple identifiers (e.g., MethodName())
            if (expression is IdentifierNameSyntax identifier)
            {
                return identifier.Identifier.Text;
            }

            return null;
        }

        /// <summary>
        /// Extracts a type edge from a type syntax node (for fields, properties, return types).
        /// </summary>
        private void ExtractTypeEdge(TypeSyntax typeSyntax, string sourceSymbolId, EdgeKind edgeKind)
        {
            var typeInfo = _semanticModel.GetTypeInfo(typeSyntax);
            if (typeInfo.Type != null && typeInfo.Type.TypeKind != TypeKind.Error)
            {
                var qualifiedName = typeInfo.Type.ToDisplayString();

                // Skip only actual primitive types and void to reduce noise
                // Don't skip important framework types like IEnumerable<T>, DateTime, etc.
                if (IsPrimitiveType(typeInfo.Type.SpecialType))
                    return;

                // Try to resolve to a symbol ID, otherwise use qualified name
                var targetSymbolId = _symbolLookup.TryGetValue(qualifiedName, out var symbolId)
                    ? symbolId
                    : qualifiedName;

                var edge = new SymbolEdge
                {
                    SourceSymbolId = sourceSymbolId,
                    TargetSymbolId = targetSymbolId,
                    Kind = edgeKind,
                    RepositoryName = _repositoryName,
                    BranchName = _branchName,
                    CommitSha = _commitSha
                };
                _edges.Add(edge);
            }
        }

        /// <summary>
        /// Determines if a SpecialType represents a primitive type that should be excluded from type edges.
        /// Only excludes actual primitives (bool, numeric types, string, void) to reduce noise.
        /// Does NOT exclude important framework types like IEnumerable, DateTime, Object, etc.
        /// </summary>
        private static bool IsPrimitiveType(SpecialType specialType)
        {
            return specialType switch
            {
                SpecialType.System_Void => true,
                SpecialType.System_Boolean => true,
                SpecialType.System_Char => true,
                SpecialType.System_SByte => true,
                SpecialType.System_Byte => true,
                SpecialType.System_Int16 => true,
                SpecialType.System_UInt16 => true,
                SpecialType.System_Int32 => true,
                SpecialType.System_UInt32 => true,
                SpecialType.System_Int64 => true,
                SpecialType.System_UInt64 => true,
                SpecialType.System_Decimal => true,
                SpecialType.System_Single => true,
                SpecialType.System_Double => true,
                SpecialType.System_String => true,
                SpecialType.System_IntPtr => true,
                SpecialType.System_UIntPtr => true,
                _ => false
            };
        }

        /// <summary>
        /// Extracts type edges for all parameters in a parameter list.
        /// </summary>
        private void ExtractParameterTypeEdges(ParameterListSyntax parameterList, string sourceSymbolId)
        {
            foreach (var parameter in parameterList.Parameters)
            {
                if (parameter.Type != null)
                {
                    ExtractTypeEdge(parameter.Type, sourceSymbolId, EdgeKind.TypeOf);
                }
            }
        }
    }

    private static ImmutableArray<MetadataReference> LoadMetadataReferences()
    {
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;

        if (!string.IsNullOrWhiteSpace(trustedAssemblies))
        {
            var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!uniquePaths.Add(path) || !File.Exists(path))
                {
                    continue;
                }

                try
                {
                    builder.Add(MetadataReference.CreateFromFile(path));
                }
                catch
                {
                    // Some runtime assemblies can't be loaded (e.g., native images); skip them silently.
                }
            }
        }

        if (builder.Count == 0)
        {
            // Fall back to a small, known set so the semantic model can at least resolve framework basics.
            builder.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            builder.Add(MetadataReference.CreateFromFile(typeof(Console).Assembly.Location));
            builder.Add(MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location));
        }

        return builder.ToImmutable();
    }
}
