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
    private readonly ILogger<RoslynParserService> _logger;

    public RoslynParserService(ILogger<RoslynParserService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses a C# file and extracts symbols and edges.
    /// </summary>
    public async Task<ParsedFile> ParseFileAsync(
        string repositoryName,
        string branchName,
        string commitSha,
        string filePath,
        string content,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tree = CSharpSyntaxTree.ParseText(content, path: filePath, cancellationToken: cancellationToken);
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

            // Create a compilation for semantic analysis
            var compilation = CSharpCompilation.Create("TempCompilation")
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddSyntaxTrees(tree);

            var semanticModel = compilation.GetSemanticModel(tree);

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
                QualifiedName = symbolInfo?.ToDisplayString(),
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
                QualifiedName = symbolInfo?.ToDisplayString(),
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
                if (symbolInfo.Symbol != null)
                {
                    var qualifiedName = symbolInfo.Symbol.ToDisplayString();

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
}

