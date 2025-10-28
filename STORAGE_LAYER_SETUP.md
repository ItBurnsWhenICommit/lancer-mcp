# Storage Layer Setup Guide

This guide explains how to set up and test the PostgreSQL storage layer for the Project Indexer MCP service.

## Prerequisites

1. **Docker** - For running PostgreSQL with pgvector
2. **.NET 9.0 SDK** - For building and running the application
3. **PostgreSQL Client** (optional) - For manual database inspection

## Quick Start

### 1. Start the Database

Navigate to the `database` directory and start PostgreSQL using Docker Compose:

```bash
cd database
docker compose up -d
```

This will:
- Start PostgreSQL 16 with pgvector extension
- Create the `project_indexer` database
- Run all migration scripts automatically
- Expose PostgreSQL on port 5432

### 2. Verify Database Setup

Check that the database is running:

```bash
docker compose ps
```

You should see the `postgres` container running.

### 3. Run the Application

The application is now configured to connect to the database automatically. The connection settings are in `appsettings.json`:

```json
{
  "DatabaseHost": "localhost",
  "DatabasePort": 5432,
  "DatabaseName": "project_indexer",
  "DatabaseUser": "postgres",
  "DatabasePassword": "postgres",
  "DatabaseMaxPoolSize": 100,
  "DatabaseMinPoolSize": 10,
  "DatabaseCommandTimeoutSeconds": 30
}
```

Build and run the application:

```bash
dotnet build
dotnet run --project ProjectIndexerMcp
```

### 4. Test Database Connectivity

To manually test the database connection, you can use the `DatabaseConnectionTest` class:

```csharp
// In your Program.cs or a test file
await DatabaseConnectionTest.RunAsync(app.Services);
```

This will run a series of tests:
1. ✓ Basic database connection
2. ✓ Query existing repositories
3. ✓ Create a test repository
4. ✓ Retrieve the created repository
5. ✓ Update the repository
6. ✓ Check if repository exists
7. ✓ Delete the test repository
8. ✓ Verify deletion

## Architecture Overview

### Database Service

The `DatabaseService` class provides:
- Connection pooling using `NpgsqlDataSource`
- Query execution methods using Dapper
- Transaction support
- pgvector type handling

### Repository Pattern

Each database table has a corresponding repository:

| Repository | Interface | Purpose |
|------------|-----------|---------|
| `RepositoryRepository` | `IRepositoryRepository` | Manage repositories |
| `BranchRepository` | `IBranchRepository` | Manage branches |
| `CommitRepository` | `ICommitRepository` | Manage commits |
| `FileRepository` | `IFileRepository` | Manage files |
| `SymbolRepository` | `ISymbolRepository` | Manage code symbols |
| `EdgeRepository` | `IEdgeRepository` | Manage symbol relationships |
| `CodeChunkRepository` | `ICodeChunkRepository` | Manage code chunks |
| `EmbeddingRepository` | `IEmbeddingRepository` | Manage embeddings and vector search |

### Service Registration

All services are registered in `Program.cs`:

```csharp
// Database services
builder.Services.AddSingleton<DatabaseService>();

// Repository services
builder.Services.AddSingleton<IRepositoryRepository, RepositoryRepository>();
builder.Services.AddSingleton<IBranchRepository, BranchRepository>();
builder.Services.AddSingleton<ICommitRepository, CommitRepository>();
builder.Services.AddSingleton<IFileRepository, FileRepository>();
builder.Services.AddSingleton<ISymbolRepository, SymbolRepository>();
builder.Services.AddSingleton<IEdgeRepository, EdgeRepository>();
builder.Services.AddSingleton<ICodeChunkRepository, CodeChunkRepository>();
builder.Services.AddSingleton<IEmbeddingRepository, EmbeddingRepository>();
```

## Usage Examples

### Creating a Repository

```csharp
var repoRepository = services.GetRequiredService<IRepositoryRepository>();

var repo = new Repository
{
    Id = Guid.NewGuid().ToString(),
    Name = "my-project",
    RemoteUrl = "https://github.com/user/my-project.git",
    DefaultBranch = "main",
    CreatedAt = DateTimeOffset.UtcNow,
    UpdatedAt = DateTimeOffset.UtcNow
};

var created = await repoRepository.CreateAsync(repo);
```

### Storing Symbols

```csharp
var symbolRepository = services.GetRequiredService<ISymbolRepository>();

var symbol = new Symbol
{
    Id = Guid.NewGuid().ToString(),
    RepositoryName = "my-project",
    BranchName = "main",
    CommitSha = "abc123",
    FilePath = "src/Program.cs",
    Name = "Main",
    Kind = SymbolKind.Method,
    Language = Language.CSharp,
    StartLine = 10,
    StartColumn = 5,
    EndLine = 20,
    EndColumn = 6
};

await symbolRepository.CreateAsync(symbol);
```

### Batch Operations

```csharp
var symbolRepository = services.GetRequiredService<ISymbolRepository>();

var symbols = new List<Symbol> { /* ... */ };
var count = await symbolRepository.CreateBatchAsync(symbols);
Console.WriteLine($"Inserted {count} symbols");
```

### Full-Text Search

```csharp
var chunkRepository = services.GetRequiredService<ICodeChunkRepository>();

var results = await chunkRepository.SearchFullTextAsync(
    repoId: "repo-id",
    query: "authentication login",
    branchName: "main",
    language: Language.CSharp,
    limit: 50
);
```

### Vector Similarity Search

```csharp
var embeddingRepository = services.GetRequiredService<IEmbeddingRepository>();

var queryVector = new float[768]; // Your embedding vector
var results = await embeddingRepository.SearchBySimilarityAsync(
    queryVector: queryVector,
    repoId: "repo-id",
    branchName: "main",
    limit: 50
);

foreach (var (embedding, distance) in results)
{
    Console.WriteLine($"Chunk {embedding.ChunkId}: distance = {distance}");
}
```

### Hybrid Search (BM25 + Vector)

```csharp
var embeddingRepository = services.GetRequiredService<IEmbeddingRepository>();

var queryText = "user authentication";
var queryVector = new float[768]; // Your embedding vector

var results = await embeddingRepository.HybridSearchAsync(
    queryText: queryText,
    queryVector: queryVector,
    repoId: "repo-id",
    branchName: "main",
    bm25Weight: 0.3f,
    vectorWeight: 0.7f,
    limit: 50
);

foreach (var (chunkId, score, bm25Score, vectorScore) in results)
{
    Console.WriteLine($"Chunk {chunkId}: score = {score} (BM25: {bm25Score}, Vector: {vectorScore})");
}
```

## Database Schema

The database includes:

### Tables
- `repos` - Repository metadata
- `branches` - Branch information and indexing state
- `commits` - Commit history
- `files` - File metadata
- `symbols` - Code symbols (classes, methods, etc.)
- `edges` - Symbol relationships (calls, imports, etc.)
- `code_chunks` - Code chunks for embedding
- `embeddings` - Vector embeddings (768 dimensions)

### Indexes
- B-tree indexes for primary keys and foreign keys
- GIN indexes for full-text search
- HNSW indexes for vector similarity search
- Trigram indexes for fuzzy text matching

### Functions
- `hybrid_search()` - Combines BM25 and vector search
- `search_chunks_fulltext()` - Full-text search on code chunks
- `search_embeddings_cosine()` - Vector similarity search (cosine)
- `search_embeddings_l2()` - Vector similarity search (L2)
- `find_references()` - Find all references to a symbol
- `find_dependencies()` - Find all dependencies of a symbol
- `find_call_chain()` - Find call chains between symbols
- `search_symbols()` - Search symbols by name

### Materialized Views
- `repo_stats` - Repository statistics
- `branch_stats` - Branch statistics
- `language_stats` - Language distribution
- `hot_symbols` - Most referenced symbols
- `symbol_complexity` - Symbol complexity metrics
- `file_stats` - File statistics
- `chunk_stats` - Code chunk statistics
- `embedding_coverage` - Embedding coverage statistics

## Troubleshooting

### Connection Refused

If you get a connection refused error:
1. Check that Docker is running: `docker ps`
2. Check that PostgreSQL is running: `docker compose ps`
3. Check the logs: `docker compose logs postgres`

### Migration Errors

If migrations fail:
1. Check the migration logs: `docker compose logs postgres`
2. Manually run migrations: `cd database && ./migrate.sh`
3. Reset the database: `docker compose down -v && docker compose up -d`

### Permission Errors

If you get permission errors:
1. Make sure the migration script is executable: `chmod +x database/migrate.sh`
2. Check Docker permissions: `sudo usermod -aG docker $USER`

## Next Steps

Now that the storage layer is set up, you can:

1. **Update IndexingService** to persist parsed data to the database
2. **Implement QueryOrchestrator** to use hybrid search for code queries
3. **Add embedding generation** for code chunks
4. **Create background jobs** to keep the index up-to-date

See the main README for more information on the overall architecture.

