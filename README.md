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

## 📚 Current Features (Step 1 & 2 Complete)

### ✅ Git Repository Tracking
- Automatic cloning of configured repositories
- Efficient bare repository storage
- Default branch tracking (main/master/trunk)
- On-demand arbitrary branch tracking
- Incremental change detection (per-branch SHA cursors)
- Thread-safe concurrent operations

### ✅ MCP Tools

- `ListRepositories` - Lists all tracked repositories and their branch states
- `TrackBranch` - Starts tracking a specific branch in a repository
- `GetFileChanges` - Gets file changes since the branch was last indexed
- `MarkBranchIndexed` - Marks a branch as fully indexed up to its current commit

## 🗺️ Roadmap

- [x] **Step 1**: MCP server bootstrap with HTTP transport
- [x] **Step 2**: Git tracker (clone, fetch, branch tracking, incremental diffs)
- [ ] **Step 3**: Multi-language parsing & symbol extraction (Tree-sitter, Roslyn)
- [ ] **Step 4**: PostgreSQL + pgvector storage
- [ ] **Step 5**: Embedding generation (code-aware models)
- [ ] **Step 6**: Hybrid search & query orchestrator
- [ ] **Step 7**: Unified `code_index.query` MCP tool
- [ ] **Step 8**: Background indexing pipeline

See [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md) for detailed progress.

## 📖 Documentation

- [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md) - Detailed implementation progress
- [LICENSE](LICENSE) - MIT License

## 🙏 Acknowledgments

Built with:
- [ModelContextProtocol.AspNetCore](https://github.com/modelcontextprotocol/csharp-sdk) - MCP C# SDK
- [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) - Git operations
- [.NET 9.0](https://dotnet.microsoft.com/) - Runtime and SDK
