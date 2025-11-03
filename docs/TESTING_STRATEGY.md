# Testing Strategy

## Overview

This document describes the testing strategy for the Project Indexer MCP server, including the use of pre-indexed fixtures for fast, reproducible integration tests.

## Testing Philosophy

The project uses a **fixture-based integration testing** approach where:

1. **Real repositories are indexed once** and the results are saved as fixtures
2. **Tests run against pre-indexed data** for speed and reproducibility
3. **The project indexes itself** as a self-test (dogfooding)
4. **A larger project (Pulsar4x)** is used for scalability testing

This approach provides:
- ✅ **Fast test execution** - No need to re-index on every test run
- ✅ **Reproducible results** - Same input data produces same output
- ✅ **Real-world validation** - Tests use actual codebases, not mocks
- ✅ **End-to-end coverage** - Verifies the complete pipeline

## Test Fixtures

### What Are Fixtures?

Fixtures are pre-generated test data consisting of:

1. **Git mirrors** - Bare Git repositories (`.git` directories)
2. **Database dumps** - PostgreSQL dumps with indexed data
3. **Metadata** - JSON files describing the fixture contents

**⚠️ Important**: Fixtures are **not shipped with the repository** due to their size. They must be generated locally using the `refresh-fixtures.sh` script. The `.gitignore` file excludes:
- `tests/fixtures/dumps/*.dump` - Database dumps
- `tests/fixtures/repos/*` - Git mirrors

This keeps the repository lightweight while allowing developers to generate fixtures on-demand.

### Fixture Repositories

| Repository | Language | Size | Purpose |
|------------|----------|------|---------|
| **project-indexer-mcp** | C# | ~50-100 files | Self-test, verify core functionality |
| **Pulsar4x** | C# | ~500+ files | Scalability, performance, complex queries |

### Fixture Contents

Each fixture includes:
- **Symbols** - Classes, methods, properties, etc.
- **Edges** - Inheritance, calls, references
- **Code chunks** - AST-aware chunks with context
- **Embeddings** - 768-dimensional vectors for semantic search

## Directory Structure

```
project-indexer-mcp/
├── scripts/
│   ├── refresh-fixtures.sh      # Generate new fixtures
│   └── restore-fixtures.sh      # Restore fixtures for testing
├── tests/
│   ├── fixtures/
│   │   ├── repos/               # Git mirrors
│   │   │   ├── project-indexer-mcp.git/
│   │   │   └── Pulsar4x.git/
│   │   ├── dumps/               # PostgreSQL dumps
│   │   │   ├── project_indexer_YYYYMMDD_HHMMSS.dump
│   │   │   ├── project_indexer_YYYYMMDD_HHMMSS.metadata.json
│   │   │   └── project_indexer_latest.dump -> (symlink)
│   │   └── README.md
│   └── ProjectIndexerMcp.IntegrationTests/
│       ├── FixtureTestBase.cs   # Base class for tests
│       ├── QueryOrchestratorTests.cs
│       └── README.md
└── .github/
    └── workflows/
        └── integration-tests.yml # CI workflow
```

## Generating Fixtures

### Prerequisites

- Docker and Docker Compose
- .NET 9.0 SDK
- Git
- Internet connection

### Steps

1. **Run the fixture refresh script:**
   ```bash
   ./scripts/refresh-fixtures.sh
   ```

2. **The script will:**
   - Clone/update Git mirrors
   - Start PostgreSQL
   - Reset database schema
   - Build MCP server
   - **Pause for manual configuration** ⚠️

3. **When prompted, configure repositories in appsettings.json:**

   Edit `ProjectIndexerMcp/appsettings.json`:

   ```json
   {
     "Repositories": [
       {
         "Name": "project-indexer-mcp",
         "RemoteUrl": "file:///absolute/path/to/tests/fixtures/repos/project-indexer-mcp.git",
         "DefaultBranch": "main"
       },
       {
         "Name": "Pulsar4x",
         "RemoteUrl": "file:///absolute/path/to/tests/fixtures/repos/Pulsar4x.git",
         "DefaultBranch": "master"
       }
     ]
   }
   ```

4. **Start the MCP server:**
   ```bash
   dotnet run --project ProjectIndexerMcp/ProjectIndexerMcp.csproj -c Release
   ```

   The server will automatically index the default branches on startup.

5. **After indexing completes, press ENTER** in the script terminal

6. **The script will:**
   - Verify indexed data
   - Create PostgreSQL dump
   - Generate metadata file

### Output

```
tests/fixtures/
├── repos/
│   ├── project-indexer-mcp.git/
│   └── Pulsar4x.git/
└── dumps/
    ├── project_indexer_20251029_123456.dump
    ├── project_indexer_20251029_123456.metadata.json
    └── project_indexer_latest.dump -> project_indexer_20251029_123456.dump
```

## Running Integration Tests

### Option 1: Using the Restore Script (Recommended)

```bash
# Restore fixtures
./scripts/restore-fixtures.sh

# Export environment variables (shown in script output)
export TEST_DB_NAME=project_indexer_test
export TEST_WORKING_DIR=/tmp/project-indexer-test-XXXXXX

# Run tests
dotnet test tests/ProjectIndexerMcp.IntegrationTests --filter Category=Integration
```

### Option 2: Manual Setup

```bash
# Set environment variables
export TEST_DB_NAME=project_indexer_test
export TEST_WORKING_DIR=/tmp/project-indexer-test
export DB_HOST=localhost
export DB_PORT=5432
export DB_USER=postgres
export DB_PASSWORD=postgres

# Start PostgreSQL
cd database
docker compose up -d

# Restore database
docker compose exec -T postgres psql -U postgres -c "CREATE DATABASE $TEST_DB_NAME;"
docker compose exec -T postgres pg_restore -U postgres -d $TEST_DB_NAME \
  < ../tests/fixtures/dumps/project_indexer_latest.dump

# Copy Git mirrors
mkdir -p $TEST_WORKING_DIR
cp -r tests/fixtures/repos/*.git $TEST_WORKING_DIR/

# Run tests
dotnet test tests/ProjectIndexerMcp.IntegrationTests --filter Category=Integration
```

## Writing Integration Tests

### 1. Inherit from FixtureTestBase

```csharp
using ProjectIndexerMcp.IntegrationTests;
using Xunit;

[Trait("Category", "Integration")]
public class MyIntegrationTests : FixtureTestBase
{
    [Fact]
    public async Task MyTest()
    {
        // Verify fixtures are loaded
        await VerifyDatabaseHasData();
        
        // Test code here
    }
}
```

### 2. Use Helper Methods

```csharp
// Query database
var symbols = await QueryAsync<Symbol>("SELECT * FROM symbols WHERE name = @Name", 
    new { Name = "QueryOrchestrator" });

// Execute command
await ExecuteAsync("UPDATE symbols SET indexed_at = NOW() WHERE id = @Id", 
    new { Id = symbolId });

// Get scalar value
var count = await ExecuteScalarAsync<int>("SELECT COUNT(*) FROM symbols");

// Get repository statistics
var stats = await GetRepositoryStatsAsync("project-indexer-mcp");
```

### 3. Test Realistic Scenarios

```csharp
[Fact]
public async Task HybridSearch_ShouldFindRelevantCode()
{
    // Arrange
    var chunkRepo = CreateCodeChunkRepository();
    var embeddingRepo = CreateEmbeddingRepository();
    
    // Get a sample embedding
    var sampleEmbedding = await embeddingRepo.GetByChunkIdAsync(
        await GetFirstChunkIdAsync(), 
        CancellationToken.None);
    
    // Act
    var results = await chunkRepo.HybridSearchAsync(
        queryText: "query orchestrator hybrid search",
        queryVector: sampleEmbedding!.Vector,
        bm25Weight: 0.3f,
        vectorWeight: 0.7f,
        limit: 10,
        CancellationToken.None);
    
    // Assert
    results.Should().NotBeEmpty();
    results.Should().Contain(r => r.Content.Contains("QueryOrchestrator"));
}
```

## Continuous Integration

### GitHub Actions Workflow

The `.github/workflows/integration-tests.yml` workflow:

1. **Starts PostgreSQL** as a service
2. **Installs pgvector** extension
3. **Initializes database schema**
4. **Checks for cached fixtures** (to avoid re-indexing on every CI run)
5. **Restores fixtures** from cache or generates new ones
6. **Runs integration tests**
7. **Uploads test results** and coverage

### Fixture Caching

Fixtures are cached based on:
- Database schema files (`database/schema/**`)
- Indexing service code (`ProjectIndexerMcp/Services/**`)

When these change, fixtures are regenerated.

### CI Performance

- **With cached fixtures:** ~2-5 minutes
- **Without cached fixtures:** ~15-30 minutes (includes indexing)

## Fixture Maintenance

### When to Refresh Fixtures

Refresh fixtures when:
- ✅ Database schema changes
- ✅ Indexing logic changes (parsing, chunking, embedding)
- ✅ Test repositories are updated
- ✅ Embedding model changes
- ✅ Tests fail due to stale data

### How to Refresh

```bash
./scripts/refresh-fixtures.sh
```

### Committing Fixtures

**Option A: Commit dumps (small projects)**
```bash
git add tests/fixtures/dumps/*.dump
git add tests/fixtures/dumps/*.metadata.json
git commit -m "Update test fixtures"
```

**Option B: Use Git LFS (large dumps)**
```bash
git lfs track "tests/fixtures/dumps/*.dump"
git add .gitattributes
git add tests/fixtures/dumps/*.dump
git commit -m "Update test fixtures (LFS)"
```

**Option C: External storage (very large)**
- Store dumps in S3/Azure Blob/GCS
- Download during CI setup
- Keep only metadata in Git

## Best Practices

### DO ✅

- ✅ Use fixtures for integration tests
- ✅ Keep fixtures up-to-date with schema changes
- ✅ Test realistic scenarios (queries an LLM would make)
- ✅ Verify both positive and negative cases
- ✅ Use descriptive test names
- ✅ Log useful debugging information

### DON'T ❌

- ❌ Modify fixtures during tests (read-only)
- ❌ Commit large dumps without Git LFS
- ❌ Skip fixture verification in tests
- ❌ Test implementation details (test behavior)
- ❌ Rely on test execution order
- ❌ Use production data in fixtures

## Troubleshooting

### "No symbols found in database"

**Cause:** Fixtures not properly restored

**Solution:**
```bash
./scripts/refresh-fixtures.sh  # Regenerate
./scripts/restore-fixtures.sh  # Restore
```

### "Git mirror not found"

**Cause:** Git mirrors not copied to test working directory

**Solution:**
```bash
cp -r tests/fixtures/repos/*.git $TEST_WORKING_DIR/
```

### "Connection refused" errors

**Cause:** PostgreSQL not running

**Solution:**
```bash
cd database
docker compose up -d
```

### Tests are slow

**Cause:** Integration tests query real database

**Solutions:**
- Run specific tests: `--filter "FullyQualifiedName~MyTest"`
- Use faster hardware (SSD, more RAM)
- Reduce test data size
- Optimize database indexes

## Performance Benchmarks

Expected test execution times (modern laptop):

| Test Type | Expected Time |
|-----------|---------------|
| Database verification | < 100ms |
| Symbol search | < 50ms |
| Full-text search | < 200ms |
| Vector search | < 500ms |
| Hybrid search | < 1s |
| Complete test suite | < 30s |

## Future Enhancements

- [ ] Automated fixture generation in CI
- [ ] Deterministic embedding stub for reproducibility
- [ ] Performance regression tests
- [ ] Load testing with large repositories
- [ ] Multi-language test fixtures (Python, JS, etc.)
- [ ] Snapshot testing for query results

