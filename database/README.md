# Database Setup - Project Indexer MCP

This directory contains the PostgreSQL database schema and migration scripts for the Project Indexer MCP service.

## Overview

The database is designed to support:
- **Full-text search** using PostgreSQL's built-in FTS with BM25-style ranking
- **Vector similarity search** using pgvector with HNSW indexes
- **Graph traversal** for code relationships (calls, references, inheritance)
- **Hybrid search** combining BM25 + vector search + graph re-ranking
- **Incremental indexing** with branch and commit tracking
- **Persistent branch state** - Repository and branch metadata survives service restarts

## Prerequisites

### PostgreSQL 16+
```bash
# Ubuntu/Debian
sudo apt-get install postgresql-16

# macOS (Homebrew)
brew install postgresql@16

# Start PostgreSQL
sudo systemctl start postgresql  # Linux
brew services start postgresql@16  # macOS
```

### pgvector Extension
```bash
# Ubuntu/Debian
sudo apt-get install postgresql-16-pgvector

# macOS (Homebrew)
brew install pgvector

# Or build from source
git clone https://github.com/pgvector/pgvector.git
cd pgvector
make
sudo make install
```

### pg_trgm Extension
This is included with PostgreSQL by default, no additional installation needed.

## Quick Start

### Option 1: Using Docker (Recommended for Development)

```bash
# Start PostgreSQL with pgvector
cd database
docker-compose up -d

# Wait for PostgreSQL to be ready
docker-compose logs -f postgres

# Run migrations
./migrate.sh
```

### Option 2: Using Existing PostgreSQL

#### 1. Set Environment Variables (Optional)
```bash
export DB_HOST=localhost
export DB_PORT=5432
export DB_NAME=project_indexer
export DB_USER=postgres
export DB_PASSWORD=postgres
```

#### 2. Run Migrations
```bash
cd database
./migrate.sh
```

This will:
1. Check PostgreSQL connection
2. Create the database if it doesn't exist
3. Run all migration files in order:
   - `00_extensions.sql` - Install pgvector, pg_trgm, btree_gin
   - `01_enums.sql` - Create enums for Language, SymbolKind, EdgeKind
   - `02_tables.sql` - Create main tables (repos, branches, commits, files, symbols, edges)
   - `03_chunks_embeddings.sql` - Create code_chunks and embeddings tables with vector indexes
   - `04_functions.sql` - Create search and graph traversal functions
   - `05_materialized_views.sql` - Create statistics and analytics views

### 3. Verify Installation
```bash
psql -h localhost -U postgres -d project_indexer
```

```sql
-- Check extensions
SELECT * FROM pg_extension WHERE extname IN ('vector', 'pg_trgm', 'btree_gin');

-- Check tables
\dt

-- Check materialized views
\dm

-- Check functions
\df
```

## Database Schema

### Core Tables

#### repos
Stores repository metadata.
- `id` - Unique identifier (GUID)
- `name` - Repository name (unique)
- `remote_url` - Git remote URL
- `default_branch` - Default branch name

#### branches
Tracks branches and their indexing state.
- `id` - Unique identifier
- `repo_id` - Foreign key to repos
- `name` - Branch name
- `head_commit_sha` - Current HEAD commit
- `index_state` - Indexing state (Pending, InProgress, Completed, Failed, Stale)
- `indexed_commit_sha` - Last successfully indexed commit

#### commits
Stores commit metadata for indexed commits.
- `id` - Unique identifier
- `repo_id` - Foreign key to repos
- `sha` - Git commit SHA
- `branch_name` - Branch where this commit was indexed
- `author_name`, `author_email` - Commit author
- `commit_message` - Commit message

#### files
Stores file metadata for indexed files.
- `id` - Unique identifier
- `repo_id` - Foreign key to repos
- `file_path` - File path relative to repository root
- `language` - Detected programming language
- `size_bytes`, `line_count` - File statistics

#### symbols
Stores extracted code symbols (classes, functions, etc.).
- `id` - Unique identifier
- `name` - Symbol name
- `qualified_name` - Fully qualified name (e.g., Namespace.Class.Method)
- `kind` - Symbol kind (Class, Method, Function, etc.)
- `parent_symbol_id` - Parent symbol (e.g., class for a method)
- `signature` - Method/function signature
- `documentation` - Documentation/comments

#### edges
Stores relationships between symbols.
- `id` - Unique identifier
- `source_symbol_id` - Source symbol
- `target_symbol_id` - Target symbol
- `kind` - Edge kind (Calls, References, Inherits, Implements, etc.)

#### code_chunks
Stores code chunks for embedding and search.
- `id` - Unique identifier
- `content` - Code content with context lines
- `symbol_id` - Associated symbol
- `start_line`, `end_line` - Symbol boundaries
- `chunk_start_line`, `chunk_end_line` - Chunk boundaries (including context)
- `token_count` - Approximate token count

#### embeddings
Stores vector embeddings for code chunks.
- `id` - Unique identifier
- `chunk_id` - Foreign key to code_chunks
- `vector` - Embedding vector (768 dimensions)
- `model` - Model used to generate embedding
- `model_version` - Model version

### Indexes

#### Full-Text Search (FTS)
- `idx_chunks_content_fts` - GIN index on code_chunks.content for BM25-style ranking

#### Vector Search (HNSW)
- `idx_embeddings_vector_hnsw` - HNSW index on embeddings.vector for fast approximate nearest neighbor search
  - `m=16` - Number of connections per layer
  - `ef_construction=64` - Size of dynamic candidate list

#### Fuzzy Search (Trigram)
- `idx_files_file_path_trgm` - GIN index on files.file_path for fuzzy file path search
- `idx_symbols_name_trgm` - GIN index on symbols.name for fuzzy symbol name search

#### Graph Traversal
- `idx_edges_source_symbol_id` - B-tree index for outgoing edges
- `idx_edges_target_symbol_id` - B-tree index for incoming edges
- `idx_edges_composite` - GIN index for multi-column queries

## Search Functions

### Full-Text Search
```sql
SELECT * FROM search_chunks_fulltext(
    query_text := 'authentication',
    repo_filter := 'my-repo',
    branch_filter := 'main',
    limit_count := 10
);
```

### Vector Search
```sql
SELECT * FROM search_embeddings_cosine(
    query_vector := '[0.1, 0.2, ...]'::vector(768),
    repo_filter := 'my-repo',
    branch_filter := 'main',
    limit_count := 10
);
```

### Hybrid Search (BM25 + Vector)
```sql
SELECT * FROM hybrid_search(
    query_text := 'authentication',
    query_vector := '[0.1, 0.2, ...]'::vector(768),
    repo_filter := 'my-repo',
    bm25_weight := 0.3,
    vector_weight := 0.7,
    limit_count := 10
);
```

### Symbol Search
```sql
SELECT * FROM search_symbols(
    query_text := 'UserService',
    repo_filter := 'my-repo',
    fuzzy := true,
    limit_count := 10
);
```

### Graph Traversal
```sql
-- Find all references to a symbol
SELECT * FROM find_references(
    target_symbol_id := 'symbol-id',
    edge_kind_filter := 'Calls'
);

-- Find all dependencies of a symbol
SELECT * FROM find_dependencies(
    source_symbol_id := 'symbol-id'
);

-- Find call chain
SELECT * FROM find_call_chain(
    start_symbol_id := 'symbol-id',
    max_depth := 5
);
```

## Materialized Views

Materialized views cache complex query results for faster access. They need to be refreshed periodically.

### Available Views
- `repo_stats` - Repository statistics
- `branch_stats` - Branch statistics
- `language_stats` - Language distribution per repository
- `symbol_kind_stats` - Symbol kind distribution
- `edge_kind_stats` - Edge kind distribution
- `hot_symbols` - Most referenced symbols
- `recent_activity` - Recent commit activity
- `indexing_progress` - Indexing progress per repository

### Refresh Views
```sql
-- Refresh all views
SELECT refresh_all_stats();

-- Refresh a specific view
SELECT refresh_stats('repo_stats');
```

## Performance Tuning

### HNSW Index Parameters
The HNSW index is configured with:
- `m=16` - Good balance between recall and memory usage
- `ef_construction=64` - Good balance between index quality and build time

For better recall (slower build, more memory):
```sql
CREATE INDEX idx_embeddings_vector_hnsw ON embeddings USING hnsw (vector vector_cosine_ops)
WITH (m = 32, ef_construction = 128);
```

For faster build (lower recall):
```sql
CREATE INDEX idx_embeddings_vector_hnsw ON embeddings USING hnsw (vector vector_cosine_ops)
WITH (m = 8, ef_construction = 32);
```

### Query Performance
```sql
-- Set ef_search for query time (higher = better recall, slower)
SET hnsw.ef_search = 100;

-- Check query plan
EXPLAIN ANALYZE SELECT * FROM search_embeddings_cosine(...);
```

## Maintenance

### Vacuum and Analyze
```sql
-- Vacuum all tables
VACUUM ANALYZE;

-- Vacuum specific table
VACUUM ANALYZE embeddings;
```

### Reindex
```sql
-- Reindex all indexes
REINDEX DATABASE project_indexer;

-- Reindex specific index
REINDEX INDEX idx_embeddings_vector_hnsw;
```

### Monitor Index Usage
```sql
SELECT 
    schemaname,
    tablename,
    indexname,
    idx_scan,
    idx_tup_read,
    idx_tup_fetch
FROM pg_stat_user_indexes
ORDER BY idx_scan DESC;
```

## Troubleshooting

### pgvector not found
```bash
# Check if pgvector is installed
psql -c "SELECT * FROM pg_available_extensions WHERE name = 'vector';"

# If not, install it
sudo apt-get install postgresql-16-pgvector
```

### Migration fails
```bash
# Drop database and start over
psql -U postgres -c "DROP DATABASE project_indexer;"
./migrate.sh
```

### Slow queries
```sql
-- Enable query logging
ALTER DATABASE project_indexer SET log_min_duration_statement = 1000;

-- Check slow queries
SELECT * FROM pg_stat_statements ORDER BY total_exec_time DESC LIMIT 10;
```

## Next Steps

After setting up the database:
1. Configure the C# application to connect to PostgreSQL
2. Implement the storage layer (Step 4 in ARCHITECTURE.md)
3. Implement the embedding service (Step 5)
4. Implement the query orchestrator (Step 6)
5. Test hybrid search functionality

## References

- [PostgreSQL Full-Text Search](https://www.postgresql.org/docs/current/textsearch.html)
- [pgvector Documentation](https://github.com/pgvector/pgvector)
- [HNSW Algorithm](https://arxiv.org/abs/1603.09320)
- [BM25 Ranking](https://en.wikipedia.org/wiki/Okapi_BM25)

