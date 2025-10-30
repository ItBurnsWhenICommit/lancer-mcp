# Integration Tests

This directory contains integration tests for the Project Indexer MCP server.

## Overview

Integration tests verify the complete system behavior using pre-indexed fixtures. Unlike unit tests, these tests:
- Use a real PostgreSQL database
- Work with actual Git repositories
- Test the full query pipeline (BM25 + vector + graph)
- Verify end-to-end functionality

## Prerequisites

1. **Docker and Docker Compose** - For PostgreSQL
2. **.NET 9.0 SDK** - For running tests
3. **Test Fixtures** - Pre-indexed data (see below)

## Setting Up Fixtures

Before running integration tests, you must generate test fixtures:

```bash
# From the repository root
./scripts/refresh-fixtures.sh
```

This will:
1. Clone Git mirrors of test repositories
2. Start PostgreSQL
3. Index the repositories
4. Create a database dump

**Note:** The script will pause and ask you to manually index repositories using the MCP server. Follow the on-screen instructions.

## Running Tests

### Option 1: Using the Restore Script (Recommended)

```bash
# Restore fixtures and set environment variables
./scripts/restore-fixtures.sh

# Export the environment variables (shown in script output)
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

# Restore database
cd database
docker compose up -d
docker compose exec -T postgres psql -U postgres -c "CREATE DATABASE $TEST_DB_NAME;"
docker compose exec -T postgres pg_restore -U postgres -d $TEST_DB_NAME < ../tests/fixtures/dumps/project_indexer_latest.dump

# Copy Git mirrors
mkdir -p $TEST_WORKING_DIR
cp -r tests/fixtures/repos/*.git $TEST_WORKING_DIR/

# Run tests
dotnet test tests/ProjectIndexerMcp.IntegrationTests --filter Category=Integration
```

## Test Structure

### FixtureTestBase

Base class for all integration tests. Provides:
- Database connection helpers
- SQL query execution methods
- Git mirror path resolution
- Data verification utilities

Example usage:

```csharp
public class MyIntegrationTest : FixtureTestBase
{
    [Fact]
    public async Task MyTest()
    {
        // Verify fixtures are loaded
        await VerifyDatabaseHasData();
        
        // Query the database
        var symbols = await QueryAsync<Symbol>("SELECT * FROM symbols LIMIT 10");
        
        // Assert
        symbols.Should().NotBeEmpty();
    }
}
```

### Test Categories

Tests are organized by functionality:

- **QueryOrchestratorTests** - Hybrid search, BM25, vector search
- **SymbolRepositoryTests** - Symbol lookup and search
- **EdgeRepositoryTests** - Graph traversal and relationships
- **GitTrackerTests** - Repository tracking and indexing

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `TEST_DB_NAME` | `project_indexer_test` | Test database name |
| `TEST_WORKING_DIR` | (required) | Directory containing Git mirrors |
| `DB_HOST` | `localhost` | PostgreSQL host |
| `DB_PORT` | `5432` | PostgreSQL port |
| `DB_USER` | `postgres` | PostgreSQL username |
| `DB_PASSWORD` | `postgres` | PostgreSQL password |

## Test Data

The fixtures include two repositories:

1. **project-indexer-mcp** - This project (self-test)
   - ~50-100 C# files
   - Tests: symbol extraction, edge creation, chunking

2. **Pulsar4x** - A larger C# project
   - ~500+ C# files
   - Tests: scalability, performance, complex queries

## Cleanup

After running tests, clean up the test environment:

```bash
# If you used restore-fixtures.sh, run the generated cleanup script
$TEST_WORKING_DIR/cleanup.sh

# Or manually:
cd database
docker compose exec -T postgres psql -U postgres -c "DROP DATABASE project_indexer_test;"
rm -rf $TEST_WORKING_DIR
```

## Continuous Integration

Integration tests are designed to run in CI. See `.github/workflows/integration-tests.yml` for the CI configuration.

CI workflow:
1. Checkout code
2. Start PostgreSQL via Docker Compose
3. Run `restore-fixtures.sh` (or download pre-built fixtures from artifact storage)
4. Run integration tests
5. Upload test results and coverage

## Troubleshooting

### "TEST_WORKING_DIR environment variable not set"

Run `./scripts/restore-fixtures.sh` first, then export the environment variables it outputs.

### "Database has no symbols"

The fixture dump may not be properly restored. Try:
```bash
./scripts/refresh-fixtures.sh  # Regenerate fixtures
./scripts/restore-fixtures.sh  # Restore again
```

### "Git mirror not found"

Ensure Git mirrors are copied to `TEST_WORKING_DIR`:
```bash
cp -r tests/fixtures/repos/*.git $TEST_WORKING_DIR/
```

### Tests are slow

Integration tests are slower than unit tests because they:
- Query a real database
- Work with actual Git repositories
- May generate embeddings

Consider:
- Running only specific tests: `dotnet test --filter "FullyQualifiedName~QueryOrchestratorTests"`
- Using a faster machine or SSD
- Reducing test data size (index fewer files)

## Writing New Tests

1. **Inherit from FixtureTestBase:**
   ```csharp
   public class MyTests : FixtureTestBase
   {
       [Fact]
       public async Task MyTest()
       {
           // Test code
       }
   }
   ```

2. **Add the Integration trait:**
   ```csharp
   [Trait("Category", "Integration")]
   public class MyTests : FixtureTestBase
   ```

3. **Use helper methods:**
   ```csharp
   // Query database
   var results = await QueryAsync<MyType>("SELECT * FROM my_table");
   
   // Execute command
   await ExecuteAsync("UPDATE my_table SET foo = @bar", new { bar = "value" });
   
   // Get repository stats
   var stats = await GetRepositoryStatsAsync("project-indexer-mcp");
   ```

4. **Verify fixtures are loaded:**
   ```csharp
   await VerifyDatabaseHasData();
   VerifyGitMirrorExists("project-indexer-mcp");
   ```

## Best Practices

1. **Don't modify fixtures** - Tests should be read-only
2. **Use FluentAssertions** - For readable assertions
3. **Add descriptive test names** - Explain what's being tested
4. **Log useful information** - Use `Console.WriteLine` for debugging
5. **Test realistic scenarios** - Use actual queries an LLM would make
6. **Verify both positive and negative cases** - Test edge cases

## Performance Benchmarks

Expected test execution times (on a modern laptop):

- Database verification: < 100ms
- Symbol search: < 50ms
- Full-text search: < 200ms
- Vector search: < 500ms
- Hybrid search: < 1s
- Complete test suite: < 30s

If tests are significantly slower, investigate:
- Database indexes
- PostgreSQL configuration
- Network latency (if using remote DB)

