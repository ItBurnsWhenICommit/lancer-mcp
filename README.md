# Project Indexer MCP

A self-hosted MCP (Model Context Protocol) server that indexes large repositories, tracks branches, and provides intelligent code search and navigation for LLMs.

## 🎯 What is this?

This is a **LAN-hosted MCP server** written in C#/.NET that:
- Clones and tracks Git repositories (main/master/trunk + arbitrary branches)
- Scales to large monorepos and multi-language codebases
- Will provide hybrid search (BM25 + vector + graph-aware ranking)
- Exposes MCP tools for AI agents to query code intelligently
- Self-hosted alternative to cloud-based code indexing services

## 🚀 Quick Start

### Prerequisites
- .NET 9.0 SDK
- Git installed on your system

### 1. Clone and Build

```bash
git clone https://github.com/ItBurnsWhenICommit/project-indexer-mcp.git
cd project-indexer-mcp
dotnet build ProjectIndexerMcp/ProjectIndexerMcp.csproj
```

### 2. Configure

Copy the example configuration:

```bash
cp appsettings.example.json ProjectIndexerMcp/appsettings.json
```

Edit `ProjectIndexerMcp/appsettings.json` to add your repositories.

### 3. Run

```bash
dotnet run --project ProjectIndexerMcp/ProjectIndexerMcp.csproj
```

The server will start on `http://localhost:5171` and automatically clone/track configured repositories.

## 📚 Current Features (Steps 1, 2 & 3 Complete)

### ✅ Git Repository Tracking
- Automatic cloning of configured repositories
- Efficient bare repository storage
- Default branch tracking (main/master/trunk)
- On-demand arbitrary branch tracking
- Incremental change detection (per-branch SHA cursors)
- Thread-safe concurrent operations

### ✅ Multi-Language Parsing & Symbol Extraction
- **C# (Roslyn)**: Precise parsing with full semantic analysis
  - Classes, interfaces, structs, enums
  - Methods, constructors, properties, fields
  - Inheritance and interface implementation tracking
  - Method call graph extraction
- **Python, JavaScript/TypeScript, Java, Go, Rust**: Regex-based parsing
  - Classes/structs and functions/methods
  - Function signatures
  - Basic symbol extraction
- **Language Detection**: Automatic detection via file extension and shebang
- **Concurrent Processing**: Configurable file read concurrency

### ✅ MCP Tools

- `Query` - Unified query interface for code search and navigation (triggers on-demand indexing)

## 🗺️ Roadmap

- [x] **Step 1**: MCP server bootstrap with HTTP transport
- [x] **Step 2**: Git tracker (clone, fetch, branch tracking, incremental diffs)
- [x] **Step 3**: Multi-language parsing & symbol extraction (Roslyn for C#, regex-based for others)
- [ ] **Step 4**: PostgreSQL + pgvector storage
- [ ] **Step 5**: Embedding generation (code-aware models)
- [ ] **Step 6**: Hybrid search & query orchestrator
- [ ] **Step 7**: Enhanced query capabilities (semantic search, call graphs, etc.)
- [ ] **Step 8**: Background indexing pipeline

## 🔧 Architecture

### Indexing Pipeline (Step 3)

```
File Change Detection (GitTrackerService)
    ↓
Language Detection (by extension + shebang)
    ↓
Parser Selection
    ├─→ C# → Roslyn (semantic analysis)
    └─→ Others → BasicParser (regex-based)
    ↓
Symbol & Edge Extraction
    ↓
In-Memory Storage (Step 4 will add PostgreSQL)
```

### Supported Languages

| Language | Parser | Symbols Extracted |
|----------|--------|-------------------|
| C# | Roslyn | Classes, interfaces, structs, enums, methods, properties, fields, constructors |
| Python | Regex | Classes, functions, methods |
| JavaScript/TypeScript | Regex | Classes, functions, arrow functions |
| Java | Regex | Classes, methods |
| Go | Regex | Structs, functions |
| Rust | Regex | Structs, functions |

**Note**: Tree-sitter integration planned for future enhancement to improve parsing accuracy for non-C# languages.

## 📖 Documentation

- [LICENSE](LICENSE) - MIT License

## 🧪 Testing

Run all tests:
```bash
dotnet test
```

Run specific test suite:
```bash
dotnet test --filter "FullyQualifiedName~IndexingServiceTests"
```

## 🙏 Acknowledgments

Built with:
- [ModelContextProtocol.AspNetCore](https://github.com/modelcontextprotocol/csharp-sdk) - MCP C# SDK
- [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) - Git operations
- [.NET 9.0](https://dotnet.microsoft.com/) - Runtime and SDK
