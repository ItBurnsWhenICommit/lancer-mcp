# Test Fixtures

This directory contains pre-indexed test fixtures for integration testing.

**⚠️ Important**: Fixtures are **not shipped with the repository** and must be generated locally using `scripts/refresh-fixtures.sh`. See the "Generating Fixtures" section below.

## Overview

The test fixtures consist of:
1. **Git mirrors** - Bare Git repositories used for testing
2. **Database dumps** - PostgreSQL dumps with pre-indexed data
3. **Metadata** - JSON files describing the fixture contents

**Why not ship fixtures?**
- Database dumps can be 100+ MB
- Git mirrors can be 50+ MB
- Keeps the repository lightweight
- Ensures fixtures match your local environment

## Repositories

The fixtures include two repositories:

1. **lancer-mcp** - This project itself (self-test)
   - URL: https://github.com/ItBurnsWhenICommit/lancer-mcp.git
   - Language: C#
   - Purpose: Verify the system can index itself

2. **Pulsar4x** - A 4X space strategy game
   - URL: https://github.com/Pulsar4xDevs/Pulsar4x.git
   - Language: C#
   - Purpose: Test with a larger, real-world codebase

## Directory Structure

```
tests/fixtures/
├── repos/                          # Git mirrors (bare repositories)
│   ├── lancer-mcp.git/            # This project's mirror
│   └── Pulsar4x.git/              # Pulsar4x mirror
├── dumps/                          # PostgreSQL dumps
│   ├── lancer_YYYYMMDD_HHMMSS.dump
│   ├── lancer_YYYYMMDD_HHMMSS.metadata.json
│   └── lancer_latest.dump -> (symlink to latest)
└── README.md                       # This file
```

## Generating Fixtures

### Prerequisites

1. Docker and Docker Compose (for PostgreSQL)
2. .NET 9.0 SDK
3. Git
4. Internet connection (for cloning repositories)

### Steps

Run the fixture refresh script:

```bash
./scripts/refresh-fixtures.sh
```

This script will:
1. Clone or update Git mirrors to `tests/fixtures/repos/`
2. Start PostgreSQL via Docker Compose
3. Reset and initialize the database schema
4. Build the MCP server
5. **Prompt you to manually index the repositories**
6. Verify the indexed data
7. Create a PostgreSQL dump in `tests/fixtures/dumps/`
8. Generate metadata JSON file

### Manual Indexing Step

When the script prompts you, follow these steps:

1. **Start the MCP server:**
   ```bash
   dotnet run --project LancerMcp/LancerMcp.csproj -c Release
   ```

2. **Configure repositories in appsettings.json:**

   Edit `LancerMcp/appsettings.json` to add the fixture repositories:

   ```json
   {
     "Repositories": [
       {
         "Name": "lancer-mcp",
         "RemoteUrl": "file:///absolute/path/to/tests/fixtures/repos/lancer-mcp.git",
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

   **Note**: Use absolute paths for the `RemoteUrl` field.

3. **Start the MCP server:**

   ```bash
   dotnet run --project LancerMcp/LancerMcp.csproj -c Release
   ```

   The server will automatically:
   - Clone the repositories
   - Index the default branches
   - Generate embeddings
   - Store data in PostgreSQL

4. **Wait for indexing to complete**

   Monitor the server logs for completion messages:
   ```
   [INFO] Indexed 1234 files from lancer-mcp/main
   [INFO] Generated 5678 embeddings
   [INFO] Completed automatic indexing of default branches
   ```

5. **Press ENTER in the script terminal** to continue with the database dump

## Using Fixtures in Tests

### Restore Database

```bash
# Using Docker Compose
cd database
docker compose exec -T postgres pg_restore -U postgres -d test_db < ../tests/fixtures/dumps/lancer_latest.dump

# Or using pg_restore directly
pg_restore -U postgres -d test_db tests/fixtures/dumps/lancer_latest.dump
```

### Copy Git Mirrors

```bash
# Copy to test working directory
cp -r tests/fixtures/repos/lancer-mcp.git /tmp/test-working-dir/
cp -r tests/fixtures/repos/Pulsar4x.git /tmp/test-working-dir/
```

### Integration Test Pattern

```csharp
[Fact]
public async Task TestHybridSearch_WithFixtures()
{
    // 1. Restore database from fixture
    await RestoreDatabaseDump("tests/fixtures/dumps/lancer_latest.dump");
    
    // 2. Copy Git mirrors to temp directory
    var workingDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    CopyDirectory("tests/fixtures/repos", workingDir);
    
    // 3. Start MCP server (no indexing needed - data already in DB)
    var server = await StartMcpServer(workingDir, skipIndexing: true);
    
    // 4. Execute test queries
    var result = await server.Query("find all classes in lancer-mcp");

    // 5. Assert results
    Assert.NotEmpty(result.Results);
    Assert.Contains(result.Results, r => r.SymbolName == "QueryOrchestrator");
    
    // 6. Cleanup
    Directory.Delete(workingDir, recursive: true);
}
```

## Fixture Metadata

Each dump has an accompanying `.metadata.json` file with:

```json
{
  "created_at": "2025-10-29T12:34:56Z",
  "repositories": [
    {
      "name": "lancer-mcp",
      "url": "https://github.com/ItBurnsWhenICommit/lancer-mcp.git",
      "mirror_path": "tests/fixtures/repos/lancer-mcp.git"
    }
  ],
  "statistics": {
    "symbols": 1234,
    "code_chunks": 5678,
    "embeddings": 5678
  },
  "database": {
    "name": "lancer",
    "dump_file": "lancer_20251029_123456.dump"
  }
}
```

## Refreshing Fixtures

Fixtures should be refreshed when:
- Database schema changes
- Indexing logic changes
- Test repositories are updated
- Embedding model changes

Simply run:
```bash
./scripts/refresh-fixtures.sh
```

## Git LFS Considerations

Database dumps can be large (100MB+). Consider:

1. **Option A: Git LFS**
   ```bash
   git lfs track "tests/fixtures/dumps/*.dump"
   git add .gitattributes
   ```

2. **Option B: External Storage**
   - Store dumps in S3/Azure Blob/GCS
   - Download during CI setup
   - Keep only metadata in Git

3. **Option C: Generate on Demand**
   - Don't commit dumps
   - Run `refresh-fixtures.sh` in CI before tests
   - Slower but no storage concerns

## Troubleshooting

### "No symbols found" error

The indexing step may have failed. Check:
- MCP server logs for errors
- PostgreSQL connection
- Embedding service availability (if using real embeddings)

### Dump restore fails

Ensure schema versions match:
```bash
# Check dump version
pg_restore -l tests/fixtures/dumps/lancer_latest.dump | head

# Ensure migrations are current
cd database
docker compose exec -T postgres psql -U postgres -d test_db < schema/00_extensions.sql
# ... run all migrations
```

### Git mirror is stale

Update mirrors manually:
```bash
cd tests/fixtures/repos/lancer-mcp.git
git fetch --all --prune
```

## CI Integration

See `.github/workflows/integration-tests.yml` for automated fixture usage in CI.

## Notes

- Fixtures use **deterministic data** - same input repos produce same output
- Embeddings may vary if using a real embedding service (consider using a stub for reproducibility)
- Git mirrors are bare repositories (no working directory)
- Database dumps are in PostgreSQL custom format (binary, compressed)

