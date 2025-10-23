# Architecture - Project Indexer MCP

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
  query: string,           // Natural language query
  repository?: string,     // Optional: filter to specific repo
  branch?: string,         // Optional: filter to specific branch
  maxResults?: number      // Optional: limit results
) -> QueryResult
```

### Example Queries

```javascript
// Code search
Query("find all classes that implement IRepository")

// Symbol lookup
Query("show me the definition of UserService")

// Find references
Query("what calls the Login method?")

// Recent changes
Query("find recent changes in authentication code")

// Cross-repository search
Query("find all uses of dependency injection", repository: "my-api")

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
┌────────────────────▼────────────────────────────────────┐
│              MCP Server (ASP.NET Core)                  │
│  ┌──────────────────────────────────────────────────┐  │
│  │         CodeIndexTool (Single MCP Tool)          │  │
│  │  • Accepts natural language queries              │  │
│  │  • Returns formatted results                     │  │
│  └──────────────────┬───────────────────────────────┘  │
│                     │                                   │
│  ┌──────────────────▼───────────────────────────────┐  │
│  │      Query Orchestrator (Future - Step 6)        │  │
│  │  • Intent Detection                              │  │
│  │  • Query Parsing & Expansion                     │  │
│  │  • Hybrid Search (BM25 + Vector + Graph)         │  │
│  │  • Result Ranking & Context Packaging            │  │
│  └──────────────────┬───────────────────────────────┘  │
│                     │                                   │
│  ┌──────────────────▼───────────────────────────────┐  │
│  │         PostgreSQL + pgvector (Step 4)           │  │
│  │  • Full-text search (BM25)                       │  │
│  │  • Vector search (HNSW)                          │  │
│  │  • Code graph (symbols, references, calls)       │  │
│  └──────────────────┬───────────────────────────────┘  │
│                     │                                   │
│  ┌──────────────────▼───────────────────────────────┐  │
│  │      Background Indexing Pipeline (Step 8)       │  │
│  │  ┌────────────────────────────────────────────┐  │  │
│  │  │  GitTrackerService (Step 2 - COMPLETE)    │  │  │
│  │  │  • Clone/Fetch Repos                      │  │  │
│  │  │  • Track Branches                         │  │  │
│  │  │  • Detect Changes (Incremental)           │  │  │
│  │  └────────────────┬───────────────────────────┘  │  │
│  │  ┌────────────────▼───────────────────────────┐  │  │
│  │  │  Parser Service (Step 3 - TODO)           │  │  │
│  │  │  • Tree-sitter (multi-language)           │  │  │
│  │  │  • Roslyn (C# precision)                  │  │  │
│  │  │  • Symbol Extraction                      │  │  │
│  │  └────────────────┬───────────────────────────┘  │  │
│  │  ┌────────────────▼───────────────────────────┐  │  │
│  │  │  Embedding Service (Step 5 - TODO)        │  │  │
│  │  │  • jina-embeddings-v2-base-code           │  │  │
│  │  │  • ONNX Runtime or Python sidecar         │  │  │
│  │  └────────────────┬───────────────────────────┘  │  │
│  │  ┌────────────────▼───────────────────────────┐  │  │
│  │  │  Storage Service (Step 4 - TODO)          │  │  │
│  │  │  • Write to PostgreSQL                    │  │  │
│  │  │  • Update indexes                         │  │  │
│  │  └────────────────────────────────────────────┘  │  │
│  └──────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

## What's Internal vs External

### ✅ Exposed to Agents (MCP Tools)

- **`Query`** - The ONE tool for all code queries

### 🔒 Internal Services (NOT exposed as MCP tools)

- **GitTrackerService** - Repository cloning, fetching, branch tracking
- **ParserService** (TODO) - Tree-sitter, Roslyn, symbol extraction
- **EmbeddingService** (TODO) - Code embeddings generation
- **StorageService** (TODO) - PostgreSQL operations
- **QueryOrchestrator** (TODO) - Intent detection, hybrid search

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

## Current Status (Step 2 Complete)

### ✅ What Works Now

1. **Single MCP Tool**: `Query` tool is exposed
2. **Git Tracking**: Repositories are cloned and tracked
3. **Branch Management**: Default + on-demand branch tracking
4. **Incremental Updates**: Change detection between commits
5. **Basic Query Response**: Returns repository status (placeholder)

### 🚧 What's Coming (Steps 3-8)

1. **Step 3**: Multi-language parsing & symbol extraction
2. **Step 4**: PostgreSQL + pgvector storage
3. **Step 5**: Embedding generation
4. **Step 6**: Query orchestrator with hybrid search
5. **Step 7**: Full `Query` tool implementation
6. **Step 8**: Background indexing pipeline

## Testing the Current Implementation

```bash
# Start the server
dotnet run --project ProjectIndexerMcp/ProjectIndexerMcp.csproj

# Query the index (currently returns repo status)
curl -X POST http://localhost:5171/mcp \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer your-api-key" \
  -d '{
    "jsonrpc": "2.0",
    "method": "tools/call",
    "params": {
      "name": "Query",
      "arguments": {
        "query": "find all authentication code"
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

## Future Query Types

The `Query` tool will eventually support:

1. **Full-text search**: "find all TODO comments"
2. **Symbol search**: "show me the UserService class"
3. **Reference search**: "what calls the Login method?"
4. **Call graph**: "show me the call chain for ProcessPayment"
5. **Recent changes**: "what changed in the last week?"
6. **Cross-repository**: "find all uses of ILogger across all repos"
7. **Semantic search**: "find code that validates email addresses"
8. **Code navigation**: "show me the implementation of this interface"

All through ONE tool with natural language queries.

## Summary

- **ONE tool for agents**: `Query(query, repo?, branch?)`
- **Server-side intelligence**: Intent detection, hybrid search, ranking
- **Internal services**: Git, parsing, embeddings, storage (not exposed)
- **Natural language**: Agents ask questions, server figures out how to answer
- **Scalable**: Background indexing, incremental updates, optimized queries

This is the correct architecture as specified in the original plan.

