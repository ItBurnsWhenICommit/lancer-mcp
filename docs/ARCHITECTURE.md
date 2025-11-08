# Architecture - Lancer MCP

## Core Principle: ONE Tool for Agents

This MCP server exposes **exactly ONE tool** to AI agents: `Query`

The server handles all complexity server-side:
- Intent detection (search vs navigation vs relations)
- Query parsing and expansion
- Hybrid search orchestration (BM25 + vector + graph)
- Result ranking and context packaging

**Agents don't need to know about:**
- Git operations (cloning, fetching, diffing)
- Repository management
- Branch tracking
- Indexing pipelines
- Database queries
- Embedding generation

## The Single Tool: `Query`

```typescript
// MCP Tool Signature
Query(
  repository: string,      // Required: repository name to search
  query: string,           // Natural language query
  branch?: string,         // Optional: filter to specific branch
  maxResults?: number      // Optional: limit results
) -> QueryResult
```

### Example Queries

```javascript
// Code search
Query(repository: "my-api", query: "find all classes that implement IRepository")

// Symbol lookup
Query(repository: "my-api", query: "show me the definition of UserService")

// Find references
Query(repository: "my-api", query: "what calls the Login method?")

// Recent changes
Query(repository: "my-api", query: "find recent changes in authentication code")

// Branch-specific search
Query("what changed in the new feature?", branch: "feature/new-auth")
```

## Internal Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    AI Agent (Client)                    │
└────────────────────┬────────────────────────────────────┘
                     │
                     │ MCP Protocol (HTTP/SSE)
                     │ ONE TOOL: Query(query, repo?, branch?)
                     │
┌────────────────────▼───────────────────────────────────┐
│              MCP Server (ASP.NET Core)                 │
│  ┌──────────────────────────────────────────────────┐  │
│  │         CodeIndexTool (Single MCP Tool)          │  │
│  │  • Accepts natural language queries              │  │
│  │  • Returns formatted results                     │  │
│  └──────────────────┬───────────────────────────────┘  │
│                     │                                  │
│  ┌──────────────────▼───────────────────────────────┐  │
│  │      Query Orchestrator                          │  │
│  │  • Intent Detection                              │  │
│  │  • Query Parsing & Expansion                     │  │
│  │  • Hybrid Search (BM25 + Vector + Graph)         │  │
│  │  • Result Ranking & Context Packaging            │  │
│  └──────────────────┬───────────────────────────────┘  │
│                     │                                  │
│  ┌──────────────────▼───────────────────────────────┐  │
│  │         PostgreSQL + pgvector                    │  │
│  │  • Full-text search (BM25)                       │  │
│  │  • Vector search (HNSW)                          │  │
│  │  • Code graph (symbols, references, calls)       │  │
│  └──────────────────┬───────────────────────────────┘  │
│                     │                                  │
│  ┌──────────────────▼───────────────────────────────┐  │
│  │      Background Indexing Pipeline (COMPLETE)     │  │
│  │  ┌────────────────────────────────────────────┐  │  │
│  │  │  GitTrackerService                         │  │  │
│  │  │  • Clone/Fetch Repos                       │  │  │
│  │  │  • Track Branches                          │  │  │
│  │  │  • Detect Changes (Incremental)            │  │  │
│  │  └────────────────┬───────────────────────────┘  │  │
│  │  ┌────────────────▼───────────────────────────┐  │  │
│  │  │  Parser Service                            │  │  │
│  │  │  • Roslyn (C# precision)                   │  │  │
│  │  │  • Regex-based (Python, JS, Java, Go)      │  │  │
│  │  │  • Symbol Extraction                       │  │  │
│  │  └────────────────┬───────────────────────────┘  │  │
│  │  ┌────────────────▼───────────────────────────┐  │  │
│  │  │  Embedding Service                         │  │  │
│  │  │  • jina-embeddings-v2-base-code            │  │  │
│  │  │  • TEI (Text Embeddings Inference)         │  │  │
│  │  └────────────────┬───────────────────────────┘  │  │
│  │  ┌────────────────▼───────────────────────────┐  │  │
│  │  │  Storage Service                           │  │  │
│  │  │  • Write to PostgreSQL                     │  │  │
│  │  │  • Update indexes                          │  │  │
│  │  └────────────────────────────────────────────┘  │  │
│  └──────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────┘
```

## What's Internal vs External

### ✅ Exposed to Agents (MCP Tools)

- **`Query`** - The ONE tool for all code queries

### 🔒 Internal Services (NOT exposed as MCP tools)

- **GitTrackerService** - Repository cloning, fetching, branch tracking
- **ParserService** - Roslyn and regex-based parsers, symbol extraction
- **EmbeddingService** - Code embeddings generation via TEI
- **StorageService** - PostgreSQL operations
- **QueryOrchestrator** - Intent detection, hybrid search

## Why This Design?

### 1. **Simplicity for Agents**
Agents don't need to understand:
- How Git works
- How to parse code
- How to query databases
- How to rank results

They just ask questions in natural language.

### 2. **Server-Side Intelligence**
The server can:
- Optimize queries based on intent
- Combine multiple search strategies
- Cache and reuse results
- Evolve search algorithms without changing the API

### 3. **Scalability**
- Background indexing runs independently
- Incremental updates minimize work
- Query orchestration can be optimized server-side

### 4. **Flexibility**
The server can return different result types based on query intent:
- Code snippets for search queries
- Symbol definitions for lookup queries
- Call graphs for reference queries
- File changes for recent activity queries

## Current Status (All Steps Complete ✅)

### ✅ Fully Implemented Features

1. **Single MCP Tool**: `Query` tool with natural language interface (per-repository queries)
2. **Git Tracking**: Repositories are cloned, tracked, and persisted to PostgreSQL
3. **Branch Management**: Default + on-demand branch tracking with database persistence
4. **Incremental Updates**: Change detection between commits with SHA cursors
5. **Multi-Language Parsing**:
   - C# via Roslyn (full semantic analysis)
   - Python, JavaScript/TypeScript, Java, Go, Rust via regex-based parsers
6. **Symbol Extraction**: Classes, methods, properties, inheritance, call graphs
7. **PostgreSQL Storage**: Full schema with repositories, branches, commits, files, symbols, edges
8. **Code Chunking**: AST-aware chunking with context overlap
9. **Embedding Generation**: Integration with Text Embeddings Inference (TEI) service
10. **Hybrid Search**: BM25 full-text + vector semantic search + graph re-ranking
11. **Query Orchestration**: Intent detection, query parsing, result ranking
12. **Background Indexing**: Automatic indexing of default branches on startup

### 🎯 Query Capabilities

The `Query` tool supports:

- **Full-text search**: "find all TODO comments"
- **Symbol search**: "show me the UserService class"
- **Reference search**: "what calls the Login method?"
- **Semantic search**: "find code that validates email addresses"
- **Recent changes**: "find recent changes in authentication code"
- **Code navigation**: Symbol definitions, implementations, references

## Testing the Implementation

```bash
# Start the database
cd database
docker compose up -d
./test_setup.sh
cd ..

# Start the embedding service
docker run -d --name text-embeddings -p 8080:80 \
  ghcr.io/huggingface/text-embeddings-inference:cpu-1.8 \
  --model-id jinaai/jina-embeddings-v2-base-code

# Configure repositories in appsettings.json
# Then start the server
dotnet run --project LancerMcp/LancerMcp.csproj

# Query the index with natural language
curl -X POST http://localhost:5171/mcp \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer your-api-key" \
  -d '{
    "jsonrpc": "2.0",
    "method": "tools/call",
    "params": {
      "name": "Query",
      "arguments": {
        "query": "find all authentication code",
        "repository": "my-project"
      }
    },
    "id": 1
  }'
```

## Design Decisions

### Why ONE tool instead of many?

**Original mistake**: I created 4 separate tools:
- `ListRepositories`
- `TrackBranch`
- `GetFileChanges`
- `MarkBranchIndexed`

**Problem**: This exposes internal operations to agents and requires them to understand the indexing pipeline.

**Correct approach**: ONE `Query` tool that handles everything server-side.

### Why natural language queries?

Agents are good at natural language. Let them ask questions naturally:
- ❌ Bad: `SearchSymbols(type="class", implements="IRepository")`
- ✅ Good: `Query("find all classes that implement IRepository")`

The server parses intent and executes the appropriate search strategy.

### Why server-side orchestration?

- **Agents stay simple**: Just ask questions
- **Server stays smart**: Optimize, cache, evolve
- **API stays stable**: Implementation can change without breaking clients

## Implementation Architecture

### Data Flow

1. **Initialization**:
   - GitTrackerService clones repositories
   - Repository metadata persisted to PostgreSQL
   - Existing branch state loaded from database (resumes tracking after restart)
2. **Branch Tracking**:
   - On-demand branch tracking with automatic fetching
   - Branch metadata persisted to database (HEAD SHA, index state)
   - HEAD changes automatically mark branches as `Stale`
3. **Change Detection**: Git diff between last indexed SHA and current HEAD
4. **Parsing**: Language-specific parsers extract symbols and relationships
5. **Chunking**: Code is chunked at function/class boundaries with context overlap
6. **Embedding**: Chunks are sent to TEI service for vector generation
7. **Storage**: All data persisted to PostgreSQL with pgvector indexes
8. **Querying**: Hybrid search combines BM25, vector similarity, and graph traversal

### Branch Persistence Lifecycle

```
Service Start
    ↓
Clone/Open Repository
    ↓
Persist Repository Metadata → PostgreSQL (repositories table)
    ↓
Load Existing Branches ← PostgreSQL (branches table)
    ↓
In-Memory State Populated (BranchState objects)
    ↓
Branch Tracking
    ├─→ New Branch Detected → Create in DB (IndexState: Pending)
    ├─→ HEAD Changed → Update in DB (IndexState: Stale)
    └─→ Indexing Complete → Update in DB (IndexState: Completed)
    ↓
Service Restart → Load from DB (resume tracking)
```

### Key Services

- **GitTrackerService**: Git operations, branch tracking, change detection, database persistence
- **IndexingService**: Orchestrates parsing, chunking, embedding, and storage
- **QueryOrchestrator**: Intent detection, hybrid search, result ranking
- **RoslynParserService**: C# semantic analysis via Roslyn
- **BasicParserService**: Regex-based parsing for other languages
- **ChunkingService**: AST-aware code chunking
- **EmbeddingService**: TEI client for vector generation
- **DatabaseService**: PostgreSQL connection and query execution

## Summary

- **ONE tool for agents**: `Query(query, repo?, branch?)`
- **Server-side intelligence**: Intent detection, hybrid search, ranking
- **Internal services**: Git, parsing, embeddings, storage (not exposed)
- **Natural language**: Agents ask questions, server figures out how to answer
- **Scalable**: Background indexing, incremental updates, optimized queries

This is the correct architecture as specified in the original plan.

