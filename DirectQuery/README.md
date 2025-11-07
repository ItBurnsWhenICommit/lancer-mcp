# DirectQuery - Manual MCP Query Tool

A .NET console application that **bypasses the MCP protocol** and directly calls the `QueryOrchestrator` service to query your indexed codebase.

## Why This Tool?

The MCP HTTP transport uses Server-Sent Events (SSE) which requires a persistent bidirectional connection. Simple HTTP clients (curl, Python requests) cannot maintain this connection, making it difficult to manually test queries.

**DirectQuery solves this** by:
- Directly instantiating the `QueryOrchestrator` service
- Bypassing the MCP protocol entirely
- Outputting raw JSON response (exactly what the MCP server would send)
- Colorized console output (warnings in yellow, errors in red)
- No need for persistent connections or SSE

## Prerequisites

1. **PostgreSQL running** with the `lancer` database
2. **Database initialized** with schema
3. **Repository indexed** (run the MCP server at least once)
4. **Embedding service running** (optional, falls back to BM25 if unavailable)

## Usage

### Option 1: Using the wrapper script (recommended)

```bash
./query.sh "Your query here"
```

> The wrapper resolves the repository root automatically and fails fast if the `.NET` CLI is missing, so you can call it from anywhere inside the repo.

### Option 2: Using dotnet run directly

```bash
dotnet run --project DirectQuery/DirectQuery.csproj "Your query here"
```

### Option 3: Build once, run multiple times

```bash
# Build the tool
dotnet build DirectQuery/DirectQuery.csproj

# Run queries
dotnet DirectQuery/bin/Debug/net9.0/DirectQuery.dll "Your query here"
```

## Examples

### Find a class
```bash
./query.sh "Where is the QueryOrchestrator class?"
```

### Find implementations
```bash
./query.sh "find all classes that implement IRepository"
```

### Find method calls
```bash
./query.sh "what calls the QueryAsync method?"
```

### Search for code
```bash
./query.sh "authentication login code"
```

## Output Format

The tool outputs **raw JSON** - exactly what the MCP server would send to a client:

```json
{
  "query": "your query here",
  "intent": 0,
  "results": [
    {
      "id": "uuid",
      "type": "symbol",
      "repository": "lancer-mcp",
      "branch": "main",
      "filePath": "path/to/file.cs",
      "language": 0,
      "symbolName": "ClassName",
      "symbolKind": 1,
      "content": "code content here",
      "startLine": 10,
      "endLine": 20,
      "score": 0.95,
      "bm25Score": 0.8,
      "vectorScore": 0.9,
      "graphScore": 0.7,
      "signature": "public class ClassName",
      "documentation": "Class documentation"
    }
  ],
  "totalResults": 1,
  "executionTimeMs": 123,
  "suggestedQueries": [],
  "metadata": {
    "keywords": ["your", "query"],
    "repository": "lancer-mcp",
    "branch": "all"
  }
}
```

**Console output is colorized:**
- Info messages: Cyan
- Success messages: Green
- Warnings: Yellow
- Errors: Red

## Runtime Defaults

- Repository: `lancer-mcp`
- Maximum results: `50`
- Output format: Raw JSON (MCP server response format)

Edit the `repository` and `maxResults` variables near the top of `Program.cs` if you need different defaults.

## Configuration

The tool uses environment variables for configuration:

| Variable | Default | Description |
|----------|---------|-------------|
| `DB_HOST` | `localhost` | PostgreSQL host |
| `DB_PORT` | `5432` | PostgreSQL port |
| `DB_NAME` | `lancer` | Database name |
| `DB_USER` | `postgres` | PostgreSQL username |
| `DB_PASSWORD` | `postgres` | PostgreSQL password |
| `EMBEDDING_SERVICE_URL` | `http://localhost:8080` | Embedding service URL |

Example with custom configuration:
```bash
DB_HOST=myserver DB_NAME=my_lancer ./query.sh "my query"
```

## How It Works

1. **Initializes services** - Creates instances of:
   - `DatabaseService` - Database connection
   - Repository services (`SymbolRepository`, `EdgeRepository`, etc.)
   - `EmbeddingService` - Embedding generation
   - `QueryOrchestrator` - Query execution engine

2. **Executes query** - Calls `QueryOrchestrator.QueryAsync()` directly

3. **Outputs raw JSON** - Serializes the `QueryResponse` to JSON (exactly what MCP server would send)

## Troubleshooting

### "No results found"

**Possible causes:**
1. Repository hasn't been indexed yet
2. Query doesn't match any indexed code
3. Database is empty

**Solution:** Run the MCP server to index the repository:
```bash
dotnet run --project LancerMcp/LancerMcp.csproj
```

### "Connection failed"

**Possible causes:**
1. PostgreSQL is not running
2. Database doesn't exist
3. Wrong credentials

**Solution:** Check PostgreSQL status:
```bash
docker compose up -d
docker exec -it lancer-postgres psql -U postgres -c "\l"
```

### "Embedding service timeout"

**Possible causes:**
1. Embedding service is not running
2. Service is slow to respond

**Solution:** The tool will fall back to BM25-only search. To use vector search, ensure the embedding service is running:
```bash
docker compose up -d
```

## Comparison with Other Testing Methods

| Method | Pros | Cons |
|--------|------|------|
| **DirectQuery** | Simple<br>Fast<br>Raw JSON output<br>No protocol overhead | Doesn't test MCP protocol<br>Requires .NET |
| **Evaluation Framework** | Tests full MCP protocol<br>Benchmarking<br>LLM integration | Complex setup<br>Requires OpenAI API key |
| **Python scripts** | Simple syntax | Cannot maintain SSE connection<br>Session lost |

## When to Use Each Tool

- **Use DirectQuery** when:
  - You want to quickly test queries
  - You want to see raw query results
  - You're debugging the query engine

- **Use Evaluation Framework** when:
  - You want to test the full MCP protocol
  - You want to benchmark performance
  - You want to test with real LLM integration

- **Use Python scripts** when:
  - You want to see the MCP protocol format
  - You want to understand how MCP works
  - You're learning about SSE transport

## See Also

- [Testing Strategy](../docs/TESTING_STRATEGY.md) - Complete testing documentation
- [Query Orchestration Guide](../docs/QUERY_ORCHESTRATION_GUIDE.md) - Query engine details
- [Integration Tests](../tests/LancerMcp.IntegrationTests/) - Automated testing
