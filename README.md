<p align="center">
  <img src="lancerLogo.png" alt="LANCER Logo" width="350"/>
</p>

# 🗡 Lancer MCP

A self-hosted MCP (Model Context Protocol) server that indexes Git repositories and provides intelligent code search for AI agents using hybrid search (BM25 + vector embeddings + graph traversal).

⚠️ My personal project and work in progress, things might not work ⚠️

## 🎯 What is this?

This is a **LAN-hosted MCP server** written in C#/.NET that:
- Clones and tracks Git repositories with incremental indexing
- Parses code using Roslyn (C#) and regex-based parsers (Python, JS, Java, Go, Rust)
- Generates code embeddings using jina-embeddings-v2-base-code
- Stores data in PostgreSQL with pgvector for vector similarity search
- Provides hybrid search combining BM25 full-text + vector semantic search + graph re-ranking
- Exposes a single `Query` MCP tool for AI agents with per-repository queries
- Self-hosted alternative to cloud-based code indexing services

## 💡 Why This Was Made

This project was born out of both curiosity and necessity.

- **To learn the Model Context Protocol deeply** — I wanted to understand how MCP servers operate at a low level: tool schemas, context lifecycles, and performance trade-offs. Building a self-hosted implementation from scratch was the best way to gain that practical knowledge.  

- **To improve personal LLMs** — I wanted a LAN-accessible platform that empowers personal LLMs to perform high-quality, contextually relevant code search — speed was not the first priority, it was power, privacy, and extensibility — without depending on cloud-based or paid indexing services.  

- **To push MCP performance beyond standard multi-tool setups** — Research such as [**LongFuncEval: Measuring the Effectiveness of Long Context Models for Function Calling** (IBM Research, 2025)](https://arxiv.org/abs/2505.10570), [**Less is More: Optimizing Function Calling for LLM Execution on Edge Devices** (arXiv 2411.15399, DATE 2025)](https://arxiv.org/abs/2411.15399), and the [**OpenAI Cookbook — MCP Tool Guide (2025)**](https://cookbook.openai.com/examples/mcp/mcp_tool_guide#best-practices-when-building-with-mcp) highlight that exposing models to large tool catalogs often leads to slower reasoning and reduced accuracy.  
  Instead of reproducing those studies, this project focuses on achieving **maximum performance and reliability** through a **single unified `Query` MCP tool**, which internally orchestrates hybrid search and retrieval.

## 🚀 Quick Start

### Prerequisites
- .NET 9.0 SDK
- Docker and Docker Compose
- Git (And access to repos you index)

### 1. Clone and Build

```bash
git clone https://github.com/ItBurnsWhenICommit/lancer-mcp.git
cd lancer-mcp
dotnet build LancerMcp/LancerMcp.csproj
```

### 2. Start Database

```bash
cd database
docker compose up -d
./test_setup.sh  # Verify database is ready
cd ..
```

### 3. Start Embedding Service

```bash
# CPU mode (slower but works without GPU)
model=jinaai/jina-embeddings-v2-base-code
volume=$PWD/embedding-data

docker run -d --name text-embeddings -p 8080:80 \
  -v $volume:/data \
  ghcr.io/huggingface/text-embeddings-inference:cpu-1.8 \
  --model-id $model
```

For GPU mode, see [docs/EMBEDDING_SETUP.md](docs/EMBEDDING_SETUP.md).

### 4. Configure

Edit `LancerMcp/appsettings.json` to add your repositories:

```json
{
  "Repositories": [
    {
      "Name": "my-project",
      "RemoteUrl": "https://github.com/user/repo.git",
      "DefaultBranch": "main"
    }
  ]
}
```

**SSH Support**: LibGit2Sharp 0.31.0 includes built-in SSH support through libgit2's OpenSSH integration. Both SSH and HTTPS URLs are supported and verified through automated tests:
- **SSH URLs** (e.g., `git@github.com:user/repo.git`): Automatically uses your system's SSH agent and SSH keys from `~/.ssh/` directory (e.g., `id_rsa`, `id_ed25519`, etc.)
- **HTTPS URLs** (e.g., `https://github.com/user/repo.git`): Uses system credential manager or personal access tokens

No additional configuration or packages are required - SSH authentication works out of the box if you have SSH keys set up on your system.

### 5. Run

```bash
dotnet run --project LancerMcp/LancerMcp.csproj
```

The server will:
1. Clone configured repositories (or load existing state from database)
2. Parse code and extract symbols
3. Generate embeddings (if embedding service is running; optional - skipped if unavailable)
4. Store data in PostgreSQL (including branch tracking state)
5. Start MCP server on `http://localhost:5171`

**Note on Embeddings**: The embedding service is optional. If not configured or unavailable, the server will continue operating with graceful degradation - symbol search and code navigation will work, but semantic similarity search will be unavailable.

## ✅ Features

### Git Repository Tracking
- Automatic cloning of configured repositories
- Efficient bare repository storage
- Default branch tracking (main/master/trunk)
- Incremental change detection (per-branch SHA cursors)
- Persistent branch state - Survives service restarts
- Thread-safe concurrent operations

### Multi-Language Code Parsing
- **C# (Roslyn)**: Full semantic analysis
  - Classes, interfaces, structs, enums
  - Methods, constructors, properties, fields
  - Inheritance and interface implementation tracking
  - Method call graph extraction
- **Python, JavaScript/TypeScript, Java, Go, Rust**: Regex-based parsing
  - Classes/structs and functions/methods
  - Function signatures
  - Basic symbol extraction

### AST-Aware Chunking
- Chunks at function/class granularity
- 5 lines of context overlap (~30-60 tokens)
- Respects 8k token limit for embedding model
- Stores both symbol and chunk boundaries

### Code Embeddings
- Uses jina-embeddings-v2-base-code (768 dimensions)
- Batch processing for efficiency
- Configurable timeout and batch size
- CPU and GPU modes supported

### PostgreSQL Storage
- Full-text search with BM25 ranking
- Vector similarity search using pgvector with HNSW indexes
- Graph traversal for code relationships
- Materialized views for analytics
- 30+ optimized indexes

### Hybrid Search
- Combines BM25 full-text search + vector semantic search
- Graph re-ranking based on symbol relationships
- Configurable weights for BM25 vs vector
- Intent detection (navigation, relations, documentation, examples)

### MCP Tools
- `Query` - Unified query interface for code search and navigation

## 🗺️ Roadmap

- [x] MCP server bootstrap with HTTP transport
- [x] Git tracker (clone, fetch, branch tracking, incremental diffs)
- [x] Multi-language parsing & symbol extraction
- [x] PostgreSQL + pgvector storage
- [x] Embedding generation with jina-embeddings-v2-base-code
- [x] Hybrid search & query orchestrator
- [ ] Enhanced query capabilities (call graphs, recent changes)
- [ ] Performance optimization and caching
- [ ] Add broader language support — TypeScript, Java, Go, Rust, Python (current focus on C#)

## 🔧 Architecture

### Indexing Pipeline

```
Git Change Detection (GitTrackerService)
    ↓
Language Detection (by extension + shebang)
    ↓
Parser Selection
    ├─→ C# → Roslyn (semantic analysis)
    └─→ Others → BasicParser (regex-based)
    ↓
Symbol & Edge Extraction
    ↓
AST-Aware Chunking (ChunkingService)
    ↓
Embedding Generation (EmbeddingService)
    ↓
PostgreSQL Storage (Dapper)
```

### Query Pipeline

```
User Query (MCP Tool)
    ↓
Intent Detection (QueryOrchestrator)
    ↓
Hybrid Search
    ├─→ BM25 Full-Text Search
    ├─→ Vector Similarity Search (pgvector)
    └─→ Graph Traversal (symbol relationships)
    ↓
Result Ranking & Merging
    ↓
Context Packaging
    ↓
Return to AI Agent
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

**Note**: Tree-sitter integration planned for future enhancement.

## 📖 Documentation

### Setup Guides
- [Database Setup](database/README.md) - PostgreSQL with pgvector
- [Embedding Service Setup](docs/EMBEDDING_SETUP.md) - Text Embeddings Inference
- [Storage Layer Setup](docs/STORAGE_LAYER_SETUP.md) - Connecting to PostgreSQL

### Architecture & Design
- [Architecture Overview](docs/ARCHITECTURE.md) - System design and MCP tool philosophy
- [Query Orchestration](docs/QUERY_ORCHESTRATION_GUIDE.md) - Hybrid search implementation

### Testing
- [Testing Strategy](docs/TESTING_STRATEGY.md) - Fixture-based integration testing
- [Unit Tests](LancerMcp.Tests/README.md) - Running unit tests
- [Integration Tests](tests/LancerMcp.IntegrationTests/README.md) - Running integration tests

### Reference
- [Database Quick Reference](database/QUICK_REFERENCE.md) - Common SQL queries
- [LICENSE](LICENSE) - MIT License

## 🧪 Testing

### Unit Tests
```bash
dotnet test LancerMcp.Tests
```

### Integration Tests
```bash
# Generate fixtures (first time only)
./scripts/refresh-fixtures.sh

# Restore fixtures and run tests
./scripts/restore-fixtures.sh
export TEST_DB_NAME=lancer_test
export TEST_WORKING_DIR=/tmp/lancer-test-XXXXXX
dotnet test tests/LancerMcp.IntegrationTests --filter Category=Integration
```

See [Testing Strategy](docs/TESTING_STRATEGY.md) for details.

## 🛠️ Configuration

Key settings in `appsettings.json`:

```json
{
  "Repositories": [
    {
      "Name": "my-project",
      "RemoteUrl": "git@github.com:user/repo.git",
      "DefaultBranch": "main"
    }
  ],
  "DatabaseHost": "localhost",
  "DatabasePort": 5432,
  "DatabaseName": "lancer",
  "EmbeddingServiceUrl": "http://localhost:8080",
  "EmbeddingBatchSize": 16,
  "EmbeddingTimeoutSeconds": 300
}
```

## 🍻 Acknowledgments

Built with:
- [ModelContextProtocol.AspNetCore](https://github.com/modelcontextprotocol/csharp-sdk) - MCP C# SDK
- [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) - Git operations
- [Roslyn](https://github.com/dotnet/roslyn) - C# semantic analysis
- [Npgsql](https://github.com/npgsql/npgsql) - PostgreSQL .NET driver
- [Dapper](https://github.com/DapperLib/Dapper) - Lightweight ORM
- [PostgreSQL](https://www.postgresql.org/) with [pgvector](https://github.com/pgvector/pgvector) - Database and vector similarity search
- [Text Embeddings Inference](https://github.com/huggingface/text-embeddings-inference) - Embedding service
- [jina-embeddings-v2-base-code](https://huggingface.co/jinaai/jina-embeddings-v2-base-code) - Code embedding model
- [xUnit](https://github.com/xunit/xunit) with [FluentAssertions](https://github.com/fluentassertions/fluentassertions) - Testing framework
- [.NET 9.0](https://dotnet.microsoft.com/) - Runtime and SDK
