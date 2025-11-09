# Query Orchestration Guide

This guide explains how the hybrid query orchestration system works in the Lancer MCP service.

## Overview

The QueryOrchestrator service implements a sophisticated hybrid search system that combines:
- **BM25 Full-Text Search** - Traditional keyword-based search
- **Vector Similarity Search** - Semantic search using embeddings
- **Graph Re-Ranking** - Boost results based on symbol relationships

## Architecture

```
User Query → Intent Detection → Query Parsing → Search Execution → Re-Ranking → Results
```

### 1. Intent Detection

The system automatically detects the intent of your query:

| Intent | Patterns | Example Queries |
|--------|----------|-----------------|
| **Navigation** | "find", "show", "where is", "locate", "definition of" | "where is the UserService class?" |
| **Relations** | "calls", "references", "uses", "depends on", "call chain" | "what calls the Login method?" |
| **Documentation** | "what is", "explain", "describe", "how does" | "explain how authentication works" |
| **Examples** | "example", "usage", "how to use", "sample" | "show me examples of using the API" |
| **Search** | (default) | "find authentication code" |

### 2. Query Parsing

The parser extracts:
- **Keywords** - Meaningful terms (stop words removed)
- **Symbol Names** - CamelCase or snake_case identifiers
- **File Paths** - Paths like `src/Program.cs`
- **Language Filter** - If specified
- **Branch Filter** - If specified

### 3. Search Execution

Based on the detected intent, different search strategies are used:

#### Navigation Search
- Searches for specific symbols by name
- Uses exact and fuzzy matching
- Returns symbol definitions with signatures

#### Relations Search
- Finds symbols mentioned in the query
- Retrieves incoming and outgoing edges (calls, references)
- Returns symbols with their relationships

#### Hybrid Search (Default)
1. **Generate Query Embedding** - Convert query to 768-dimensional vector
2. **Execute Hybrid Search** - Call PostgreSQL `hybrid_search()` function
3. **Combine Scores** - BM25 (30%) + Vector (70%)
4. **Fallback** - If embeddings fail, use full-text search only

### 4. Graph Re-Ranking

For relation queries, results are re-ranked based on:
- Number of incoming edges (references to this symbol)
- Number of outgoing edges (what this symbol calls)
- Centrality in the call graph

Formula:
```
graphScore = min(1.0, (outgoingCount + incomingCount * 2) / 20.0)
finalScore = originalScore * 0.7 + graphScore * 0.3
```

### 5. Result Formatting

Results are optimized for AI consumption with minimal payload size:
- **Location** - `file` (path), `lines` (range like "56-107")
- **Score** - Overall relevance score (0.0-1.0)
- **Type** - Result type (e.g., "symbol", "caller", "code_chunk")
- **Symbol Info** - `symbol` (name), `kind` (e.g., "Method", "Class") - only if present
- **Code** - `sig` (signature) or `content` (code snippet) - only if present
- **Documentation** - `docs` - only if present
- **Related Symbols** - `related` array - only for relation queries

**Note**: Repository and branch are at the top level only, not duplicated per result.

## Usage Examples

### Example 1: Simple Search

**Query:** "authentication login"

**Intent:** Search

**Process:**
1. Extract keywords: ["authentication", "login"]
2. Generate embedding for "authentication login"
3. Execute hybrid search (BM25 + vector)
4. Return top 50 results

**Response:**
```json
{
  "repo": "my-app",
  "branch": "main",
  "query": "authentication login",
  "intent": "Search",
  "total": 42,
  "results": [
    {
      "file": "src/Services/AuthService.cs",
      "lines": "45-52",
      "score": 0.92,
      "type": "code_chunk",
      "symbol": "Login",
      "kind": "Method",
      "sig": "public async Task<LoginResult> Login(string username, string password)"
    }
  ],
  "suggestions": [
    "Show me how Login is used",
    "What calls Login?",
    "Find similar code to Login"
  ]
}
```

### Example 2: Navigation

**Query:** "where is the UserService class?"

**Intent:** Navigation

**Process:**
1. Detect navigation intent
2. Extract symbol name: "UserService"
3. Search symbols by name
4. Return exact matches

**Response:**
```json
{
  "repo": "my-app",
  "branch": "main",
  "query": "where is the UserService class?",
  "intent": "Navigation",
  "total": 1,
  "results": [
    {
      "file": "src/Services/UserService.cs",
      "lines": "10-150",
      "score": 1.0,
      "type": "symbol",
      "symbol": "UserService",
      "kind": "Class",
      "sig": "public class UserService : IUserService"
    }
  ]
}
```

### Example 3: Relations

**Query:** "what calls the Login method?"

**Intent:** Relations

**Process:**
1. Detect relations intent
2. Find "Login" symbol
3. Get incoming edges (callers)
4. Return symbol with related symbols

**Response:**
```json
{
  "repo": "my-app",
  "branch": "main",
  "query": "what calls the Login method?",
  "intent": "Relations",
  "total": 1,
  "results": [
    {
      "file": "src/Services/AuthService.cs",
      "lines": "45-52",
      "score": 0.95,
      "type": "symbol_with_relations",
      "symbol": "Login",
      "kind": "Method",
      "sig": "public async Task<LoginResult> Login(string username, string password)",
      "related": [
        {
          "name": "LoginController.Post",
          "kind": "Method",
          "rel": "referenced_by_Call",
          "file": "src/Controllers/LoginController.cs",
          "line": 25
        },
        {
          "name": "AuthenticationTests.TestLogin",
          "kind": "Method",
          "rel": "referenced_by_Call",
          "file": "tests/AuthenticationTests.cs",
          "line": 42
        }
      ]
    }
  ]
}
```

## Configuration

### Hybrid Search Weights

You can adjust the weights in `QueryOrchestrator.cs`:

```csharp
var hybridResults = await _embeddingRepository.HybridSearchAsync(
    queryText: parsedQuery.OriginalQuery,
    queryVector: queryEmbedding,
    repoId: parsedQuery.RepositoryName,
    branchName: parsedQuery.BranchName,
    bm25Weight: 0.3f,    // 30% BM25
    vectorWeight: 0.7f,  // 70% Vector
    limit: parsedQuery.MaxResults * 2,
    cancellationToken);
```

**Recommendations:**
- **Code Search:** BM25: 0.3, Vector: 0.7 (default)
- **Keyword-Heavy:** BM25: 0.5, Vector: 0.5
- **Semantic Search:** BM25: 0.2, Vector: 0.8

### Graph Re-Ranking Weights

```csharp
result = result with
{
    GraphScore = graphScore,
    Score = result.Score * 0.7f + graphScore * 0.3f  // 70% search, 30% graph
};
```

## Performance Optimization

### 1. Database Indexes

Ensure these indexes exist (created by migration scripts):
- **HNSW** for vector similarity (fast approximate nearest neighbor)
- **GIN** for full-text search (fast keyword matching)
- **B-tree** for symbol lookups (fast exact matching)

### 2. Result Limiting

- Default: 50 results
- Hybrid search fetches 2x results for re-ranking
- Adjust `maxResults` parameter as needed

### 3. Caching

Consider adding caching for:
- Frequently queried symbols
- Popular search queries
- Embedding vectors

## Troubleshooting

### No Results Returned

**Possible Causes:**
1. Repository not indexed yet
2. Branch not tracked
3. Embeddings not generated

**Solutions:**
1. Check indexing status in logs
2. Ensure branch is tracked: `EnsureBranchTrackedAsync()`
3. Verify embedding service is running

### Low-Quality Results

**Possible Causes:**
1. Embeddings not available (falling back to BM25 only)
2. Query too vague
3. Weights not tuned for your use case

**Solutions:**
1. Check embedding service logs
2. Use more specific queries
3. Adjust BM25/vector weights

### Slow Queries

**Possible Causes:**
1. Large result set
2. Missing indexes
3. Graph re-ranking on large graphs

**Solutions:**
1. Reduce `maxResults`
2. Run `ANALYZE` on PostgreSQL tables
3. Limit graph traversal depth

## Advanced Features

### Custom Intent Detection

Add custom patterns in `QueryOrchestrator.cs`:

```csharp
private static readonly Regex CustomPattern = new(
    @"\b(your|custom|pattern)\b",
    RegexOptions.IgnoreCase | RegexOptions.Compiled);
```

### Language Filtering

Filter by programming language:

```csharp
var response = await _queryOrchestrator.QueryAsync(
    query: "authentication",
    repositoryName: "my-repo",
    branchName: "main",
    language: Language.CSharp,  // Only C# results
    maxResults: 50,
    cancellationToken);
```

## Next Steps

1. **Test the system** - Run queries and verify results
2. **Tune weights** - Adjust BM25/vector/graph weights for your use case
3. **Monitor performance** - Track query execution times
4. **Add caching** - Cache frequent queries for better performance
5. **Extend intents** - Add custom intent patterns for your domain

## See Also

- [Storage Layer Setup Guide](STORAGE_LAYER_SETUP.md)
- [Database Schema](database/README.md)
- [Architecture Overview](ARCHITECTURE.md)

